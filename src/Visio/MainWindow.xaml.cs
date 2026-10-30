using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Visio;

public partial class MainWindow : Window
{
    // The renderer is served from the app folder; documents come from DocServer.
    private const string AppHost = "app.visio";
    private static readonly string AppOrigin = $"https://{AppHost}/";

    private static readonly string OpenFilter = string.Join("|",
        "Усі підтримувані|*.pdf;*.docx;*.docm;*.dotx;*.doc;*.rtf;*.odt;*.xlsx;*.xlsm;*.xlsb;*.xls;*.ods;*.csv;*.tsv;*.pptx;*.ppsx;*.ppt;*.odp;*.md;*.txt;*.log;*.json;*.xml;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.svg;*.mp4;*.webm;*.mp3;*.wav",
        "Документи|*.pdf;*.docx;*.docm;*.dotx;*.doc;*.rtf;*.odt;*.md;*.txt",
        "Таблиці|*.xlsx;*.xlsm;*.xlsb;*.xls;*.ods;*.csv;*.tsv",
        "Презентації|*.pptx;*.ppsx;*.ppt;*.odp",
        "Зображення|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.svg;*.ico",
        "Усі файли|*.*");

    private DocServer? _docs;
    private string? _pendingPath;
    private string? _currentPath;
    private int _openSeq;
    private bool _pageReady;
    private WindowState _restoreState;
    private WindowStyle _restoreStyle;

    public MainWindow(string? path)
    {
        InitializeComponent();
        _pendingPath = path;
        Background = new SolidColorBrush(SystemTheme.IsDark ? Color.FromRgb(0x1e, 0x1e, 0x1e) : Color.FromRgb(0xf3, 0xf3, 0xf3));
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) => await InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        CoreWebView2Environment env;
        try
        {
            // Keep the browser profile out of the install folder, which may be read-only.
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Visio", "WebView2");
            env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Не знайдено Microsoft Edge WebView2 Runtime.\nВстановіть його з https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                "Visio", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        var core = Web.CoreWebView2;
        var s = core.Settings;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false; // the page zooms its own content
        s.AreHostObjectsAllowed = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
#if !DEBUG
        s.AreDevToolsEnabled = false;
#endif

        var viewerDir = Path.Combine(AppContext.BaseDirectory, "viewer");
        core.SetVirtualHostNameToFolderMapping(AppHost, viewerDir, CoreWebView2HostResourceAccessKind.Allow);
        _docs = new DocServer(core);
        core.WebMessageReceived += OnWebMessage;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.Navigate(AppOrigin + "index.html");
    }

    // ---------------------------------------------------------------------
    // Page <-> host messages
    // ---------------------------------------------------------------------
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? type;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return;
        }

        switch (type)
        {
            case "ready":
                _pageReady = true;
                // First load opens the command-line file; a reload (F5) reopens the current one.
                var path = _pendingPath ?? _currentPath;
                _pendingPath = null;
                if (path != null) OpenFile(path);
                break;
            case "pick":
                PickFile();
                break;
            case "open-dropped":
                if (e.AdditionalObjects?.FirstOrDefault() is CoreWebView2File file) OpenFile(file.Path);
                break;
            case "fullscreen":
                ToggleFullscreen();
                break;
            case "exit-fullscreen":
                if (WindowStyle == WindowStyle.None) ToggleFullscreen();
                break;
        }
    }

    private void Post(object message) =>
        Web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    private void PickFile()
    {
        var dlg = new OpenFileDialog { Filter = OpenFilter, Title = "Відкрити документ" };
        if (_currentPath != null) dlg.InitialDirectory = Path.GetDirectoryName(_currentPath);
        if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
    }

    // ---------------------------------------------------------------------
    // Opening files
    // ---------------------------------------------------------------------
    public async void OpenFile(string path)
    {
        if (!_pageReady)
        {
            _pendingPath = path;
            return;
        }

        // Files dropped from a zip or a browser have no path on disk.
        if (string.IsNullOrWhiteSpace(path))
        {
            Post(new { type = "error", title = "Цей файл не можна відкрити напряму", detail = "Спочатку збережіть його на диск." });
            return;
        }
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Post(new { type = "error", title = "Некоректний шлях", detail = path });
            return;
        }
        if (!File.Exists(path))
        {
            Post(new { type = "error", title = "Файл не знайдено", detail = path });
            return;
        }

        var seq = ++_openSeq;
        var name = Path.GetFileName(path);
        _currentPath = path;
        Title = $"{name} — Visio";

        if (!Converter.Needs(path))
        {
            ShowDocument(path, name, null);
            return;
        }

        var soffice = Converter.FindLibreOffice();
        if (soffice == null)
        {
            Post(new
            {
                type = "error",
                title = $"Для формату «{Path.GetExtension(path)}» потрібен LibreOffice",
                detail = "Встановіть безкоштовний LibreOffice (libreoffice.org) — Visio використає його для перетворення файлу на PDF.",
            });
            return;
        }

        Post(new { type = "status", title = "Перетворення документа…", detail = name });
        try
        {
            var pdf = await Converter.ToPdfAsync(path, soffice);
            if (seq == _openSeq) ShowDocument(pdf, name, "pdf");
        }
        catch (Exception ex)
        {
            if (seq == _openSeq) Post(new { type = "error", title = "Не вдалося відкрити файл", detail = ex.Message });
        }
    }

    /// <summary>Shows <paramref name="servedPath"/> under the original file's name; <paramref name="format"/> overrides the renderer.</summary>
    private void ShowDocument(string servedPath, string displayName, string? format) =>
        Post(new { type = "open", name = displayName, url = _docs!.Register(servedPath), format });

    // ---------------------------------------------------------------------
    // Links inside documents open in the default browser, never in the app.
    // ---------------------------------------------------------------------
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith(AppOrigin, StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private static void OpenExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return;
        if (u.Scheme is not ("http" or "https" or "mailto")) return;
        Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
    }

    // ---------------------------------------------------------------------
    // Fullscreen (F11)
    // ---------------------------------------------------------------------
    private void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = _restoreStyle;
            WindowState = _restoreState;
        }
        else
        {
            _restoreStyle = WindowStyle;
            _restoreState = WindowState;
            WindowStyle = WindowStyle.None;
            // Re-maximize so the borderless window covers the taskbar.
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
    }
}

internal static class SystemTheme
{
    public static bool IsDark
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void ApplyTitleBar(IntPtr hwnd)
    {
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}
