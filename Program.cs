using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PrettyMark;

// --- App Settings ---
class AppSettings
{
    public bool DarkMode { get; set; }
    public bool DrawerOpen { get; set; } = true;
    public string Language { get; set; } = "";
    public List<string> RecentFiles { get; set; } = new();
    public List<string> SessionFiles { get; set; } = new();
    public string SessionActiveFile { get; set; } = "";

    // Window position/size, remembered across runs. Null = never saved (first run
    // or an old settings.json from before this feature) -- falls back to the default.
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // Last scroll position within each file, keyed by full file path, so reopening a document
    // -- across tab switches within a session AND across closing/reopening PrettyMark entirely
    // -- returns you to where you left off rather than the top. OrdinalIgnoreCase because
    // Windows paths are case-insensitive; re-applied after every Load() below since
    // JsonSerializer.Deserialize builds a fresh Dictionary with the default (case-sensitive)
    // comparer, not this one.
    public Dictionary<string, double> ScrollPositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrettyMark", "settings.json");

    private static readonly string[] ValidExtensions = { ".md", ".markdown", ".txt" };

    private static bool IsValidFilePath(string p) =>
        !string.IsNullOrEmpty(p) && Path.IsPathRooted(p) &&
        ValidExtensions.Contains(Path.GetExtension(p).ToLowerInvariant());

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                settings.RecentFiles = settings.RecentFiles?.Where(IsValidFilePath).Take(10).ToList() ?? new();
                settings.SessionFiles = settings.SessionFiles?.Where(IsValidFilePath).ToList() ?? new();
                if (!string.IsNullOrEmpty(settings.SessionActiveFile) && !IsValidFilePath(settings.SessionActiveFile))
                    settings.SessionActiveFile = "";
                settings.ScrollPositions = new Dictionary<string, double>(
                    settings.ScrollPositions ?? new(), StringComparer.OrdinalIgnoreCase);
                return settings;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}"); }
        return new AppSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }
}

// --- Color config ---
// User-editable theme color overrides. Separate from AppSettings/settings.json
// (internal program state) since this file is meant to be hand-edited -- it's
// only read at startup, so edit it while PrettyMark is closed.
class ColorTheme
{
    public string TextColor { get; set; }
    public string BackgroundColor { get; set; }
}

class ColorConfig
{
    public string _readme { get; set; } =
        "PrettyMark color overrides. Edit while the program is closed -- changes are only read at startup.";
    public ColorTheme Light { get; set; } = new() { TextColor = "#1f2328", BackgroundColor = "#ffffff" };
    public ColorTheme Dark { get; set; } = new() { TextColor = "#f0f6fc", BackgroundColor = "#0d1117" };

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrettyMark", "colors.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static ColorConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<ColorConfig>(File.ReadAllText(ConfigPath));

            // First run: write the defaults out so there's something to find and edit.
            var defaults = new ColorConfig();
            defaults.Save();
            return defaults;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Color config load failed: {ex.Message}"); }
        return new ColorConfig();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, WriteOptions));
    }
}

// --- Tab model ---
class TabInfo
{
    public string Id { get; set; }
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public FileSystemWatcher Watcher { get; set; }
}

// --- Entry point ---
static class Program
{
    private const string MutexName = "PrettyMark_SingleInstance_Mutex";
    private const string EventName = "PrettyMark_SingleInstance_Event";

    private static readonly string OpenRequestFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrettyMark", "open-request.txt");

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string filePath = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
        if (filePath != null && !File.Exists(filePath))
        {
            var errorStrings = MainForm.LoadTranslationsStatic(
                MainForm.ResolveLanguageStatic(AppSettings.Load().Language));
            var msg = string.Format(
                errorStrings.GetValueOrDefault("error_file_not_found", "File not found: {0}"),
                filePath);
            MessageBox.Show(msg, errorStrings.GetValueOrDefault("app_name", "PrettyMark"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // Another instance is running — send file path via temp file + event
            if (filePath != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(OpenRequestFile)!);
                    File.WriteAllText(OpenRequestFile, filePath);
                    using var evt = EventWaitHandle.OpenExisting(EventName);
                    evt.Set();
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Single-instance send failed: {ex.Message}"); }
            }
            return;
        }

        var form = new MainForm(filePath);
        form.StartOpenRequestListener(EventName, OpenRequestFile);
        Application.Run(form);
    }
}

class MainForm : Form
{
    private WebView2 webView;
    private System.Timers.Timer debounceTimer;
    private ToolStripMenuItem darkModeItem;
    private ToolStripMenuItem drawerItem;
    private AppSettings settings;
    private ColorConfig colorConfig;

    // Fullscreen state
    private bool _isFullscreen;
    private FormBorderStyle _savedBorderStyle;
    private FormWindowState _savedWindowState;

    // Tab state
    private readonly List<TabInfo> tabs = new();
    private string activeTabId;

    // i18n state
    private Dictionary<string, string> _strings = new();
    private string _currentLang = "en";

    // Menu item references for translation updates
    private ToolStripMenuItem fileMenu, openItem, closeTabItem, exitItem;
    private ToolStripMenuItem recentMenu, printItem;
    private ToolStripMenuItem editMenu, findItem;
    private ToolStripMenuItem viewMenu, zoomInItem, zoomOutItem, resetZoomItem, fullScreenItem;
    private ToolStripMenuItem langMenu, helpMenu, aboutItem;
    private ToolStripMenuItem optionsItem;

    // Session restore
    private string _initialFilePath;

    // Single-instance listener
    private CancellationTokenSource _listenerCts;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    private const int SW_RESTORE = 9;

    public void StartOpenRequestListener(string eventName, string requestFile)
    {
        var evt = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        _listenerCts = new CancellationTokenSource();
        var ct = _listenerCts.Token;
        Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!evt.WaitOne(500)) continue;
                    if (!File.Exists(requestFile)) continue;
                    var path = File.ReadAllText(requestFile).Trim();
                    try { File.Delete(requestFile); } catch { }
                    if (!string.IsNullOrEmpty(path))
                        BeginInvoke(() =>
                        {
                            OpenTab(path);
                            if (IsIconic(Handle)) ShowWindow(Handle, SW_RESTORE);
                            SetForegroundWindow(Handle);
                        });
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Open request listener error: {ex.Message}"); }
            }
            evt.Dispose();
        }, ct);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _listenerCts?.Cancel();
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWindowBounds();
        base.OnFormClosing(e);
    }

    // Remembers window position/size/maximized-state across runs. Uses RestoreBounds (the
    // normal, non-maximized rectangle) when maximized, since Bounds while maximized is the
    // full-screen rectangle -- not what we want to restore to next time.
    private void SaveWindowBounds()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.WindowX = bounds.X;
        settings.WindowY = bounds.Y;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        settings.Save();
    }

    // Applies the saved window rectangle if one exists and is still visible on some monitor
    // (guards against a saved position from a monitor that's since been unplugged/resized).
    // Falls back to the original default (960x800, centered) otherwise.
    private void ApplySavedWindowBounds()
    {
        if (settings.WindowX.HasValue && settings.WindowY.HasValue &&
            settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
        {
            var rect = new System.Drawing.Rectangle(
                settings.WindowX.Value, settings.WindowY.Value,
                Math.Max(settings.WindowWidth.Value, MinimumSize.Width),
                Math.Max(settings.WindowHeight.Value, MinimumSize.Height));

            if (IsRectVisibleOnAnyScreen(rect))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = rect;
                if (settings.WindowMaximized) WindowState = FormWindowState.Maximized;
                return;
            }
        }

        Size = new System.Drawing.Size(960, 800);
        StartPosition = FormStartPosition.CenterScreen;
    }

    // True if at least a corner-sized chunk of rect overlaps some screen's working area.
    private static bool IsRectVisibleOnAnyScreen(System.Drawing.Rectangle rect)
    {
        const int minVisible = 100;
        foreach (var screen in Screen.AllScreens)
        {
            var overlap = System.Drawing.Rectangle.Intersect(screen.WorkingArea, rect);
            if (overlap.Width >= minVisible && overlap.Height >= minVisible) return true;
        }
        return false;
    }

    public MainForm(string filePath)
    {
        settings = AppSettings.Load();
        colorConfig = ColorConfig.Load();
        _currentLang = ResolveLanguageStatic(settings.Language);
        _strings = LoadTranslationsStatic(_currentLang);

        Text = T("app_name");
        MinimumSize = new System.Drawing.Size(400, 300);
        ApplySavedWindowBounds();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "favicon.ico");
        if (File.Exists(iconPath))
            Icon = new System.Drawing.Icon(iconPath);

        SetupMenu();
        InitializeWebView();

        _initialFilePath = filePath;
    }

    private void SetupMenu()
    {
        var menuStrip = new MenuStrip();

        // File
        fileMenu = new ToolStripMenuItem();
        openItem = new ToolStripMenuItem("", null, (s, e) => OpenFile())
        { ShortcutKeys = Keys.Control | Keys.O };
        recentMenu = new ToolStripMenuItem();
        printItem = new ToolStripMenuItem("", null, (s, e) => PrintDocument())
        { ShortcutKeys = Keys.Control | Keys.P };
        closeTabItem = new ToolStripMenuItem("", null, (s, e) => { if (activeTabId != null) CloseTab(activeTabId); })
        { ShortcutKeys = Keys.Control | Keys.W };
        exitItem = new ToolStripMenuItem("", null, (s, e) => Close());
        fileMenu.DropDownItems.Add(openItem);
        fileMenu.DropDownItems.Add(recentMenu);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(printItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(closeTabItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(exitItem);

        // Edit
        editMenu = new ToolStripMenuItem();
        findItem = new ToolStripMenuItem("", null, (s, e) => ExecuteJs("toggleFind()"))
        { ShortcutKeys = Keys.Control | Keys.F };
        editMenu.DropDownItems.Add(findItem);

        // View
        viewMenu = new ToolStripMenuItem();
        darkModeItem = new ToolStripMenuItem("", null, (s, e) =>
        {
            SetDarkMode(!darkModeItem.Checked);
        }) { ShortcutKeys = Keys.Control | Keys.D };
        viewMenu.DropDownItems.Add(darkModeItem);

        drawerItem = new ToolStripMenuItem("", null, (s, e) =>
        {
            ToggleDrawer();
        }) { ShortcutKeys = Keys.Control | Keys.B };
        drawerItem.Checked = settings.DrawerOpen;
        viewMenu.DropDownItems.Add(drawerItem);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        // Language submenu
        langMenu = new ToolStripMenuItem();
        viewMenu.DropDownItems.Add(langMenu);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        zoomInItem = new ToolStripMenuItem("", null,
            (s, e) => ExecuteJs("zoomIn()")) { ShortcutKeys = Keys.Control | Keys.Oemplus };
        zoomOutItem = new ToolStripMenuItem("", null,
            (s, e) => ExecuteJs("zoomOut()")) { ShortcutKeys = Keys.Control | Keys.OemMinus };
        resetZoomItem = new ToolStripMenuItem("", null,
            (s, e) => ExecuteJs("zoomReset()")) { ShortcutKeys = Keys.Control | Keys.D0 };
        viewMenu.DropDownItems.Add(zoomInItem);
        viewMenu.DropDownItems.Add(zoomOutItem);
        viewMenu.DropDownItems.Add(resetZoomItem);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        fullScreenItem = new ToolStripMenuItem("", null,
            (s, e) => ToggleFullscreen()) { ShortcutKeys = Keys.F11 };
        viewMenu.DropDownItems.Add(fullScreenItem);

        // Options (single top-level item, no dropdown -- opens the dialog directly)
        optionsItem = new ToolStripMenuItem("", null, (s, e) => OpenOptionsDialog());

        // ?
        helpMenu = new ToolStripMenuItem();
        aboutItem = new ToolStripMenuItem("", null, (s, e) => ExecuteJs("showAbout()"));
        helpMenu.DropDownItems.Add(aboutItem);

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, viewMenu, optionsItem, helpMenu });
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);

        ApplyMenuTranslations();
    }

    private async void InitializeWebView()
    {
        webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = true };
        Controls.Add(webView);
        webView.BringToFront();

        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrettyMark", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
        await webView.EnsureCoreWebView2Async(env);

        // Serve assets from local folder via virtual host
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", assetsDir, CoreWebView2HostResourceAccessKind.Allow);

        // Serve images referenced by the CURRENTLY ACTIVE tab's document under a second virtual
        // host, "doc.local". We intercept requests live (WebResourceRequested) rather than using
        // SetVirtualHostNameToFolderMapping, because that API's folder can't be safely re-pointed
        // per tab: per Microsoft's own docs, once the page's resource loaders exist (which they do,
        // since PrettyMark's page loads once at startup and tabs are swapped via innerHTML, never a
        // real navigation), mapping changes may not take effect without a full page reload.
        webView.CoreWebView2.AddWebResourceRequestedFilter(
            "https://doc.local/*", CoreWebView2WebResourceContext.Image, CoreWebView2WebResourceRequestSourceKinds.Document);
        webView.CoreWebView2.WebResourceRequested += OnDocLocalResourceRequested;

        // Handle JS messages
        webView.CoreWebView2.WebMessageReceived += OnWebMessage;

        // Intercept navigation: only allow initial page load, block everything else
        webView.CoreWebView2.NavigationStarting += (s, e) =>
        {
            if (e.Uri == "https://app.local/index.html") return;

            e.Cancel = true;
            System.Diagnostics.Debug.WriteLine($"Navigation blocked: {e.Uri}");

            if (e.Uri.StartsWith("file:///"))
            {
                var path = new Uri(e.Uri).LocalPath;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (new[] { ".md", ".markdown", ".txt" }.Contains(ext))
                    BeginInvoke(() => OpenTab(path));
                else
                    // Not a type PrettyMark renders itself (e.g. .html) — hand off to the OS default app.
                    // Covers Ctrl+click / middle-click, which land here instead of the JS click handler.
                    BeginInvoke(() => System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }));
            }
        };

        // Intercept new window requests (triggered by file drag & drop)
        webView.CoreWebView2.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            if (e.Uri.StartsWith("file:///"))
            {
                var path = new Uri(e.Uri).LocalPath;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (new[] { ".md", ".markdown", ".txt" }.Contains(ext))
                    BeginInvoke(() => OpenTab(path));
                else
                    // Same fallback as NavigationStarting above, for links opened via window.open()/target="_blank".
                    BeginInvoke(() => System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }));
            }
        };

        // Load content once page is ready
        webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

        // Clean up WebView chrome
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

        webView.CoreWebView2.Navigate("https://app.local/index.html");
    }

    private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;

        // Send i18n strings to JS
        SendStringsToJs();

        // Send app version to JS (shown in the About dialog)
        ExecuteJs($"setAppVersion({JsonSerializer.Serialize(AppVersion.Current)})");

        // Send user color overrides to JS; applied whenever the theme is (re)set (see setDarkMode in index.html)
        ExecuteJs($"setColorOverrides({JsonSerializer.Serialize(colorConfig)})");

        // Apply dark mode from saved settings
        SetDarkMode(settings.DarkMode);

        // Apply drawer state
        ExecuteJs($"setDrawerOpen({(settings.DrawerOpen ? "true" : "false")})");

        await RestoreSession();
        if (_initialFilePath != null)
        {
            OpenTab(_initialFilePath);
            _initialFilePath = null;
        }
        if (tabs.Count == 0)
        {
            await webView.ExecuteScriptAsync("showWelcome()");
        }
    }

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!e.Source.StartsWith("https://app.local/")) return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.GetProperty("type").GetString();
            if (type == "open_url")
            {
                var url = doc.RootElement.GetProperty("url").GetString();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
            }
            else if (type == "open_local_file")
            {
                // A local link clicked in the rendered markdown, already resolved by the JS side
                // against the active tab's own folder. Markdown files open as a new tab like any
                // other; anything else (e.g. .html) goes to the OS default app.
                var url = doc.RootElement.GetProperty("url").GetString();
                if (Uri.TryCreate(url, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
                {
                    var path = fileUri.LocalPath;
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (new[] { ".md", ".markdown", ".txt" }.Contains(ext))
                        OpenTab(path);
                    else
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            else if (type == "switch_tab")
            {
                var id = doc.RootElement.GetProperty("id").GetString();
                SwitchTab(id);
            }
            else if (type == "close_tab")
            {
                var id = doc.RootElement.GetProperty("id").GetString();
                CloseTab(id);
            }
            else if (type == "scroll_position")
            {
                // Debounced ~600ms in JS (index.html) rather than on every scroll event.
                // tab lookup can legitimately miss (e.g. a message that was already in flight
                // when its tab got closed), so this is a no-op rather than an error in that case.
                var id = doc.RootElement.GetProperty("id").GetString();
                var scrollTop = doc.RootElement.GetProperty("scrollTop").GetDouble();
                var tab = tabs.FirstOrDefault(t => t.Id == id);
                if (tab != null)
                {
                    settings.ScrollPositions[tab.FilePath] = scrollTop;
                    settings.Save();
                }
            }
            else if (type == "shortcut")
            {
                var action = doc.RootElement.GetProperty("action").GetString();
                switch (action)
                {
                    case "open": OpenFile(); break;
                    case "close_tab": if (activeTabId != null) CloseTab(activeTabId); break;
                    case "print": PrintDocument(); break;
                    case "fullscreen": ToggleFullscreen(); break;
                    case "dark_mode": SetDarkMode(!darkModeItem.Checked); break;
                    case "sidebar": ToggleDrawer(); break;
                }
            }
            else if (type == "click")
            {
                foreach (ToolStripItem item in MainMenuStrip.Items)
                    if (item is ToolStripMenuItem mi && mi.DropDown.Visible)
                        mi.HideDropDown();
            }
            else if (type == "drawer_toggled")
            {
                var open = doc.RootElement.GetProperty("open").GetBoolean();
                settings.DrawerOpen = open;
                drawerItem.Checked = open;
                settings.Save();
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"PrettyMark: WebMessage parse failed: {ex.Message}"); }
    }

    // --- Tab operations ---

    private static readonly string[] AllowedExtensions = { ".md", ".markdown", ".txt" };

    // Shared by OpenTab and RestoreSession: builds the JSON payload for addTab(), including
    // any scroll position persisted from a previous session (0 if this file's never been
    // scrolled/tracked before). Keeping the lookup in one place avoids the two call sites
    // drifting out of sync with what addTab() in index.html actually expects.
    private string BuildTabJson(TabInfo tab)
    {
        var scrollTop = settings.ScrollPositions.GetValueOrDefault(tab.FilePath, 0);
        return JsonSerializer.Serialize(new
        {
            id = tab.Id,
            name = tab.FileName,
            path = Path.GetDirectoryName(tab.FilePath),
            scrollTop
        });
    }

    private async void OpenTab(string path)
    {
        path = Path.GetFullPath(path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return;
        if (!File.Exists(path)) return;

        // If file already open, just switch to it
        var existing = tabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SwitchTab(existing.Id);
            return;
        }

        var tab = new TabInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = path,
            FileName = Path.GetFileName(path)
        };

        // Create watcher
        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        tab.Watcher = new FileSystemWatcher(dir, file) { NotifyFilter = NotifyFilters.LastWrite };
        tab.Watcher.Changed += (s, e) => OnFileChanged(tab.Id);
        tab.Watcher.EnableRaisingEvents = true;

        tabs.Add(tab);
        activeTabId = tab.Id;

        settings.AddRecentFile(path);
        SaveSession();
        RebuildRecentMenu();

        // Send tab info to JS
        var tabJson = BuildTabJson(tab);
        await webView.ExecuteScriptAsync($"addTab({tabJson})");

        // Render content
        await RenderTab(tab);
        UpdateTitle(tab);
    }

    private async void SwitchTab(string tabId)
    {
        var tab = tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.Id == activeTabId) return;

        activeTabId = tabId;
        SaveSession();
        await webView.ExecuteScriptAsync($"activateTab({JsonSerializer.Serialize(tabId)})");
        await RenderTab(tab);
        UpdateTitle(tab);
    }

    private async void CloseTab(string tabId)
    {
        var tab = tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        // Dispose watcher
        tab.Watcher.EnableRaisingEvents = false;
        tab.Watcher.Dispose();
        tab.Watcher = null;

        tabs.Remove(tab);
        await webView.ExecuteScriptAsync($"removeTab({JsonSerializer.Serialize(tabId)})");

        if (activeTabId == tabId)
        {
            if (tabs.Count > 0)
            {
                // Activate the last tab
                var next = tabs.Last();
                activeTabId = next.Id;
                await webView.ExecuteScriptAsync($"activateTab({JsonSerializer.Serialize(next.Id)})");
                await RenderTab(next);
                UpdateTitle(next);
            }
            else
            {
                activeTabId = null;
                Text = T("app_name");
                await webView.ExecuteScriptAsync("showWelcome()");
            }
        }
        SaveSession();
    }

    private static readonly Dictionary<string, string> ImageContentTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".bmp"] = "image/bmp", [".webp"] = "image/webp", [".svg"] = "image/svg+xml"
    };

    private void OnDocLocalResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        // Resolves against whichever tab is active AT REQUEST TIME, so there's no stale mapping to
        // worry about — switching tabs just changes what the next request resolves against.
        try
        {
            var activeTab = tabs.FirstOrDefault(t => t.Id == activeTabId);
            if (activeTab == null)
            {
                e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }

            var uri = new Uri(e.Request.Uri);
            var relPath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(activeTab.FilePath), relPath));

            if (!File.Exists(fullPath))
            {
                e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = ImageContentTypes.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";

            var stream = new MemoryStream(File.ReadAllBytes(fullPath));
            e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                stream, 200, "OK", $"Content-Type: {contentType}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"PrettyMark: doc.local resource request failed: {ex.Message}");
            e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 500, "Error", "");
        }
    }

    // preserveScroll selects which JS-side function renders the content:
    //   false (default) -> render()         - jumps to top of document
    //   true             -> reloadContent()  - keeps the reader's current scroll position
    // Every caller except OnFileChanged (the file-watcher's debounced callback) is opening
    // or switching to a genuinely different document, where scrolling to top is correct;
    // only the file-watcher case represents the CURRENTLY DISPLAYED document changing
    // underneath the reader (e.g. trimming ads out of a saved chat export), where jumping
    // back to the top on every save is the behavior we're fixing.
    private async Task RenderTab(TabInfo tab, bool preserveScroll = false)
    {
        if (tab == null || !File.Exists(tab.FilePath)) return;
        var content = ReadFile(tab.FilePath);
        var json = JsonSerializer.Serialize(content);
        var jsFunction = preserveScroll ? "reloadContent" : "render";
        await webView.ExecuteScriptAsync($"{jsFunction}({json})");
    }

    private void UpdateTitle(TabInfo tab)
    {
        var name = T("app_name");
        Text = tab != null ? $"{tab.FileName} \u2014 {name}" : name;
    }

    private static string ReadFile(string path)
    {
        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch { return File.ReadAllText(path, Encoding.Latin1); }
    }

    private void OpenFile()
    {
        var mdLabel = T("dialog_filter_md");
        var allLabel = T("dialog_filter_all");
        using var dialog = new OpenFileDialog
        {
            Filter = $"{mdLabel} (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|{allLabel} (*.*)|*.*",
            Title = T("dialog_open_title")
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            OpenTab(dialog.FileName);
    }

    private void OnFileChanged(string tabId)
    {
        debounceTimer?.Stop();
        debounceTimer?.Dispose();
        debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        debounceTimer.Elapsed += (s, ev) =>
        {
            BeginInvoke(() =>
            {
                // Only re-render if this is the active tab
                if (tabId == activeTabId)
                {
                    var tab = tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab != null) _ = RenderTab(tab, preserveScroll: true);
                }
            });
        };
        debounceTimer.Start();
    }

    private void RebuildRecentMenu()
    {
        recentMenu.DropDownItems.Clear();
        var recent = settings.RecentFiles.Where(File.Exists).Take(10).ToList();
        if (recent.Count == 0)
        {
            var emptyItem = new ToolStripMenuItem(T("menu_recent_empty")) { Enabled = false };
            recentMenu.DropDownItems.Add(emptyItem);
        }
        else
        {
            foreach (var path in recent)
            {
                var item = new ToolStripMenuItem(Path.GetFileName(path)) { ToolTipText = path, Tag = path };
                item.Click += (s, e) => OpenTab((string)((ToolStripMenuItem)s).Tag);
                recentMenu.DropDownItems.Add(item);
            }
            recentMenu.DropDownItems.Add(new ToolStripSeparator());
            var clearItem = new ToolStripMenuItem(T("menu_recent_clear"));
            clearItem.Click += (s, e) =>
            {
                settings.RecentFiles.Clear();
                settings.Save();
                RebuildRecentMenu();
            };
            recentMenu.DropDownItems.Add(clearItem);
        }
    }

    private void PrintDocument()
    {
        if (activeTabId == null) return;
        ExecuteJs("window.print()");
    }

    private void SaveSession()
    {
        settings.SessionFiles = tabs.Select(t => t.FilePath).ToList();
        var activeTab = tabs.FirstOrDefault(t => t.Id == activeTabId);
        settings.SessionActiveFile = activeTab?.FilePath ?? "";
        settings.Save();
    }

    private async Task RestoreSession()
    {
        var files = settings.SessionFiles.Where(File.Exists).ToList();
        if (files.Count == 0) return;

        string activeFilePath = settings.SessionActiveFile;
        string lastTabId = null;

        foreach (var path in files)
        {
            var tab = new TabInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                FilePath = path,
                FileName = Path.GetFileName(path)
            };

            var dir = Path.GetDirectoryName(path);
            var file = Path.GetFileName(path);
            tab.Watcher = new FileSystemWatcher(dir, file) { NotifyFilter = NotifyFilters.LastWrite };
            tab.Watcher.Changed += (s, e) => OnFileChanged(tab.Id);
            tab.Watcher.EnableRaisingEvents = true;

            tabs.Add(tab);

            var tabJson = BuildTabJson(tab);
            // activate:false -- see addTab()'s comment in index.html for why. The tab actually
            // meant to end up active gets a real activateTab() call below, once, after every
            // tab in the session has been added.
            await webView.ExecuteScriptAsync($"addTab({tabJson}, false)");

            if (string.Equals(path, activeFilePath, StringComparison.OrdinalIgnoreCase))
                lastTabId = tab.Id;
            else if (lastTabId == null)
                lastTabId = tab.Id;
        }

        if (lastTabId != null)
        {
            activeTabId = lastTabId;
            await webView.ExecuteScriptAsync($"activateTab({JsonSerializer.Serialize(lastTabId)})");
            var activeTab = tabs.FirstOrDefault(t => t.Id == lastTabId);
            if (activeTab != null)
            {
                await RenderTab(activeTab);
                UpdateTitle(activeTab);
            }
        }
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            FormBorderStyle = _savedBorderStyle;
            WindowState = _savedWindowState;
            MainMenuStrip.Visible = true;
        }
        else
        {
            _savedBorderStyle = FormBorderStyle;
            _savedWindowState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            MainMenuStrip.Visible = false;
        }
        _isFullscreen = !_isFullscreen;
    }

    private void ToggleDrawer()
    {
        settings.DrawerOpen = !settings.DrawerOpen;
        drawerItem.Checked = settings.DrawerOpen;
        settings.Save();
        ExecuteJs($"setDrawerOpen({(settings.DrawerOpen ? "true" : "false")})");
    }

    private void SetDarkMode(bool on)
    {
        darkModeItem.Checked = on;
        settings.DarkMode = on;
        settings.Save();
        ExecuteJs($"setDarkMode({(on ? "true" : "false")})");
    }

    // Opens the Options dialog modally. On OK, the dialog's working copy of the colors becomes
    // the real colorConfig, gets persisted to colors.json, and is pushed into JS immediately --
    // the same setColorOverrides() call used at startup -- so the change is visible without
    // restarting.
    private void OpenOptionsDialog()
    {
        using var dlg = new OptionsDialog(colorConfig, _strings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            colorConfig = dlg.Result;
            colorConfig.Save();
            ExecuteJs($"setColorOverrides({JsonSerializer.Serialize(colorConfig)})");
        }
    }

    private async void ExecuteJs(string script)
    {
        if (webView?.CoreWebView2 != null)
            await webView.ExecuteScriptAsync(script);
    }

    // --- i18n ---

    private string T(string key)
    {
        return _strings.GetValueOrDefault(key, key);
    }

    public static Dictionary<string, string> LoadTranslationsStatic(string lang)
    {
        var langDir = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
        // Validate lang: alphanumeric/hyphen only, no path traversal
        if (string.IsNullOrEmpty(lang) || lang.Any(c => c == '.' || c == '/' || c == '\\'))
            lang = "en";
        var path = Path.Combine(langDir, $"{lang}.json");
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(langDir) + Path.DirectorySeparatorChar))
            path = Path.Combine(langDir, "en.json");
        if (!File.Exists(path))
            path = Path.Combine(langDir, "en.json");
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Translation load failed: {ex.Message}"); }
        return new Dictionary<string, string>();
    }

    public static string ResolveLanguageStatic(string settingsLang)
    {
        if (!string.IsNullOrEmpty(settingsLang))
            return settingsLang;

        var uiLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var langDir = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
        var path = Path.Combine(langDir, $"{uiLang}.json");
        return File.Exists(path) ? uiLang : "en";
    }

    private List<string> GetAvailableLanguages()
    {
        var langDir = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
        if (!Directory.Exists(langDir)) return new List<string> { "en" };
        return Directory.GetFiles(langDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(l => l)
            .ToList();
    }

    private void ApplyMenuTranslations()
    {
        fileMenu.Text = T("menu_file");
        openItem.Text = T("menu_open");
        recentMenu.Text = T("menu_recent");
        RebuildRecentMenu();
        printItem.Text = T("menu_print");
        closeTabItem.Text = T("menu_close_tab");
        exitItem.Text = T("menu_exit");
        editMenu.Text = T("menu_edit");
        findItem.Text = T("menu_find");
        viewMenu.Text = T("menu_view");
        darkModeItem.Text = T("menu_dark_mode");
        drawerItem.Text = T("menu_sidebar");
        zoomInItem.Text = T("menu_zoom_in");
        zoomOutItem.Text = T("menu_zoom_out");
        resetZoomItem.Text = T("menu_reset_zoom");
        fullScreenItem.Text = T("menu_fullscreen");
        // Falls back to "Options" directly (rather than the strict T(), which would show the
        // literal key) since this menu item predates any lang/*.json entry for it.
        optionsItem.Text = _strings.GetValueOrDefault("menu_options", "Options");
        helpMenu.Text = T("menu_help");
        aboutItem.Text = T("menu_about");

        // Rebuild Language submenu
        langMenu.Text = T("menu_language");
        langMenu.DropDownItems.Clear();
        foreach (var lang in GetAvailableLanguages())
        {
            var item = new ToolStripMenuItem(lang.ToUpperInvariant())
            {
                Checked = lang == _currentLang,
                Tag = lang
            };
            item.Click += (s, e) =>
            {
                var l = (string)((ToolStripMenuItem)s).Tag;
                if (l != _currentLang) SwitchLanguage(l);
            };
            langMenu.DropDownItems.Add(item);
        }
    }

    private void SwitchLanguage(string lang)
    {
        _currentLang = lang;
        settings.Language = lang;
        settings.Save();
        _strings = LoadTranslationsStatic(lang);
        ApplyMenuTranslations();
        SendStringsToJs();

        // Update title
        var activeTab = tabs.FirstOrDefault(t => t.Id == activeTabId);
        UpdateTitle(activeTab);
    }

    private void SendStringsToJs()
    {
        var json = JsonSerializer.Serialize(_strings);
        ExecuteJs($"setStrings({json})");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var tab in tabs)
            {
                if (tab.Watcher != null)
                {
                    tab.Watcher.EnableRaisingEvents = false;
                    tab.Watcher.Dispose();
                }
            }
            tabs.Clear();
            debounceTimer?.Dispose();
            webView?.Dispose();
        }
        base.Dispose(disposing);
    }
}

// --- Options dialog ---
// First pass: edits the four colors currently in colors.json (fg/bg for each theme). Uses
// FormStartPosition.CenterParent, so it always opens relative to MainForm -- which already
// guarantees it's on-screen (see ApplySavedWindowBounds/IsRectVisibleOnAnyScreen above) -- rather
// than needing its own off-screen-recovery logic the way a taskbar-launched, parent-less window
// (e.g. a system-tray dialog) would.
class OptionsDialog : Form
{
    private readonly Dictionary<string, string> strings;
    private Panel lightTextSwatch, lightBgSwatch, darkTextSwatch, darkBgSwatch;

    // The edited colors, populated only if the user clicks OK (see OnOk below). Null otherwise.
    public ColorConfig Result { get; private set; }

    public OptionsDialog(ColorConfig current, Dictionary<string, string> strings)
    {
        this.strings = strings;

        Text = T("options_title", "Options");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);

        // layout/buttonPanel are deliberately NOT docked. AutoSize on a docked control reports
        // "whatever space the parent leaves," not its actual content size -- which is what
        // shrank this dialog down to almost nothing. Left undocked, AutoSize instead resizes
        // each panel to its real preferred size, and we then size and position everything
        // ourselves below, so the Form ends up exactly as big as its content actually needs.
        var layout = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new System.Drawing.Point(Padding.Left, Padding.Top)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        lightTextSwatch = AddColorRow(layout, T("options_light_text", "Light Theme Text Color"), current.Light.TextColor);
        lightBgSwatch = AddColorRow(layout, T("options_light_bg", "Light Theme Background Color"), current.Light.BackgroundColor);
        darkTextSwatch = AddColorRow(layout, T("options_dark_text", "Dark Theme Text Color"), current.Dark.TextColor);
        darkBgSwatch = AddColorRow(layout, T("options_dark_bg", "Dark Theme Background Color"), current.Dark.BackgroundColor);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        var cancelBtn = new Button { Text = T("options_cancel", "Cancel"), DialogResult = DialogResult.Cancel };
        var okBtn = new Button { Text = T("options_ok", "OK") };
        okBtn.Click += (s, e) => OnOk();
        buttonPanel.Controls.Add(cancelBtn);
        buttonPanel.Controls.Add(okBtn);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        // Now that both panels have auto-sized to their real content, place buttonPanel below
        // layout and right-aligned under it, then shrink-wrap the Form's ClientSize around the
        // two -- this is what actually replaces the broken Form.AutoSize from before.
        const int gapBetweenRows = 12;
        buttonPanel.Location = new System.Drawing.Point(
            layout.Right - buttonPanel.Width, layout.Bottom + gapBetweenRows);
        ClientSize = new System.Drawing.Size(
            Padding.Left + layout.Width + Padding.Right,
            Padding.Top + layout.Height + gapBetweenRows + buttonPanel.Height + Padding.Bottom);
    }

    // Like MainForm's T(), but with an explicit fallback rather than the key itself, so the
    // dialog reads fine in English even before these keys exist in lang/*.json.
    private string T(string key, string fallback) => strings.GetValueOrDefault(key, fallback);

    // Adds one "label | color swatch | ... button" row and wires the button to open the standard
    // Windows color picker against that swatch. Returns the swatch panel so OnOk can read its
    // final BackColor back out when the dialog is accepted.
    private Panel AddColorRow(TableLayoutPanel layout, string label, string initialHex)
    {
        int row = layout.RowCount;
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6)
        }, 0, row);

        var swatch = new Panel
        {
            Size = new System.Drawing.Size(32, 20),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = System.Drawing.ColorTranslator.FromHtml(initialHex),
            Margin = new Padding(0, 4, 4, 4)
        };
        layout.Controls.Add(swatch, 1, row);

        var browseBtn = new Button { Text = "...", Width = 32, Margin = new Padding(0, 2, 0, 2) };
        browseBtn.Click += (s, e) => PickColor(swatch);
        layout.Controls.Add(browseBtn, 2, row);

        return swatch;
    }

    // Opens the standard Windows color-selection dialog pre-set to the swatch's current color,
    // and updates the swatch's BackColor if the user confirms a new one.
    private void PickColor(Panel swatch)
    {
        using var dlg = new ColorDialog { Color = swatch.BackColor, FullOpen = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            swatch.BackColor = dlg.Color;
        }
    }

    // Builds Result from the four swatches' current colors and closes the dialog with OK.
    private void OnOk()
    {
        Result = new ColorConfig
        {
            Light = new ColorTheme
            {
                TextColor = System.Drawing.ColorTranslator.ToHtml(lightTextSwatch.BackColor),
                BackgroundColor = System.Drawing.ColorTranslator.ToHtml(lightBgSwatch.BackColor)
            },
            Dark = new ColorTheme
            {
                TextColor = System.Drawing.ColorTranslator.ToHtml(darkTextSwatch.BackColor),
                BackgroundColor = System.Drawing.ColorTranslator.ToHtml(darkBgSwatch.BackColor)
            }
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}

