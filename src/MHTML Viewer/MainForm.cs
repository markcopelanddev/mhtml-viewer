using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MhtmlViewer;

internal enum FileSortMode
{
    Name,
    DateModified,
    Type,
    Size,
    DateCreated,
    Authors,
    Categories,
    Tags,
    Title
}

internal enum FileSortDirection
{
    Ascending,
    Descending
}

public sealed class MainForm : Form
{
    private static readonly SortModeOption[] SortModeOptions =
    [
        new(FileSortMode.Name, "Name"),
        new(FileSortMode.DateModified, "Date modified"),
        new(FileSortMode.Type, "Type"),
        new(FileSortMode.Size, "Size"),
        new(FileSortMode.DateCreated, "Date created"),
        new(FileSortMode.Authors, "Authors"),
        new(FileSortMode.Categories, "Categories"),
        new(FileSortMode.Tags, "Tags"),
        new(FileSortMode.Title, "Title")
    ];

    private static readonly SortDirectionOption[] SortDirectionOptions =
    [
        new(FileSortDirection.Ascending, "Ascending"),
        new(FileSortDirection.Descending, "Descending")
    ];

    private static readonly Regex HtmlTitleRegex = new(
        @"<title[^>]*>(?<value>.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlMetaTagRegex = new(
        @"<meta\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PdfInfoValueRegex = new(
        @"/(?<name>Title|Author|Subject|Keywords)\s*\((?<value>(?:\\.|[^\\)])*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly Button _chooseFolderButton = new() { Text = "Select Folder", AutoSize = true };
    private readonly Button _selectFileButton = new() { Text = "Select File", AutoSize = true };
    private readonly Label _sortLabel = new() { Text = "Sort by", AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(8, 6, 0, 0) };
    private readonly ComboBox _sortModeComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox _sortDirectionComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
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
    private readonly Dictionary<string, FileMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    private List<string> _files = [];
    private string? _folderPath;
    private int _currentIndex = -1;
    private int _zoomPercent = 100;
    private FileSortMode _sortMode = FileSortMode.Name;
    private FileSortDirection _sortDirection = FileSortDirection.Ascending;
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
        ConfigureSortControls();

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
            WrapContents = true
        };

        commandRow.Controls.AddRange([
            _chooseFolderButton,
            _selectFileButton,
            _sortLabel,
            _sortModeComboBox,
            _sortDirectionComboBox,
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

    private void ConfigureSortControls()
    {
        _sortModeComboBox.Items.AddRange(SortModeOptions);
        _sortModeComboBox.SelectedIndex = 0;
        _sortDirectionComboBox.Items.AddRange(SortDirectionOptions);
        _sortDirectionComboBox.SelectedIndex = 0;
    }

    private void WireEvents()
    {
        _chooseFolderButton.Click += (_, _) => ChooseFolder();
        _selectFileButton.Click += (_, _) => ChooseFile();
        _sortModeComboBox.SelectedIndexChanged += (_, _) => SortSelectionChanged();
        _sortDirectionComboBox.SelectedIndexChanged += (_, _) => SortSelectionChanged();
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
        _metadataCache.Clear();
        _files = Directory
            .EnumerateFiles(folderPath)
            .Where(IsSupportedFile)
            .ToList();
        SortFiles();

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

    private void SortSelectionChanged()
    {
        if (_sortModeComboBox.SelectedItem is SortModeOption selectedMode)
        {
            _sortMode = selectedMode.Mode;
        }

        if (_sortDirectionComboBox.SelectedItem is SortDirectionOption selectedDirection)
        {
            _sortDirection = selectedDirection.Direction;
        }

        var currentFile = CurrentFilePath;
        SortFiles();
        RestoreCurrentIndex(currentFile);
        UpdateUi();

        if (_folderPath is not null)
        {
            SetStatus($"Sorted by {GetSortDescription()}");
        }
    }

    private string? CurrentFilePath =>
        _currentIndex >= 0 && _currentIndex < _files.Count
            ? _files[_currentIndex]
            : null;

    private void SortFiles()
    {
        _files.Sort(CompareFiles);
    }

    private void RestoreCurrentIndex(string? currentFile)
    {
        if (_files.Count == 0)
        {
            _currentIndex = -1;
            return;
        }

        if (currentFile is null)
        {
            _currentIndex = Math.Clamp(_currentIndex, 0, _files.Count - 1);
            return;
        }

        var currentFullPath = Path.GetFullPath(currentFile);
        var updatedIndex = _files.FindIndex(path =>
            string.Equals(Path.GetFullPath(path), currentFullPath, StringComparison.OrdinalIgnoreCase));
        _currentIndex = updatedIndex >= 0
            ? updatedIndex
            : Math.Clamp(_currentIndex, 0, _files.Count - 1);
    }

    private int CompareFiles(string left, string right)
    {
        var primaryCompare = CompareFilesBySelectedMode(left, right);
        if (_sortDirection == FileSortDirection.Descending)
        {
            primaryCompare = -primaryCompare;
        }

        if (primaryCompare != 0)
        {
            return primaryCompare;
        }

        return _fileComparer.Compare(left, right);
    }

    private int CompareFilesBySelectedMode(string left, string right)
    {
        return _sortMode switch
        {
            FileSortMode.Name => _fileComparer.Compare(left, right),
            FileSortMode.DateModified => GetLastWriteTime(left).CompareTo(GetLastWriteTime(right)),
            FileSortMode.Type => CompareText(GetFileTypeLabel(left), GetFileTypeLabel(right)),
            FileSortMode.Size => GetFileSize(left).CompareTo(GetFileSize(right)),
            FileSortMode.DateCreated => GetCreationTime(left).CompareTo(GetCreationTime(right)),
            FileSortMode.Authors => CompareText(GetMetadata(left).Authors, GetMetadata(right).Authors),
            FileSortMode.Categories => CompareText(GetMetadata(left).Categories, GetMetadata(right).Categories),
            FileSortMode.Tags => CompareText(GetMetadata(left).Tags, GetMetadata(right).Tags),
            FileSortMode.Title => CompareText(GetMetadata(left).Title, GetMetadata(right).Title),
            _ => _fileComparer.Compare(left, right)
        };
    }

    private int CompareText(string? left, string? right)
    {
        return _fileComparer.CompareText(left ?? string.Empty, right ?? string.Empty);
    }

    private static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mht", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mhtml", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime GetLastWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTime(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static DateTime GetCreationTime(string path)
    {
        try
        {
            return File.GetCreationTime(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetFileTypeLabel(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "PDF",
            ".mht" => "MHTML",
            ".mhtml" => "MHTML",
            var extension when !string.IsNullOrWhiteSpace(extension) => extension.TrimStart('.').ToUpperInvariant(),
            _ => "File"
        };
    }

    private FileMetadata GetMetadata(string path)
    {
        if (_metadataCache.TryGetValue(path, out var metadata))
        {
            return metadata;
        }

        metadata = ReadMetadata(path);
        _metadataCache[path] = metadata;
        return metadata;
    }

    private static FileMetadata ReadMetadata(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ReadPdfMetadata(path);
        }

        if (extension.Equals(".mht", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mhtml", StringComparison.OrdinalIgnoreCase))
        {
            return ReadHtmlMetadata(path);
        }

        return new FileMetadata(Path.GetFileNameWithoutExtension(path), string.Empty, string.Empty, string.Empty);
    }

    private static FileMetadata ReadHtmlMetadata(string path)
    {
        var html = NormalizePotentialQuotedPrintable(ReadTextSample(path, includeTail: false));
        var title = FirstNonEmpty(
            CleanMetadataValue(HtmlTitleRegex.Match(html).Groups["value"].Value),
            FindMetaContent(html, "title", "og:title", "twitter:title"),
            Path.GetFileNameWithoutExtension(path));
        var authors = FindMetaContent(html, "author", "dc.creator", "article:author", "byl");
        var categories = FindMetaContent(html, "category", "article:section", "section");
        var tags = FindMetaContent(html, "keywords", "news_keywords", "article:tag");

        return new FileMetadata(title, authors, categories, tags);
    }

    private static FileMetadata ReadPdfMetadata(string path)
    {
        var sample = ReadTextSample(path, includeTail: true);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PdfInfoValueRegex.Matches(sample))
        {
            var name = match.Groups["name"].Value;
            if (!values.ContainsKey(name))
            {
                values[name] = CleanMetadataValue(UnescapePdfString(match.Groups["value"].Value));
            }
        }

        return new FileMetadata(
            FirstNonEmpty(GetMetadataValue(values, "Title"), Path.GetFileNameWithoutExtension(path)),
            GetMetadataValue(values, "Author"),
            GetMetadataValue(values, "Subject"),
            GetMetadataValue(values, "Keywords"));
    }

    private static string GetMetadataValue(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string ReadTextSample(string path, bool includeTail)
    {
        const int sampleBytes = 1_048_576;

        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length <= sampleBytes || !includeTail)
            {
                return ReadChunk(stream, (int)Math.Min(sampleBytes, stream.Length));
            }

            var firstChunk = ReadChunk(stream, sampleBytes);
            stream.Seek(Math.Max(0, stream.Length - sampleBytes), SeekOrigin.Begin);
            var lastChunk = ReadChunk(stream, sampleBytes);
            return firstChunk + Environment.NewLine + lastChunk;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadChunk(Stream stream, int byteCount)
    {
        if (byteCount <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[byteCount];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);
        return DecodeText(buffer.AsSpan(0, bytesRead));
    }

    private static string DecodeText(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(buffer[3..]);
        }

        if (buffer.Length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(buffer[2..]);
        }

        if (buffer.Length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(buffer[2..]);
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static string NormalizePotentialQuotedPrintable(string value)
    {
        return value
            .Replace("=\r\n", string.Empty, StringComparison.Ordinal)
            .Replace("=\n", string.Empty, StringComparison.Ordinal)
            .Replace("=3D", "=", StringComparison.OrdinalIgnoreCase)
            .Replace("=22", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("=27", "'", StringComparison.OrdinalIgnoreCase)
            .Replace("=20", " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindMetaContent(string html, params string[] names)
    {
        foreach (Match match in HtmlMetaTagRegex.Matches(html))
        {
            var tag = match.Value;
            var name = FirstNonEmpty(GetHtmlAttribute(tag, "name"), GetHtmlAttribute(tag, "property"));
            if (!names.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var content = CleanMetadataValue(GetHtmlAttribute(tag, "content"));
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return string.Empty;
    }

    private static string GetHtmlAttribute(string tag, string attributeName)
    {
        var match = Regex.Match(
            tag,
            $@"\b{Regex.Escape(attributeName)}\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return CleanMetadataValue(match.Success ? match.Groups["value"].Value : string.Empty);
    }

    private static string UnescapePdfString(string value)
    {
        return value
            .Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\\r", " ", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal);
    }

    private static string CleanMetadataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(WebUtility.HtmlDecode(value), @"\s+", " ").Trim();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
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

    private string GetSortDescription()
    {
        var mode = SortModeOptions.First(option => option.Mode == _sortMode).Label;
        var direction = SortDirectionOptions.First(option => option.Direction == _sortDirection).Label.ToLowerInvariant();
        return $"{mode} ({direction})";
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

    private sealed record SortModeOption(FileSortMode Mode, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed record SortDirectionOption(FileSortDirection Direction, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private sealed record FileMetadata(string Title, string Authors, string Categories, string Tags);
}
