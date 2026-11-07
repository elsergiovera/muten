using System.Diagnostics;
using System.Drawing;
using muten.Core;

namespace muten.Tray;

public class TrayApplicationContext : ApplicationContext, ISettingsHost
{
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ManualCheckMinimum = TimeSpan.FromSeconds(1.2);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly NotifyIcon _notifyIcon;
    private readonly AudioSessionManager _manager;
    private readonly AutoMuteService _autoMute;
    private readonly ForegroundWatcher _watcher;
    private readonly SynchronizationContext _uiContext;
    private readonly ContextMenuStrip _menu;
    private readonly TrayMenuView _menuView;
    private readonly GlobalHotkey _hotkey;
    private readonly Icon _activeIcon;
    private readonly Icon _pausedIcon;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly HttpClient _http;
    private MutenSettings _settings;
    private SettingsForm? _settingsForm;
    private bool _hotkeySuspended;
    private bool _exited;

    private UpdateState _updateState;
    private ReleaseInfo? _latest;

    public event Action? StatusChanged;

    public TrayApplicationContext(bool afterUpdate = false)
    {
        // Install the WinForms context now (no window handle needed) so audio callbacks can be posted to this thread
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _uiContext = SynchronizationContext.Current!;

        _manager = new AudioSessionManager();
        _settings = SettingsManager.Load();

        _autoMute = new AutoMuteService(_manager, _uiContext)
        {
            Enabled = _settings.AutoMuteEnabled,
            FadeMs = _settings.FadeMs,
            GraceMs = _settings.GraceMs,
        };
        foreach (var app in _settings.ManagedApps)
        {
            _autoMute.AddManagedApp(app);
        }

        // Undo whatever a crashed previous run left muted or lowered
        _autoMute.RestoreFromState();

        _autoMute.AppsMuted += OnAppsMuted;

        _watcher = new ForegroundWatcher();
        _watcher.ForegroundChanged += OnForegroundChanged;
        _watcher.Start();

        // Treat apps already playing in the background right away
        _autoMute.OnForegroundChanged(_watcher.CurrentProcessName, null);

        _activeIcon = AppIcons.CreateTrayIcon(Theme.Red);
        _pausedIcon = AppIcons.CreateTrayIcon(Theme.PausedDot);

        _menuView = new TrayMenuView();
        _menuView.AppClicked += row =>
        {
            ToggleManaged(row);
            RefreshMenu();
        };
        _menuView.AddClicked += () => ShowSettings(openPicker: true);
        _menuView.PauseClicked += () =>
        {
            TogglePause();
            RefreshMenu();
        };
        _menuView.SettingsClicked += () => ShowSettings(openPicker: false);
        _menuView.QuitClicked += Exit;
        _menuView.InstallClicked += () => _ = InstallUpdateAsync();

        _menu = CreateMenuStrip(_menuView);
        _menu.Opening += (_, _) => RefreshMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _autoMute.Enabled ? _activeIcon : _pausedIcon,
            Text = _autoMute.Enabled ? "muten" : "muten (paused)",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.MouseClick += OnTrayClick;

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += OnHotkeyPressed;
        RegisterSavedHotkey(reportFailure: true);

        // Updates: only a published single-file exe updates itself (a dev build has muten.dll next to it)
        _http = Updater.CreateClient();
        _updateState = IsPublishedBuild ? UpdateState.Checking : UpdateState.Disabled;
        _updateTimer = new System.Windows.Forms.Timer { Interval = (int)FirstUpdateCheck.TotalMilliseconds };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
            await CheckForUpdatesAsync();
        };
        if (IsPublishedBuild)
        {
            CleanupAfterUpdate();
            _updateTimer.Start();
        }

        StartupToast.Show(afterUpdate ? $"muten was updated to {Version}." : "Muten is running in the background.");
    }

    // Shared by the tray and the screenshot harness
    public static ContextMenuStrip CreateMenuStrip(TrayMenuView view)
    {
        var menu = new TrayMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            BackColor = Color.White,
            DropShadowEnabled = true,
        };
        var host = new ToolStripControlHost(view)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = view.Size,
        };
        view.SizeChanged += (_, _) => host.Size = view.Size;
        menu.Items.Add(host);

        // Create both windows now (hidden), so the first popup never shows before the view can paint
        menu.CreateControl();
        if (!view.IsHandleCreated) _ = view.Handle;
        return menu;
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        // Show context menu on any click (left, middle, etc.), not just right-click
        if (e.Button != MouseButtons.Right)
        {
            var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            mi?.Invoke(_notifyIcon, null);
        }
    }

    private void OnForegroundChanged(string processName, int pid, string? exePath)
    {
        _autoMute.OnForegroundChanged(processName, exePath);
    }

    private void OnAppsMuted(List<string> names)
    {
        if (!_settings.NotificationsEnabled || names.Count == 0) return;
        var text = string.Join(", ", names) + " mutened";
        _uiContext.Post(_ => StartupToast.Show(text), null);
    }

    // ---- tray menu

    private TrayMenuModel BuildMenuModel()
    {
        var rows = new List<TrayAppRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sessions = _manager.GetSessions()
            .Where(s => s.ProcessId != 0 && (s.IsActive || _autoMute.IsManaged(s.ProcessName)))
            .OrderByDescending(s => s.IsActive) // an app with several processes shows its playing one
            .ToList();

        foreach (var session in sessions)
        {
            if (string.IsNullOrEmpty(session.ProcessName) || !seen.Add(session.ProcessName)) continue;

            _autoMute.ManagedApps.TryGetValue(session.ProcessName, out var app);
            rows.Add(new TrayAppRow(
                session.ProcessName,
                !string.IsNullOrEmpty(app?.DisplayName) ? app.DisplayName : session.DisplayName,
                session.ExecutablePath ?? app?.ExePath,
                Managed: app is not null,
                BackgroundVolume: app?.BackgroundVolume ?? 0,
                VolumePercent: (int)Math.Round(session.Volume * 100),
                SessionMuted: session.IsMuted));
        }

        return new TrayMenuModel
        {
            // Managed apps first, otherwise in session order
            Apps = rows.OrderByDescending(r => r.Managed).ToList(),
            Paused = !_autoMute.Enabled,
            UpdateVersion = _updateState is UpdateState.Available or UpdateState.Installing ? _latest?.Version.ToString(3) : null,
            Installing = _updateState == UpdateState.Installing,
        };
    }

    private void RefreshMenu()
    {
        var before = _menu.Bounds;
        bool visible = _menu.Visible;

        _menuView.SetModel(BuildMenuModel());

        if (visible)
        {
            _menu.PerformLayout();
            // A menu opened upwards from the tray grows/shrinks at the top so it stays above the taskbar
            var screen = Screen.FromRectangle(before).Bounds;
            if (_menu.Height != before.Height && before.Top + before.Height / 2 > screen.Top + screen.Height / 2)
                _menu.Top = before.Bottom - _menu.Height;
        }
    }

    private void ToggleManaged(TrayAppRow row)
    {
        if (string.IsNullOrEmpty(row.ProcessName)) return;

        if (_autoMute.IsManaged(row.ProcessName))
        {
            _autoMute.RemoveManagedApp(row.ProcessName);
        }
        else
        {
            _autoMute.AddManagedApp(new ManagedApp
            {
                ProcessName = row.ProcessName,
                ExePath = row.ExePath,
                DisplayName = row.Name,
            });
            _autoMute.Reevaluate();
        }

        SaveSettings();
        StatusChanged?.Invoke();
    }

    private void TogglePause()
    {
        _autoMute.Enabled = !_autoMute.Enabled;

        if (!_autoMute.Enabled)
        {
            _autoMute.RestoreAll();
        }
        else
        {
            _autoMute.Reevaluate();
        }

        _notifyIcon.Icon = _autoMute.Enabled ? _activeIcon : _pausedIcon;
        _notifyIcon.Text = _autoMute.Enabled ? "muten" : "muten (paused)";
        SaveSettings();
    }

    private void OnHotkeyPressed()
    {
        TogglePause();
        if (_menu.Visible) RefreshMenu();
        StartupToast.Show(_autoMute.Enabled ? "muten resumed." : "muten paused. Everything plays.");
    }

    private void ShowSettings(bool openPicker)
    {
        _menu.Close();

        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(this);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
            if (openPicker)
            {
                var form = _settingsForm;
                form.Shown += (_, _) => form.BeginInvoke(form.OpenPicker);
            }
            _settingsForm.Show();
        }
        else
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            if (openPicker) _settingsForm.BeginInvoke(_settingsForm.OpenPicker);
        }

        _settingsForm.Activate();
    }

    private void SaveSettings()
    {
        // Keep the user's order: known apps where they were, new ones at the end
        var current = _autoMute.ManagedApps;
        var ordered = _settings.ManagedApps
            .Where(a => current.ContainsKey(a.ProcessName))
            .Select(a => current[a.ProcessName])
            .ToList();
        ordered.AddRange(current.Values.Where(a => !ordered.Any(o => o.ProcessName.Equals(a.ProcessName, StringComparison.OrdinalIgnoreCase))));

        // `with` keeps the fields set elsewhere
        _settings = _settings with
        {
            ManagedApps = ordered,
            AutoMuteEnabled = _autoMute.Enabled,
            FadeMs = _autoMute.FadeMs,
            GraceMs = _autoMute.GraceMs,
        };

        try
        {
            SettingsManager.Save(_settings);
        }
        catch
        {
            // A locked or read-only settings file must not take the tray down
        }
    }

    // ---- hotkey

    private void RegisterSavedHotkey(bool reportFailure)
    {
        if (_hotkeySuspended) return;
        var hotkey = Hotkey.Parse(_settings.PauseHotkey);
        if (!_hotkey.TrySet(hotkey, out var error) && reportFailure)
            StartupToast.Show($"Pause key: {error}");
    }

    // ---- ISettingsHost

    public IReadOnlyList<ManagedApp> ManagedApps => _settings.ManagedApps;

    public void AddApp(ManagedApp app)
    {
        _autoMute.AddManagedApp(app);
        _autoMute.Reevaluate();
        SaveSettings();
    }

    public void RemoveApp(string processName)
    {
        _autoMute.RemoveManagedApp(processName);
        SaveSettings();
    }

    public void SetBackgroundVolume(string processName, int percent)
    {
        if (!_autoMute.ManagedApps.TryGetValue(processName, out var app)) return;
        _autoMute.AddManagedApp(app with { BackgroundVolume = Math.Clamp(percent, 0, 100) });
        // An app that is already lowered moves to the new level right away
        _autoMute.Reevaluate();
        SaveSettings();
    }

    public int FadeMs
    {
        get => _autoMute.FadeMs;
        set
        {
            _autoMute.FadeMs = Math.Max(0, value);
            SaveSettings();
        }
    }

    public int GraceMs
    {
        get => _autoMute.GraceMs;
        set
        {
            _autoMute.GraceMs = Math.Max(0, value);
            SaveSettings();
        }
    }

    public bool Notifications
    {
        get => _settings.NotificationsEnabled;
        set
        {
            _settings = _settings with { NotificationsEnabled = value };
            SaveSettings();
        }
    }

    public bool StartWithWindows
    {
        get => StartupRegistry.IsEnabled();
        set => StartupRegistry.SetEnabled(value);
    }

    public Hotkey? PauseHotkey => Hotkey.Parse(_settings.PauseHotkey);

    public string? SetPauseHotkey(Hotkey? hotkey)
    {
        // Registering is the only reliable way to know whether another app already owns the combination
        bool ok = _hotkey.TrySet(hotkey, out var error);
        if (ok)
        {
            _settings = _settings with { PauseHotkey = hotkey?.ToString() ?? "" };
            SaveSettings();
        }

        // While the box is capturing keys the hotkey stays off; it comes back in SuspendHotkey(false)
        if (_hotkeySuspended) _hotkey.Unregister();
        return ok ? null : error;
    }

    public void SuspendHotkey(bool suspended)
    {
        if (_hotkeySuspended == suspended) return;
        _hotkeySuspended = suspended;
        if (suspended) _hotkey.Unregister();
        else RegisterSavedHotkey(reportFailure: false);
    }

    public IReadOnlyList<RunningApp> GetRunningApps()
    {
        IReadOnlyList<AudioSession> sessions;
        try { sessions = _manager.GetSessions(); } catch { sessions = []; }
        return RunningApps.Find(sessions, _autoMute.IsManaged);
    }

    public string Version => CurrentVersion.ToString(3);

    public UpdateState UpdateState => _updateState;

    public string? AvailableVersion => _latest?.Version.ToString(3);

    public void InstallUpdate() => _ = InstallUpdateAsync();

    public void CheckForUpdates()
    {
        if (_updateState is UpdateState.Checking or UpdateState.Installing or UpdateState.Disabled) return;

        // Restart the timer so the next automatic check is a full interval after this one
        _updateTimer.Stop();
        _updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
        _updateTimer.Start();
        // Keep "checking for updates…" on screen for a moment: GitHub answers in a fraction of a second, and a
        // status that flips straight back looks like the click did nothing
        _ = CheckForUpdatesAsync(ManualCheckMinimum);
    }

    // ---- updates

    private static Version CurrentVersion =>
        Updater.Normalize(typeof(TrayApplicationContext).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static bool IsPublishedBuild => !File.Exists(Path.Combine(AppContext.BaseDirectory, "muten.dll"));

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    private void SetUpdateState(UpdateState state)
    {
        _updateState = state;
        if (_menu.Visible) RefreshMenu();
        StatusChanged?.Invoke();
    }

    private void CleanupAfterUpdate()
    {
        // The previous exe may take a moment to exit after starting this one
        if (Updater.CleanupOldFiles(ExePath)) return;

        int attempts = 0;
        var retry = new System.Windows.Forms.Timer { Interval = 3000 };
        retry.Tick += (_, _) =>
        {
            if (Updater.CleanupOldFiles(ExePath) || ++attempts >= 10)
            {
                retry.Stop();
                retry.Dispose();
            }
        };
        retry.Start();
    }

    private async Task CheckForUpdatesAsync(TimeSpan minimum = default)
    {
        if (_updateState is UpdateState.Installing or UpdateState.Disabled) return;
        if (_updateState != UpdateState.Available) SetUpdateState(UpdateState.Checking);

        var shown = System.Diagnostics.Stopwatch.StartNew();
        async Task WaitMinimum()
        {
            var remaining = minimum - shown.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        }

        try
        {
            var latest = await Updater.GetLatestAsync(_http);
            await WaitMinimum();
            if (latest is not null && Updater.IsNewer(latest.Version, CurrentVersion))
            {
                _latest = latest;
                SetUpdateState(UpdateState.Available);
            }
            else
            {
                _latest = null;
                SetUpdateState(UpdateState.UpToDate);
            }
        }
        catch
        {
            await WaitMinimum();
            SetUpdateState(_latest is not null ? UpdateState.Available : UpdateState.CheckFailed);
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_latest is null || _updateState == UpdateState.Installing) return;

        var release = _latest;
        UpdateSwap? swap = null;
        bool restored = false, started = false;
        SetUpdateState(UpdateState.Installing);

        try
        {
            var exePath = ExePath;
            var installDir = Path.GetDirectoryName(exePath)!;
            var workDir = Path.Combine(Path.GetTempPath(), "muten-update");

            var zip = await Updater.DownloadAsync(_http, release, workDir);
            var source = await Task.Run(() => Updater.Extract(zip, Path.GetFileName(exePath)));
            swap = Updater.Apply(source, installDir);

            // Hand every app its sound back before this process goes away
            _autoMute.RestoreAll();
            restored = true;

            Process.Start(new ProcessStartInfo(exePath, $"--updated {Environment.ProcessId}")
            {
                UseShellExecute = false,
                WorkingDirectory = installDir,
            });
            started = true;
        }
        catch
        {
            swap?.Rollback();
            if (restored && _autoMute.Enabled) _autoMute.Reevaluate();
            SetUpdateState(UpdateState.Available);

            try
            {
                Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true });
            }
            catch { }
            StartupToast.Show("Update failed. Opening the download page.");
        }

        if (started) Exit();
    }

    // ---- shutdown

    private void Shutdown()
    {
        if (_exited) return;
        _exited = true;

        _updateTimer.Stop();
        _updateTimer.Dispose();
        _hotkey.Dispose();
        _settingsForm?.Close();
        _autoMute.RestoreAll();
        _watcher.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _manager.Dispose();
        _http.Dispose();
        _activeIcon.Dispose();
        _pausedIcon.Dispose();
    }

    private void Exit()
    {
        Shutdown();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Shutdown();
        base.Dispose(disposing);
    }
}
