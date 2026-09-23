using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PrettyReMark;

// --- App Settings ---
class AppSettings
{
    public bool DarkMode { get; set; }
    public bool DrawerOpen { get; set; } = true;

    // On by default -- the program is advertised as tabbed, so new users should see the tabs.
    // See the Options dialog's "Show Tab Bar" checkbox.
    public bool ShowTabBar { get; set; } = true;

    // Undocumented: there's no UI for this -- it's meant to be hand-set to true in settings.json
    // while PrettyReMark is closed. When true, the Options dialog gains a "Reload Colors.json"
    // button next to "Show Tab Bar" that re-reads colors.json from disk into the dialog's swatch
    // grid, useful for previewing hand-edited or externally generated color files (e.g. from a
    // theme-import script) without restarting the app. Defaults to false/hidden since it's a
    // power-user testing aid, not something most users need to see.
    public bool EnableColorReloadButton { get; set; } = false;
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

    // Sidebar ("Open Files" drawer) width, remembered across runs the same way as the window
    // size above. Null = never saved (first run, or a pre-resize settings.json) -- falls back
    // to index.html's own CSS default (240px) rather than a value duplicated here.
    public int? DrawerWidth { get; set; }

    // Last scroll position within each file, keyed by full file path, so reopening a document
    // -- across tab switches within a session AND across closing/reopening PrettyReMark entirely
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
        "PrettyReMark", "settings.json");

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

                // Discard any scroll position for a file that isn't currently open in a tab --
                // once a file's closed, there's no reason to keep remembering where you were in
                // it, so this only checks SessionFiles, not RecentFiles.
                var openFiles = new HashSet<string>(
                    settings.SessionFiles ?? new(), StringComparer.OrdinalIgnoreCase);
                settings.ScrollPositions = new Dictionary<string, double>(
                    (settings.ScrollPositions ?? new()).Where(kv => openFiles.Contains(kv.Key)),
                    StringComparer.OrdinalIgnoreCase);

                return settings;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}"); }
        return new AppSettings();
    }

    // WriteIndented matches ColorConfig.Save() below -- both are meant to be human-readable if
    // you ever open them by hand, and there's no reason for the two files to look different.
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, WriteOptions));
    }
}

// --- Color config ---
// User-editable theme color overrides. Separate from AppSettings/settings.json
// (internal program state) since this file is meant to be hand-edited -- it's
// only read at startup, so edit it while PrettyReMark is closed.
class ColorTheme
{
    public string TextColor { get; set; }
    public string BackgroundColor { get; set; }

    // Sidebar (the "Open Files" drawer) and page-background colors. Defaults below match the
    // hardcoded values in assets/index.html's <style> block -- these overrides exist so the user
    // can change them without editing HTML/CSS by hand.
    //
    // Background/Filename/Path also double as the tab bar's background/selected-text/
    // unselected-text colors (see _applyColorOverrides() in index.html) -- no separate tab-bar
    // fields, by design, so the two stay visually in sync.
    public string SidebarBackgroundColor { get; set; }
    public string SidebarFilenameColor { get; set; }
    public string SidebarPathColor { get; set; }
    public string SidebarActiveBackgroundColor { get; set; }
    public string SidebarActiveBarColor { get; set; }
    public string SidebarHoverBackgroundColor { get; set; }
}

class ColorConfig
{
    public string _readme { get; set; } =
        "PrettyReMark color overrides. Edit while the program is closed -- changes are only read at startup.";
    public ColorTheme Light { get; set; } = new()
    {
        TextColor = "#1f2328", BackgroundColor = "#ffffff",
        SidebarBackgroundColor = "#f6f8fa", SidebarFilenameColor = "#1f2328",
        SidebarPathColor = "#656d76", SidebarActiveBackgroundColor = "#ddf4ff",
        SidebarActiveBarColor = "#0969da", SidebarHoverBackgroundColor = "#e8ebef"
    };
    public ColorTheme Dark { get; set; } = new()
    {
        TextColor = "#f0f6fc", BackgroundColor = "#0d1117",
        SidebarBackgroundColor = "#161b22", SidebarFilenameColor = "#f0f6fc",
        SidebarPathColor = "#8b949e", SidebarActiveBackgroundColor = "#1a2332",
        SidebarActiveBarColor = "#4493f8", SidebarHoverBackgroundColor = "#1c2129"
    };

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrettyReMark", "colors.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static ColorConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var loaded = JsonSerializer.Deserialize<ColorConfig>(File.ReadAllText(ConfigPath));
                var defaults = new ColorConfig();

                // A colors.json written before a given color existed (e.g. the 6 sidebar colors
                // added after this file already existed on disk) deserializes that color as null
                // on THIS specific ColorTheme instance -- object properties aren't backfilled from
                // ColorTheme's own field initializers during deserialization, only from what's
                // actually present in the JSON. An unhandled null here doesn't throw: it flows
                // through as Color.Empty in the Options dialog, which WinForms silently renders as
                // the ambient system control color instead -- and if OK is then clicked, THAT gets
                // saved back as a real (wrong) hex value. Backfilling every null against today's
                // defaults, then re-saving, closes that hole for good -- including for any future
                // color added the same way.
                loaded.Light = FillMissingDefaults(loaded.Light, defaults.Light);
                loaded.Dark = FillMissingDefaults(loaded.Dark, defaults.Dark);
                loaded.Save();
                return loaded;
            }

            // First run: write the defaults out so there's something to find and edit.
            var freshDefaults = new ColorConfig();
            freshDefaults.Save();
            return freshDefaults;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Color config load failed: {ex.Message}"); }
        return new ColorConfig();
    }

    // Returns a ColorTheme with every null property in `loaded` replaced by the matching
    // property from `defaults`. See the comment in Load() for why this is needed.
    private static ColorTheme FillMissingDefaults(ColorTheme loaded, ColorTheme defaults) => new()
    {
        TextColor = loaded.TextColor ?? defaults.TextColor,
        BackgroundColor = loaded.BackgroundColor ?? defaults.BackgroundColor,
        SidebarBackgroundColor = loaded.SidebarBackgroundColor ?? defaults.SidebarBackgroundColor,
        SidebarFilenameColor = loaded.SidebarFilenameColor ?? defaults.SidebarFilenameColor,
        SidebarPathColor = loaded.SidebarPathColor ?? defaults.SidebarPathColor,
        SidebarActiveBackgroundColor = loaded.SidebarActiveBackgroundColor ?? defaults.SidebarActiveBackgroundColor,
        SidebarActiveBarColor = loaded.SidebarActiveBarColor ?? defaults.SidebarActiveBarColor,
        SidebarHoverBackgroundColor = loaded.SidebarHoverBackgroundColor ?? defaults.SidebarHoverBackgroundColor
    };

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
    private const string MutexName = "PrettyReMark_SingleInstance_Mutex";
    private const string EventName = "PrettyReMark_SingleInstance_Event";

    private static readonly string OpenRequestFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrettyReMark", "open-request.txt");

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
            MessageBox.Show(msg, errorStrings.GetValueOrDefault("app_name", "PrettyReMark"),
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

partial class MainForm : Form
{
    // Version string baked into the assembly by "-p:Version=$(VERSION)" on the "dotnet publish"
    // line in the Makefile ("single" target) -- CHANGELOG.md is the only place the version
    // number is ever written by hand; this just reads it back out of the build, replacing the
    // old hand-generated AppVersion.cs. ToString(2) keeps the "major.minor" format the rest of
    // the app expects (e.g. "1.14"), matching the "[0-9]+\.[0-9]+" the Makefile scrapes out of
    // CHANGELOG.md; the "?? "0.0"" fallback only fires if a build somehow skips -p:Version
    // entirely (e.g. running "dotnet build" by hand outside the Makefile).
    private static readonly string CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.0";

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

    // Converted from [DllImport]/extern to [LibraryImport]/partial (SYSLIB1054): marshalling code
    // is now generated at compile time instead of built by the runtime on first call. LibraryImport
    // won't guess how to marshal `bool`, unlike DllImport (which defaults to the 4-byte Win32 BOOL),
    // so each bool parameter/return is marked explicitly with UnmanagedType.Bool to preserve that
    // same Win32 BOOL behavior.
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
    private const int SW_RESTORE = 9;

    // Plain SetForegroundWindow is routinely ignored by Windows' focus-stealing prevention when
    // called from a process that isn't itself currently in the foreground -- which is exactly our
    // situation here, we're a backgrounded instance reacting to a signal. Temporarily attaching
    // our thread's input queue to the current foreground window's input queue satisfies the OS's
    // internal "is this thread allowed to set the foreground window" check unconditionally, so
    // this works regardless of timing or which process launched what. Must run on the UI thread
    // (GetCurrentThreadId has to be *this* window's thread), so only call it from inside
    // BeginInvoke as StartOpenRequestListener below does.
    private void ForceForeground()
    {
        if (IsIconic(Handle)) ShowWindow(Handle, SW_RESTORE);

        var foregroundWindow = GetForegroundWindow();
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        uint currentThreadId = GetCurrentThreadId();

        bool attached = foregroundThreadId != currentThreadId
            && AttachThreadInput(currentThreadId, foregroundThreadId, true);
        try
        {
            SetForegroundWindow(Handle);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }

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
                    if (!string.IsNullOrEmpty(path)) {
                        BeginInvoke(() => {
                            OpenTab(path);
                            ForceForeground();
                        });
                    }
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

        UpdateTitle(null);
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
        // darkModeItem/drawerItem are no longer added to this menu -- both moved to the Options
        // dialog (checkboxes; see OptionsDialog below) -- but the objects themselves are kept
        // around (never added to any ToolStrip, so never visible) since OnWebMessage's "shortcut"
        // and "drawer_toggled" cases, and ApplyMenuTranslations, still read/write their .Checked
        // and .Text as the bookkeeping for "is dark mode / the sidebar currently on".
        viewMenu = new ToolStripMenuItem();
        darkModeItem = new ToolStripMenuItem("", null, (s, e) =>
        {
            SetDarkMode(!darkModeItem.Checked);
        }) { ShortcutKeys = Keys.Control | Keys.D };

        drawerItem = new ToolStripMenuItem("", null, (s, e) =>
        {
            ToggleDrawer();
        }) { ShortcutKeys = Keys.Control | Keys.B };
        drawerItem.Checked = settings.DrawerOpen;

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

        // Help
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
            "PrettyReMark", "WebView2");
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
        // since PrettyReMark's page loads once at startup and tabs are swapped via innerHTML, never a
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

            if (e.Uri.StartsWith("file:///")) {
                var path = new Uri(e.Uri).LocalPath;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (new[] { ".md", ".markdown", ".txt" }.Contains(ext)) {
                    BeginInvoke(() => OpenTab(path));
                }
                else {
                    // Not a type PrettyReMark renders itself (e.g. .html) — hand off to the OS default app.
                    // Covers Ctrl+click / middle-click, which land here instead of the JS click handler.
                    BeginInvoke(() => System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }));
                }
            }
            else {
                // Any non-file scheme (mailto:, tel:, http(s):, and anything else that shows up
                // later) — rather than special-case each one, hand the raw URI to ShellExecute and
                // let Windows' own protocol-handler resolution do what it already does for
                // double-clicked links elsewhere. Covers Ctrl+click / middle-click on these links,
                // which land here instead of the JS click handler (which skips mailto:/tel: itself
                // and lets native navigation reach this handler).
                BeginInvoke(() => System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }));
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
                if (new[] { ".md", ".markdown", ".txt" }.Contains(ext)) {
                    BeginInvoke(() => OpenTab(path));
                }
                else {
                    // Same fallback as NavigationStarting above, for links opened via window.open()/target="_blank".
                    BeginInvoke(() => System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }));
                }
            }
            else {
                // Same non-file fallback as NavigationStarting above.
                BeginInvoke(() => System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }));
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
        ExecuteJs($"setAppVersion({JsonSerializer.Serialize(CurrentVersion)})");

        // Send user color overrides to JS; applied whenever the theme is (re)set (see setDarkMode in index.html)
        ExecuteJs($"setColorOverrides({JsonSerializer.Serialize(colorConfig)})");

        // Apply dark mode from saved settings
        SetDarkMode(settings.DarkMode);

        // Apply drawer state
        ExecuteJs($"setDrawerOpen({(settings.DrawerOpen ? "true" : "false")})");
        if (settings.DrawerWidth.HasValue)
            ExecuteJs($"setDrawerWidth({settings.DrawerWidth.Value})");

        // Apply tab bar visibility (default on -- see AppSettings.ShowTabBar)
        ExecuteJs($"setTabBarVisible({(settings.ShowTabBar ? "true" : "false")})");

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
                    if (new[] { ".md", ".markdown", ".txt" }.Contains(ext)) {
                        OpenTab(path);
                    }
                    else {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                    }
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
                foreach (ToolStripItem item in MainMenuStrip.Items) {
                    if (item is ToolStripMenuItem mi && mi.DropDown.Visible) {
                        mi.HideDropDown();
                    }
                }
            }
            else if (type == "drawer_toggled")
            {
                var open = doc.RootElement.GetProperty("open").GetBoolean();
                settings.DrawerOpen = open;
                drawerItem.Checked = open;
                settings.Save();
            }
            else if (type == "drawer_width")
            {
                // Sent once on mouseup at the end of a drag (see index.html), not per mousemove.
                var width = doc.RootElement.GetProperty("width").GetInt32();
                settings.DrawerWidth = width;
                settings.Save();
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"PrettyReMark: WebMessage parse failed: {ex.Message}"); }
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
#pragma warning disable RCS1246 // .Last() kept intentionally -- see comment below
                var next = tabs.Last();	//  equivalent to:
                // var next = tabs[tabs.Count - 1];
#pragma warning restore RCS1246                
                activeTabId = next.Id;
                await webView.ExecuteScriptAsync($"activateTab({JsonSerializer.Serialize(next.Id)})");
                await RenderTab(next);
                UpdateTitle(next);
            }
            else
            {
                activeTabId = null;
                UpdateTitle(null);
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
            System.Diagnostics.Trace.WriteLine($"PrettyReMark: doc.local resource request failed: {ex.Message}");
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
        var name = $"{T("app_name")} v{CurrentVersion}";
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
        // ConvertAll (not Select().ToList()) since `tabs` is a List<T>: avoids the lazy-iterator +
        // dynamic-resize overhead of Select/ToList by allocating the destination array up front.
        settings.SessionFiles = tabs.ConvertAll(t => t.FilePath);
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

    private void ToggleDrawer() => SetDrawerOpen(!settings.DrawerOpen);

    // Sets the sidebar to a specific open/closed state (used by both the Ctrl+B toggle above
    // and the Options dialog's "Show Sidebar" checkbox, which sets an explicit value rather
    // than toggling).
    private void SetDrawerOpen(bool open)
    {
        settings.DrawerOpen = open;
        drawerItem.Checked = open;
        settings.Save();
        ExecuteJs($"setDrawerOpen({(open ? "true" : "false")})");
    }

    // Sets tab-bar visibility (the Options dialog's "Show Tab Bar" checkbox, default on --
    // there's no keyboard shortcut for this one, unlike the sidebar, since it's not expected to
    // be toggled often).
    private void SetTabBarVisible(bool visible)
    {
        settings.ShowTabBar = visible;
        settings.Save();
        ExecuteJs($"setTabBarVisible({(visible ? "true" : "false")})");
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
        using var dlg = new OptionsDialog(colorConfig, settings.DarkMode, settings.DrawerOpen, settings.ShowTabBar,
            settings.EnableColorReloadButton, _strings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            colorConfig = dlg.ColorResult;
            colorConfig.Save();
            ExecuteJs($"setColorOverrides({JsonSerializer.Serialize(colorConfig)})");

            SetDarkMode(dlg.DarkModeResult);
            SetDrawerOpen(dlg.SidebarResult);
            SetTabBarVisible(dlg.ShowTabBarResult);
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
            .Order() // net8.0: shorthand for OrderBy(x => x)
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
        // Explicit fallback (like optionsItem above) rather than the strict T(), so this reads
        // "Help" even before lang/*.json's "menu_help" entries are updated away from "?".
        helpMenu.Text = _strings.GetValueOrDefault("menu_help", "Help");
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
// Color grid + non-color option checkboxes (Dark Mode, Show Sidebar -- moved here from the View
// menu). Uses FormStartPosition.CenterParent, so it always opens relative to MainForm -- which
// already guarantees it's on-screen (see ApplySavedWindowBounds/IsRectVisibleOnAnyScreen above)
// -- rather than needing its own off-screen-recovery logic the way a taskbar-launched,
// parent-less window (e.g. a system-tray dialog) would.
class OptionsDialog : Form
{
    private readonly Dictionary<string, string> strings;

    // Each color row's Light/Dark swatch pair, named after the ColorTheme property it edits.
    private (Panel light, Panel dark) textSwatches, bgSwatches, sidebarBgSwatches,
        sidebarFilenameSwatches, sidebarPathSwatches, sidebarActiveBgSwatches,
        sidebarActiveBarSwatches, sidebarHoverBgSwatches;

    private readonly CheckBox darkModeCheck;
    private readonly CheckBox sidebarCheck;
    private readonly CheckBox tabBarCheck;

    // The edited colors and non-color options, populated only if the user clicks OK (see OnOk
    // below) -- callers should only read these after checking ShowDialog() == DialogResult.OK.
    public ColorConfig ColorResult { get; private set; }
    public bool DarkModeResult { get; private set; }
    public bool SidebarResult { get; private set; }
    public bool ShowTabBarResult { get; private set; }

    public OptionsDialog(ColorConfig current, bool darkMode, bool sidebarVisible, bool showTabBar,
        bool enableColorReloadButton, Dictionary<string, string> strings)
    {
        this.strings = strings;

        Text = T("options_title", "Options");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);

        // layout is deliberately NOT docked. AutoSize on a docked control reports "whatever
        // space the parent leaves," not its actual content size -- which is what shrank this
        // dialog down to almost nothing at one point. Left undocked, AutoSize instead resizes it
        // to its real preferred size, and ClientSize is set from that below, so the Form ends up
        // exactly as big as its content actually needs -- everything (grid, dividers, checkboxes,
        // and the buttons) now lives in this one panel, so there's nothing else to position.
        //
        // 7 columns: row label | vertical divider | light swatch | light "..." button |
        // vertical divider | dark swatch | dark "..." button. The two narrow gutter columns
        // (1 and 4) get a thin full-height rule painted after all color rows exist (see
        // SetUpColumnDividers below) -- this is what visually separates the Title / Light / Dark
        // groups from the mockup, instead of them blending together.
        var layout = new TableLayoutPanel
        {
            ColumnCount = 7,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new System.Drawing.Point(Padding.Left, Padding.Top)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 11));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 11));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        AddHeaderRow(layout, T("options_col_light", "Light Theme"), T("options_col_dark", "Dark Theme"));

        textSwatches = AddColorRow(layout, T("options_text", "Text Color"),
            current.Light.TextColor, current.Dark.TextColor);
        bgSwatches = AddColorRow(layout, T("options_bg", "Background Color"),
            current.Light.BackgroundColor, current.Dark.BackgroundColor);
        // These three rows also drive the tab bar (background/selected-text/unselected-text,
        // respectively) -- see the comment on ColorTheme's fields above. No separate tab-bar
        // rows on purpose.
        sidebarBgSwatches = AddColorRow(layout, T("options_sidebar_bg", "Sidebar Background"),
            current.Light.SidebarBackgroundColor, current.Dark.SidebarBackgroundColor);
        sidebarFilenameSwatches = AddColorRow(layout, T("options_sidebar_filename", "Sidebar Filename"),
            current.Light.SidebarFilenameColor, current.Dark.SidebarFilenameColor);
        sidebarPathSwatches = AddColorRow(layout, T("options_sidebar_path", "Sidebar File Path"),
            current.Light.SidebarPathColor, current.Dark.SidebarPathColor);
        sidebarActiveBgSwatches = AddColorRow(layout, T("options_sidebar_active_bg", "Selected Item Background"),
            current.Light.SidebarActiveBackgroundColor, current.Dark.SidebarActiveBackgroundColor);
        sidebarActiveBarSwatches = AddColorRow(layout, T("options_sidebar_active_bar", "Selected Item Bar"),
            current.Light.SidebarActiveBarColor, current.Dark.SidebarActiveBarColor);
        sidebarHoverBgSwatches = AddColorRow(layout, T("options_sidebar_hover_bg", "Hovered Item Background"),
            current.Light.SidebarHoverBackgroundColor, current.Dark.SidebarHoverBackgroundColor);

        // Captured now, with the color grid's row count final but before the checkbox section
        // below exists -- so the vertical dividers painted by SetUpColumnDividers (below) span
        // exactly the header + color rows, and stop short of the (full-width, no columns to
        // separate) checkbox section underneath.
        int colorGridRows = layout.RowCount;
        SetUpColumnDividers(layout, colorGridRows);

        AddDivider(layout);

        // Footer: two rows, each pairing a checkbox (col 0, same spot as every color row's
        // label) with buttons filling the Light+Dark columns to its right -- symmetric with the
        // color grid above rather than a separate button strip below it. "Dark Mode" pairs with
        // a single wide Reset button spanning both column groups (cols 2-6); "Show Sidebar"
        // pairs with OK centered under Light (cols 2-3) and Cancel centered under Dark (cols
        // 5-6), so OK/Cancel land directly under the "Light Theme"/"Dark Theme" headers.
        int darkModeRow = layout.RowCount;
        layout.RowCount = darkModeRow + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        darkModeCheck = new CheckBox
        {
            Text = T("options_dark_mode", "Dark Mode"), AutoSize = true,
            Checked = darkMode, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 4)
        };
        layout.Controls.Add(darkModeCheck, 0, darkModeRow);

        var resetBtn = new Button
        {
            Text = T("options_reset", "Reset to Defaults"), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4)
        };
        resetBtn.Click += (s, e) => ResetColorsToDefaults();
        layout.Controls.Add(resetBtn, 2, darkModeRow);
        layout.SetColumnSpan(resetBtn, 5); // cols 2..6: light swatch/button, gutter, dark swatch/button

        // "Show Tab Bar" gets its own row, with nothing alongside it in the Light/Dark columns --
        // unlike Dark Mode/Show Sidebar above and below it, it has no paired button to share the
        // row with.
        int tabBarRow = layout.RowCount;
        layout.RowCount = tabBarRow + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        tabBarCheck = new CheckBox
        {
            Text = T("options_tab_bar", "Show Tab Bar"), AutoSize = true,
            Checked = showTabBar, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 4)
        };
        layout.Controls.Add(tabBarCheck, 0, tabBarRow);

        // Undocumented power-user aid (see AppSettings.EnableColorReloadButton) -- only added to
        // the layout at all when the setting is on, so the space next to "Show Tab Bar" stays
        // empty for everyone else, same as it was before this feature existed. Sized/positioned
        // like resetBtn above: fills cols 2-6, the same row as its paired checkbox.
        if (enableColorReloadButton)
        {
            var reloadBtn = new Button
            {
                Text = T("options_reload_colors", "Reload Colors.json"), Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4)
            };
            reloadBtn.Click += (s, e) => ReloadColorsFromDisk();
            layout.Controls.Add(reloadBtn, 2, tabBarRow);
            layout.SetColumnSpan(reloadBtn, 5); // cols 2..6, same span as resetBtn
        }

        int sidebarRow = layout.RowCount;
        layout.RowCount = sidebarRow + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        sidebarCheck = new CheckBox
        {
            Text = T("options_sidebar", "Show Sidebar"), AutoSize = true,
            Checked = sidebarVisible, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 4)
        };
        layout.Controls.Add(sidebarCheck, 0, sidebarRow);

        var okBtn = new Button { Text = T("options_ok", "OK"), Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        okBtn.Click += (s, e) => OnOk();
        layout.Controls.Add(okBtn, 2, sidebarRow);
        layout.SetColumnSpan(okBtn, 2); // cols 2-3, under "Light Theme"

        var cancelBtn = new Button
        {
            Text = T("options_cancel", "Cancel"), DialogResult = DialogResult.Cancel,
            Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4)
        };
        layout.Controls.Add(cancelBtn, 5, sidebarRow);
        layout.SetColumnSpan(cancelBtn, 2); // cols 5-6, under "Dark Theme"

        Controls.Add(layout);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        // Everything (color grid, dividers, checkboxes, and now the buttons too) lives in one
        // TableLayoutPanel, so the Form's ClientSize is just Padding + layout's own size -- no
        // separate button-row height to add underneath, and so no leftover blank space at the
        // bottom the way there was with the old below-the-grid buttonPanel/resetPanel.
        ClientSize = new System.Drawing.Size(
            Padding.Left + layout.Width + Padding.Right,
            Padding.Top + layout.Height + Padding.Bottom);
    }

    // Like MainForm's T(), but with an explicit fallback rather than the key itself, so the
    // dialog reads fine in English even before these keys exist in lang/*.json.
    private string T(string key, string fallback) => strings.GetValueOrDefault(key, fallback);

    // Adds the "Light Theme" / "Dark Theme" column headers above the color grid. Each header
    // spans its swatch+button column pair (colspan 2) so it's centered over both.
    private void AddHeaderRow(TableLayoutPanel layout, string lightLabel, string darkLabel)
    {
        int row = layout.RowCount;
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var boldFont = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold);
        var lightHeader = new Label
        {
            Text = lightLabel, AutoSize = true, Font = boldFont,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, 6)
        };
        var darkHeader = new Label
        {
            Text = darkLabel, AutoSize = true, Font = boldFont,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, 6)
        };

        layout.Controls.Add(lightHeader, 2, row);
        layout.SetColumnSpan(lightHeader, 2);
        layout.Controls.Add(darkHeader, 5, row);
        layout.SetColumnSpan(darkHeader, 2);
    }

    // Adds one "label | light swatch | ... | dark swatch | ..." row. Returns both swatch panels
    // so OnOk can read their final BackColor back out when the dialog is accepted.
    private (Panel light, Panel dark) AddColorRow(TableLayoutPanel layout, string label, string lightHex, string darkHex)
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

        var lightSwatch = AddSwatchAndButton(layout, 2, row, lightHex);
        var darkSwatch = AddSwatchAndButton(layout, 5, row, darkHex);
        return (lightSwatch, darkSwatch);
    }

    // Adds one swatch + "..." picker button pair at the given column, and wires the button to
    // open the standard Windows color picker against that swatch.
    private Panel AddSwatchAndButton(TableLayoutPanel layout, int col, int row, string initialHex)
    {
        var swatch = new Panel
        {
            Size = new System.Drawing.Size(32, 20),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = System.Drawing.ColorTranslator.FromHtml(initialHex),
            Margin = new Padding(0, 4, 4, 4)
        };
        layout.Controls.Add(swatch, col, row);

        var browseBtn = new Button { Text = "...", Width = 32, Margin = new Padding(0, 2, 0, 2) };
        browseBtn.Click += (s, e) => PickColor(swatch);
        layout.Controls.Add(browseBtn, col + 1, row);

        return swatch;
    }

    // A shared, deliberately explicit divider color -- rather than a SystemColors.* value --
    // since SystemColors.ControlDark rendered as near-illegible black under a custom Windows
    // visual style (WindowBlinds) in testing. A fixed mid-gray stays visible regardless of the
    // active OS/skin theme.
    private static readonly System.Drawing.Color DividerColor = System.Drawing.Color.FromArgb(160, 160, 160);

    // Paints the two vertical rules separating the label / Light / Dark column groups, in the
    // given narrow gutter columns (1 and 4), down through colorGridRows.
    //
    // This is a Paint handler rather than an added/spanned child control (which was the first
    // approach here) because TableLayoutPanel doesn't reliably paint a control placed via
    // RowSpan when the panel itself is AutoSize/GrowAndShrink -- the auto-sizing pass can finish
    // without leaving it visible geometry. Reading back the panel's actual post-layout pixel
    // widths/heights via GetColumnWidths()/GetRowHeights() and drawing directly sidesteps that.
    private void SetUpColumnDividers(TableLayoutPanel layout, int colorGridRows)
    {
        layout.Paint += (s, e) =>
        {
            var colWidths = layout.GetColumnWidths();
            var rowHeights = layout.GetRowHeights();

				int gutter1CenterX = colWidths[0] + (colWidths[1] / 2);
				gutter1CenterX -= 3;
				int gutter2CenterX = colWidths[0] + colWidths[1] + colWidths[2] + colWidths[3] + (colWidths[4] / 2);
				gutter2CenterX -= 3;
            int gridBottomY = rowHeights.Take(colorGridRows).Sum();

            using var pen = new System.Drawing.Pen(DividerColor, 2);
            e.Graphics.DrawLine(pen, gutter1CenterX, 0, gutter1CenterX, gridBottomY);
            e.Graphics.DrawLine(pen, gutter2CenterX, 0, gutter2CenterX, gridBottomY);
        };
    }

    // Thin horizontal rule separating the color grid from the checkbox options below it,
    // spanning all 7 columns.
    private void AddDivider(TableLayoutPanel layout)
    {
        int row = layout.RowCount;
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var line = new Panel
        {
            Height = 2,
            Dock = DockStyle.Fill,
            BackColor = DividerColor,
            Margin = new Padding(0, 10, 0, 10)
        };
        layout.Controls.Add(line, 0, row);
        layout.SetColumnSpan(line, 7);
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

    // Resets every swatch (both themes) to PrettyReMark's built-in default colors, live in the
    // dialog -- lets someone who's experimented their way into an unreadable combination get
    // back to a known-good state without leaving the dialog or hand-editing colors.json. Doesn't
    // touch the Dark Mode / Show Sidebar checkboxes -- those aren't colors, and the button is
    // scoped to "default colors" per its label. Nothing is written to disk until OK is clicked.
    private void ResetColorsToDefaults()
    {
        var defaults = new ColorConfig();
        SetSwatchPair(textSwatches, defaults.Light.TextColor, defaults.Dark.TextColor);
        SetSwatchPair(bgSwatches, defaults.Light.BackgroundColor, defaults.Dark.BackgroundColor);
        SetSwatchPair(sidebarBgSwatches, defaults.Light.SidebarBackgroundColor, defaults.Dark.SidebarBackgroundColor);
        SetSwatchPair(sidebarFilenameSwatches, defaults.Light.SidebarFilenameColor, defaults.Dark.SidebarFilenameColor);
        SetSwatchPair(sidebarPathSwatches, defaults.Light.SidebarPathColor, defaults.Dark.SidebarPathColor);
        SetSwatchPair(sidebarActiveBgSwatches, defaults.Light.SidebarActiveBackgroundColor, defaults.Dark.SidebarActiveBackgroundColor);
        SetSwatchPair(sidebarActiveBarSwatches, defaults.Light.SidebarActiveBarColor, defaults.Dark.SidebarActiveBarColor);
        SetSwatchPair(sidebarHoverBgSwatches, defaults.Light.SidebarHoverBackgroundColor, defaults.Dark.SidebarHoverBackgroundColor);
    }

    // Undocumented power-user aid -- see AppSettings.EnableColorReloadButton. Re-reads colors.json
    // from disk (e.g. after hand-editing it, or overwriting it with an externally generated file)
    // and pushes it into every swatch, live in the dialog -- same "nothing written until OK"
    // contract as ResetColorsToDefaults above, just sourced from disk instead of hardcoded
    // defaults. ColorConfig.Load() already handles a missing/partial file by falling back to
    // defaults and backfilling nulls, so no extra error handling is needed here.
    private void ReloadColorsFromDisk()
    {
        var loaded = ColorConfig.Load();
        SetSwatchPair(textSwatches, loaded.Light.TextColor, loaded.Dark.TextColor);
        SetSwatchPair(bgSwatches, loaded.Light.BackgroundColor, loaded.Dark.BackgroundColor);
        SetSwatchPair(sidebarBgSwatches, loaded.Light.SidebarBackgroundColor, loaded.Dark.SidebarBackgroundColor);
        SetSwatchPair(sidebarFilenameSwatches, loaded.Light.SidebarFilenameColor, loaded.Dark.SidebarFilenameColor);
        SetSwatchPair(sidebarPathSwatches, loaded.Light.SidebarPathColor, loaded.Dark.SidebarPathColor);
        SetSwatchPair(sidebarActiveBgSwatches, loaded.Light.SidebarActiveBackgroundColor, loaded.Dark.SidebarActiveBackgroundColor);
        SetSwatchPair(sidebarActiveBarSwatches, loaded.Light.SidebarActiveBarColor, loaded.Dark.SidebarActiveBarColor);
        SetSwatchPair(sidebarHoverBgSwatches, loaded.Light.SidebarHoverBackgroundColor, loaded.Dark.SidebarHoverBackgroundColor);
    }

    private static void SetSwatchPair((Panel light, Panel dark) swatches, string lightHex, string darkHex)
    {
        swatches.light.BackColor = System.Drawing.ColorTranslator.FromHtml(lightHex);
        swatches.dark.BackColor = System.Drawing.ColorTranslator.FromHtml(darkHex);
    }

    // Builds ColorResult/DarkModeResult/SidebarResult from the dialog's current controls and
    // closes with OK.
    private void OnOk()
    {
        string Hex(Panel p) => System.Drawing.ColorTranslator.ToHtml(p.BackColor);

        ColorResult = new ColorConfig
        {
            Light = new ColorTheme
            {
                TextColor = Hex(textSwatches.light),
                BackgroundColor = Hex(bgSwatches.light),
                SidebarBackgroundColor = Hex(sidebarBgSwatches.light),
                SidebarFilenameColor = Hex(sidebarFilenameSwatches.light),
                SidebarPathColor = Hex(sidebarPathSwatches.light),
                SidebarActiveBackgroundColor = Hex(sidebarActiveBgSwatches.light),
                SidebarActiveBarColor = Hex(sidebarActiveBarSwatches.light),
                SidebarHoverBackgroundColor = Hex(sidebarHoverBgSwatches.light)
            },
            Dark = new ColorTheme
            {
                TextColor = Hex(textSwatches.dark),
                BackgroundColor = Hex(bgSwatches.dark),
                SidebarBackgroundColor = Hex(sidebarBgSwatches.dark),
                SidebarFilenameColor = Hex(sidebarFilenameSwatches.dark),
                SidebarPathColor = Hex(sidebarPathSwatches.dark),
                SidebarActiveBackgroundColor = Hex(sidebarActiveBgSwatches.dark),
                SidebarActiveBarColor = Hex(sidebarActiveBarSwatches.dark),
                SidebarHoverBackgroundColor = Hex(sidebarHoverBgSwatches.dark)
            }
        };
        DarkModeResult = darkModeCheck.Checked;
        SidebarResult = sidebarCheck.Checked;
        ShowTabBarResult = tabBarCheck.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }
}
