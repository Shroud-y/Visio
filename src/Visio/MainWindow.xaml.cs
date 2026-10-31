using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Visio;

public partial class MainWindow : Window
{
    // The renderer is served from the app folder; documents come from DocServer.
    private const string AppHost = "app.visio";
    private static readonly string AppOrigin = $"https://{AppHost}/";

    private static readonly string OpenFilter = string.Join("|",
        "All supported|*.pdf;*.docx;*.docm;*.dotx;*.doc;*.rtf;*.odt;*.xlsx;*.xlsm;*.xlsb;*.xls;*.ods;*.csv;*.tsv;*.pptx;*.ppsx;*.ppt;*.odp;*.md;*.txt;*.log;*.json;*.xml;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.svg;*.mp4;*.webm;*.mp3;*.wav",
        "Documents|*.pdf;*.docx;*.docm;*.dotx;*.doc;*.rtf;*.odt;*.md;*.txt",
        "Spreadsheets|*.xlsx;*.xlsm;*.xlsb;*.xls;*.ods;*.csv;*.tsv",
        "Presentations|*.pptx;*.ppsx;*.ppt;*.odp",
        "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.svg;*.ico",
        "All files|*.*");

    private static readonly string AppVersion = UpdateCheck.Current.ToString(3);

    private readonly Settings _settings;
    private bool _startupDone;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadTimer;
    private UpdateCheck.Release? _update;
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
        _settings = Settings.Load();
        RestorePlacement();
        Background = new SolidColorBrush(SystemTheme.IsDark ? Color.FromRgb(0x1e, 0x1e, 0x1e) : Color.FromRgb(0xf3, 0xf3, 0xf3));
        SourceInitialized += (_, _) => SystemTheme.ApplyTitleBar(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) => await InitWebViewAsync();
        Closing += (_, _) => SavePlacement();
    }

    // ---------------------------------------------------------------------
    // Window placement: reopen where the user left the window.
    // ---------------------------------------------------------------------
    private void RestorePlacement()
    {
        if (_settings is not { Left: double l, Top: double t, Width: double w, Height: double h }) return;
        if (w < MinWidth || h < MinHeight) return;

        // Skip positions on a monitor that is no longer connected: at least the
        // title bar must be on the virtual screen.
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(new Rect(l + 40, t, Math.Max(w - 80, 1), 30))) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = l;
        Top = t;
        Width = w;
        Height = h;
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        // In fullscreen, remember the state the window had before F11.
        var fullscreen = WindowStyle == WindowStyle.None;
        var maximized = fullscreen ? _restoreState == WindowState.Maximized : WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (bounds.IsEmpty || double.IsInfinity(bounds.Width)) return;

        Settings.Update(s =>
        {
            s.Left = bounds.Left;
            s.Top = bounds.Top;
            s.Width = bounds.Width;
            s.Height = bounds.Height;
            s.Maximized = maximized;
        });
    }

    private async Task InitWebViewAsync()
    {
        CoreWebView2Environment env;
        try
        {
            // Keep the browser profile out of the install folder, which may be read-only.
            env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.DataDir, "WebView2"));
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Microsoft Edge WebView2 Runtime was not found.\nInstall it from https://go.microsoft.com/fwlink/p/?LinkId=2124703",
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
                if (!_startupDone)
                {
                    _startupDone = true;
                    OnFirstReady();
                }
                break;
            case "menu":
                OnMenu(ReadString(e, "item"));
                break;
            case "banner-action":
                OnBannerAction(ReadString(e, "id"), ReadString(e, "action"));
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

    private static string? ReadString(CoreWebView2WebMessageReceivedEventArgs e, string name)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        return doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private void ShowBanner(string id, string text, string? detail, params (string Id, string Label)[] actions) =>
        Post(new { type = "banner", id, text, detail, actions = actions.Select(a => new { id = a.Id, label = a.Label }) });

    // ---------------------------------------------------------------------
    // Startup prompts, menu and banner actions
    // ---------------------------------------------------------------------
    private void OnFirstReady()
    {
        try
        {
            DefaultApp.RefreshIfMoved();
            if (_settings.AskDefaultApp && !DefaultApp.IsDefault())
            {
                ShowBanner("default", "Make Visio your default viewer for documents?", null,
                    ("set", "Set as default"), ("later", "Not now"), ("never", "Don't ask again"));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Registry locked down by policy: just don't offer.
        }
        _ = CheckForUpdatesAsync(manual: false);
    }

    private void OnMenu(string? item)
    {
        switch (item)
        {
            case "default":
                if (DefaultApp.IsDefault())
                {
                    ShowBanner("default", "Visio is already your default viewer.", null);
                }
                else
                {
                    MakeDefault();
                }
                break;
            case "updates":
                _ = CheckForUpdatesAsync(manual: true);
                break;
            case "about":
                ShowBanner("about", $"Visio {AppVersion}", "Read-only document viewer · MIT License", ("github", "GitHub page"));
                break;
        }
    }

    private void OnBannerAction(string? id, string? action)
    {
        switch (id, action)
        {
            case ("default", "set"):
                MakeDefault();
                break;
            case ("default", "never"):
                Settings.Update(s => s.AskDefaultApp = false);
                break;
            case ("about", "github"):
                OpenExternal(UpdateCheck.RepoUrl);
                break;
            case ("update", "download") when _update != null:
                OpenExternal(_update.Url);
                break;
            case ("update", "skip") when _update != null:
                Settings.Update(s => s.SkippedVersion = _update.Tag);
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Update check: once a day on startup, or on demand from the menu.
    // ---------------------------------------------------------------------
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual)
        {
            if (!_settings.CheckForUpdates) return;
            if (_settings.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromDays(1)) return;
        }
        if (manual) ShowBanner("update", "Checking for updates…", null);

        UpdateCheck.Release latest;
        try
        {
            latest = await UpdateCheck.GetLatestAsync();
        }
        catch (Exception ex)
        {
            // Offline at startup is normal; only report errors the user asked for.
            if (manual) ShowBanner("update", "Could not check for updates", ex.Message);
            return;
        }
        Settings.Update(s => s.LastUpdateCheck = DateTime.UtcNow);

        if (UpdateCheck.IsNewer(latest.Version) && (manual || _settings.SkippedVersion != latest.Tag))
        {
            _update = latest;
            ShowBanner("update", $"Visio {latest.Version.ToString(3)} is available", $"You have {AppVersion}",
                ("download", "Download"), ("skip", "Skip this version"));
        }
        else if (manual)
        {
            ShowBanner("update", "Visio is up to date", $"Version {AppVersion}");
        }
    }

    private void MakeDefault()
    {
        try
        {
            DefaultApp.RegisterAndOpenSettings();
        }
        catch (Exception ex)
        {
            ShowBanner("default", "Could not register Visio", ex.Message);
        }
    }

    private void PickFile()
    {
        var dlg = new OpenFileDialog { Filter = OpenFilter, Title = "Open document" };
        if (_currentPath != null) dlg.InitialDirectory = Path.GetDirectoryName(_currentPath);
        if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
    }

    // ---------------------------------------------------------------------
    // Opening files
    // ---------------------------------------------------------------------
    /// <param name="reload">The open file changed on disk: re-render keeping the view.</param>
    public async void OpenFile(string path, bool reload = false)
    {
        if (!_pageReady)
        {
            _pendingPath = path;
            return;
        }

        // Files dropped from a zip or a browser have no path on disk.
        if (string.IsNullOrWhiteSpace(path))
        {
            Post(new { type = "error", title = "This file can't be opened directly", detail = "Save it to disk first." });
            return;
        }
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Post(new { type = "error", title = "Invalid path", detail = path });
            return;
        }
        if (!File.Exists(path))
        {
            Post(new { type = "error", title = "File not found", detail = path });
            return;
        }

        var seq = ++_openSeq;
        var name = Path.GetFileName(path);
        _currentPath = path;
        Title = $"{name} — Visio";
        Watch(path);

        if (!Converter.Needs(path))
        {
            ShowDocument(path, name, null, reload);
            return;
        }

        var soffice = Converter.FindLibreOffice();
        if (soffice == null)
        {
            Post(new
            {
                type = "error",
                title = $"Opening {Path.GetExtension(path)} files requires LibreOffice",
                detail = "Install the free LibreOffice (libreoffice.org) and Visio will use it to convert the file to PDF.",
            });
            return;
        }

        Post(new { type = "status", title = "Converting document…", detail = name });
        try
        {
            var pdf = await Converter.ToPdfAsync(path, soffice);
            if (seq == _openSeq) ShowDocument(pdf, name, "pdf", reload);
        }
        catch (Exception ex)
        {
            if (seq == _openSeq) Post(new { type = "error", title = "Could not open the file", detail = ex.Message });
        }
    }

    /// <summary>Shows <paramref name="servedPath"/> under the original file's name; <paramref name="format"/> overrides the renderer.</summary>
    private void ShowDocument(string servedPath, string displayName, string? format, bool reload) =>
        Post(new { type = "open", name = displayName, url = _docs!.Register(servedPath), format, reload });

    // ---------------------------------------------------------------------
    // Reload when the open file changes on disk (re-saved by an editor,
    // replaced by a download). Editors write in bursts, so wait for quiet.
    // ---------------------------------------------------------------------
    private void Watch(string path)
    {
        if (_watcher != null && string.Equals(_watcher.Path, Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase)
            && string.Equals(_watcher.Filter, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += OnWatchedFileChanged;
            _watcher.Created += OnWatchedFileChanged;
            // Safe-save: the editor writes a temp file and renames it over the original.
            _watcher.Renamed += OnWatchedFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            // Some network shares and virtual drives can't be watched; reload just won't happen.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!string.Equals(e.FullPath, _currentPath, StringComparison.OrdinalIgnoreCase)) return;
        Dispatcher.BeginInvoke(() =>
        {
            _reloadTimer ??= CreateReloadTimer();
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
    }

    private DispatcherTimer CreateReloadTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_currentPath != null && File.Exists(_currentPath)) OpenFile(_currentPath, reload: true);
        };
        return timer;
    }

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
