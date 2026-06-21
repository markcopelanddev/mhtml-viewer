using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MhtmlViewer;

public sealed class MainForm : Form
{
    private readonly Button _chooseFolderButton = new() { Text = "Select Folder", AutoSize = true };
    private readonly Button _selectFileButton = new() { Text = "Select File", AutoSize = true };
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
    private readonly TrackBar _fileSlider = new()
    {
        Dock = DockStyle.Fill,
        TickStyle = TickStyle.None,
        Minimum = 0,
        Maximum = 0,
        Value = 0,
        SmallChange = 1,
        LargeChange = 1
    };
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NaturalFileComparer _fileComparer = new();

    private List<string> _files = [];
    private string? _folderPath;
    private int _currentIndex = -1;
    private int _zoomPercent = 100;
    private bool _webViewReady;
    private bool _updatingSlider;

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
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
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
            _selectFileButton,
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

        var sliderPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 0, 8, 0)
        };
        sliderPanel.Controls.Add(_fileSlider);

        rootLayout.Controls.Add(topPanel, 0, 0);
        rootLayout.Controls.Add(_viewer, 0, 1);
        rootLayout.Controls.Add(sliderPanel, 0, 2);
        rootLayout.Controls.Add(_statusStrip, 0, 3);

        Controls.Add(rootLayout);
    }

    private void WireEvents()
    {
        _chooseFolderButton.Click += (_, _) => ChooseFolder();
        _selectFileButton.Click += (_, _) => ChooseFile();
        _previousButton.Click += (_, _) => MoveBy(-1);
        _nextButton.Click += (_, _) => MoveBy(1);
        _zoomOutFiveButton.Click += (_, _) => AdjustZoom(-5);
        _zoomOutOneButton.Click += (_, _) => AdjustZoom(-1);
        _zoomResetButton.Click += (_, _) => SetZoom(100);
        _zoomInOneButton.Click += (_, _) => AdjustZoom(1);
        _zoomInFiveButton.Click += (_, _) => AdjustZoom(5);
        _fileSlider.ValueChanged += (_, _) => SliderValueChanged();
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

    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select a PDF, MHT, or MHTML file",
            Filter = "Supported files (*.pdf;*.mht;*.mhtml)|*.pdf;*.mht;*.mhtml|PDF files (*.pdf)|*.pdf|MHTML files (*.mht;*.mhtml)|*.mht;*.mhtml",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = _folderPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!IsSupportedFile(dialog.FileName))
        {
            MessageBox.Show(this, "Select a PDF, MHT, or MHTML file.", "Unsupported file", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var folderPath = Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        LoadFolder(folderPath, dialog.FileName);
    }

    private void LoadFolder(string folderPath, string? selectedFilePath = null)
    {
        _folderPath = folderPath;
        _files = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedFile)
            .OrderBy(path => path, _fileComparer)
            .ToList();

        if (selectedFilePath is not null)
        {
            var selectedFullPath = Path.GetFullPath(selectedFilePath);
            _currentIndex = _files.FindIndex(path =>
                string.Equals(Path.GetFullPath(path), selectedFullPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _currentIndex = _files.Count > 0 ? 0 : -1;
        }

        if (_currentIndex < 0 && _files.Count > 0)
        {
            _currentIndex = 0;
        }

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

        JumpToIndex(_currentIndex + delta);
    }

    private void JumpToIndex(int index)
    {
        if (_files.Count == 0)
        {
            return;
        }

        var nextIndex = Math.Clamp(index, 0, _files.Count - 1);
        if (nextIndex == _currentIndex)
        {
            return;
        }

        _currentIndex = nextIndex;
        UpdateUi();
        NavigateCurrent();
    }

    private void SliderValueChanged()
    {
        if (_updatingSlider)
        {
            return;
        }

        JumpToIndex(_fileSlider.Value);
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
        UpdateSlider();

        _folderLabel.Text = _folderPath is null
            ? "No folder selected"
            : _folderPath;

        _fileLabel.Text = _currentIndex >= 0
            ? $"{_currentIndex + 1} of {_files.Count}: {Path.GetFileName(_files[_currentIndex])}"
            : "No file selected";
    }

    private void UpdateSlider()
    {
        _updatingSlider = true;
        try
        {
            if (_files.Count == 0)
            {
                if (_fileSlider.Value != 0)
                {
                    _fileSlider.Value = 0;
                }

                _fileSlider.Maximum = 0;
                _fileSlider.Enabled = false;
                return;
            }

            var maximum = _files.Count - 1;
            if (_fileSlider.Maximum < maximum)
            {
                _fileSlider.Maximum = maximum;
            }

            if (_fileSlider.Value > maximum)
            {
                _fileSlider.Value = maximum;
            }

            _fileSlider.Maximum = maximum;
            _fileSlider.TickFrequency = Math.Max(1, _files.Count / 20);
            _fileSlider.LargeChange = Math.Max(1, _files.Count / 10);
            _fileSlider.SmallChange = 1;
            _fileSlider.Value = Math.Clamp(_currentIndex, 0, maximum);
            _fileSlider.Enabled = _files.Count > 1;
        }
        finally
        {
            _updatingSlider = false;
        }
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && (e.KeyCode == Keys.O || e.KeyCode == Keys.F))
        {
            if (e.KeyCode == Keys.F)
            {
                ChooseFile();
            }
            else
            {
                ChooseFolder();
            }

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
