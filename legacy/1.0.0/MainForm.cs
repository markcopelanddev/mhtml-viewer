using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MhtmlViewer;

public sealed class MainForm : Form
{
    private readonly Button _chooseFolderButton = new() { Text = "Select Folder", AutoSize = true };
    private readonly Button _previousButton = new() { Text = "Previous", AutoSize = true };
    private readonly Button _nextButton = new() { Text = "Next", AutoSize = true };
    private readonly Button _zoomOutFiveButton = new() { Text = "-5%", AutoSize = true };
    private readonly Button _zoomOutOneButton = new() { Text = "-1%", AutoSize = true };
    private readonly Button _zoomResetButton = new() { Text = "100%", AutoSize = true };
    private readonly Button _zoomInOneButton = new() { Text = "+1%", AutoSize = true };
    private readonly Button _zoomInFiveButton = new() { Text = "+5%", AutoSize = true };
    private readonly Label _fileLabel = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
    private readonly Label _folderLabel = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
    private readonly WebView2 _viewer = new() { Dock = DockStyle.Fill };
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NaturalFileComparer _fileComparer = new();

    private List<string> _files = [];
    private string? _folderPath;
    private int _currentIndex = -1;
    private int _zoomPercent = 100;
    private bool _webViewReady;

    public MainForm()
    {
        Text = "MHTML Viewer";
        Width = 1280;
        Height = 860;
        MinimumSize = new Size(820, 540);
        KeyPreview = true;

        BuildLayout();
        WireEvents();
        UpdateUi();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await InitializeWebViewAsync();
    }

    private void BuildLayout()
    {
        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };

        var commandRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        commandRow.Controls.AddRange([
            _chooseFolderButton,
            _previousButton,
            _nextButton,
            _zoomOutFiveButton,
            _zoomOutOneButton,
            _zoomResetButton,
            _zoomInOneButton,
            _zoomInFiveButton
        ]);

        topPanel.Controls.Add(commandRow, 0, 0);
        topPanel.Controls.Add(_folderLabel, 0, 1);
        topPanel.Controls.Add(_fileLabel, 0, 2);

        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_viewer);
        Controls.Add(_statusStrip);
        Controls.Add(topPanel);
    }

    private void WireEvents()
    {
        _chooseFolderButton.Click += (_, _) => ChooseFolder();
        _previousButton.Click += (_, _) => MoveBy(-1);
        _nextButton.Click += (_, _) => MoveBy(1);
        _zoomOutFiveButton.Click += (_, _) => AdjustZoom(-5);
        _zoomOutOneButton.Click += (_, _) => AdjustZoom(-1);
        _zoomResetButton.Click += (_, _) => SetZoom(100);
        _zoomInOneButton.Click += (_, _) => AdjustZoom(1);
        _zoomInFiveButton.Click += (_, _) => AdjustZoom(5);
        _viewer.NavigationStarting += (_, _) => SetStatus("Loading...");
        _viewer.NavigationCompleted += (_, args) => SetStatus(args.IsSuccess ? "Ready" : $"Load failed: {args.WebErrorStatus}");
        KeyDown += MainForm_KeyDown;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHTML Viewer",
                "WebView2");
            Directory.CreateDirectory(dataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await _viewer.EnsureCoreWebView2Async(environment);
            _viewer.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _viewer.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webViewReady = true;
            ApplyZoom();
            SetStatus("Select a folder to begin.");
            NavigateCurrent();
        }
        catch (Exception ex)
        {
            SetStatus($"WebView2 failed to start: {ex.Message}");
        }
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder containing PDF, MHT, or MHTML files.",
            UseDescriptionForTitle = true,
            SelectedPath = _folderPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        LoadFolder(dialog.SelectedPath);
    }

    private void LoadFolder(string folderPath)
    {
        _folderPath = folderPath;
        _files = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedFile)
            .OrderBy(path => path, _fileComparer)
            .ToList();
        _currentIndex = _files.Count > 0 ? 0 : -1;
        UpdateUi();
        NavigateCurrent();
    }

    private static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mht", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mhtml", StringComparison.OrdinalIgnoreCase);
    }

    private void MoveBy(int delta)
    {
        if (_files.Count == 0)
        {
            return;
        }

        var nextIndex = Math.Clamp(_currentIndex + delta, 0, _files.Count - 1);
        if (nextIndex == _currentIndex)
        {
            return;
        }

        _currentIndex = nextIndex;
        UpdateUi();
        NavigateCurrent();
    }

    private void NavigateCurrent()
    {
        if (!_webViewReady || _currentIndex < 0 || _currentIndex >= _files.Count)
        {
            if (_files.Count == 0 && _webViewReady)
            {
                _viewer.CoreWebView2.NavigateToString("<html><body style=\"font-family:Segoe UI,sans-serif;padding:24px\">No PDF, MHT, or MHTML files found.</body></html>");
            }

            return;
        }

        var path = _files[_currentIndex];
        _viewer.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
        ApplyZoom();
        SetStatus($"Loaded {Path.GetFileName(path)}");
    }

    private void AdjustZoom(int delta)
    {
        SetZoom(_zoomPercent + delta);
    }

    private void SetZoom(int zoomPercent)
    {
        _zoomPercent = Math.Clamp(zoomPercent, 25, 500);
        ApplyZoom();
        UpdateUi();
    }

    private void ApplyZoom()
    {
        if (_webViewReady)
        {
            _viewer.ZoomFactor = _zoomPercent / 100.0;
        }
    }

    private void UpdateUi()
    {
        _previousButton.Enabled = _currentIndex > 0;
        _nextButton.Enabled = _currentIndex >= 0 && _currentIndex < _files.Count - 1;
        _zoomResetButton.Text = $"{_zoomPercent}%";

        _folderLabel.Text = _folderPath is null
            ? "No folder selected"
            : _folderPath;

        _fileLabel.Text = _currentIndex >= 0
            ? $"{_currentIndex + 1} of {_files.Count}: {Path.GetFileName(_files[_currentIndex])}"
            : "No file selected";
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && (e.KeyCode == Keys.O || e.KeyCode == Keys.F))
        {
            ChooseFolder();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.PageUp)
        {
            MoveBy(-1);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Space)
        {
            MoveBy(1);
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Oemplus)
        {
            AdjustZoom(e.Shift ? 5 : 1);
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.OemMinus)
        {
            AdjustZoom(e.Shift ? -5 : -1);
            e.Handled = true;
        }
    }
}
