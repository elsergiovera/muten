using System.Drawing;
using Microsoft.Win32;
using muten.Core;

namespace muten.Tray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AudioSessionManager _manager;
    private readonly AutoMuteService _autoMute;
    private readonly ForegroundWatcher _watcher;
    private MutenSettings _settings;
    private bool _keepMenuOpen;

    public TrayApplicationContext()
    {
        _manager = new AudioSessionManager();
        _settings = SettingsManager.Load();

        _autoMute = new AutoMuteService(_manager) { Enabled = _settings.AutoMuteEnabled };
        foreach (var app in _settings.ManagedApps)
        {
            _autoMute.AddManagedApp(app);
        }

        _autoMute.AppsMuted += OnAppsMuted;

        _watcher = new ForegroundWatcher();
        _watcher.ForegroundChanged += OnForegroundChanged;
        _watcher.Start();

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)!,
            Text = "muten",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip { Items = { "Loading..." } },
        };

        _notifyIcon.ContextMenuStrip!.Opening += OnMenuOpening;
        _notifyIcon.ContextMenuStrip!.Closing += OnMenuClosing;
        _notifyIcon.MouseClick += OnTrayClick;

        StartupToast.Show("Muten is running in the background.");
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
        _notifyIcon.ContextMenuStrip!.Invoke(() => StartupToast.Show(text));
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var menu = _notifyIcon.ContextMenuStrip!;
        menu.Items.Clear();

        var sessions = _manager.GetSessions()
            .Where(s => s.IsActive || _autoMute.IsManaged(s.ProcessName))
            .ToList();

        if (sessions.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("No active audio sessions") { Enabled = false });
        }
        else
        {
            foreach (var session in sessions)
            {
                var isManaged = _autoMute.IsManaged(session.ProcessName);

                var label = session.IsMuted
                    ? $"{session.DisplayName} (muten)"
                    : $"{session.DisplayName} ({session.Volume * 100:F0}%)";

                var item = new ToolStripMenuItem(label)
                {
                    Checked = isManaged,
                    CheckOnClick = false,
                    Image = GetProcessIcon(session.ExecutablePath),
                };

                if (isManaged)
                    item.BackColor = session.IsMuted
                        ? Color.FromArgb(245, 173, 173)
                        : Color.FromArgb(250, 214, 214);

                item.MouseDown += (_, _) => _keepMenuOpen = true;
                item.Click += (_, _) =>
                {
                    ToggleManaged(session);
                    RefreshMenu();
                };
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        var pauseItem = new ToolStripMenuItem("Pause")
        {
            Image = LoadEmbeddedImage("speaker_muted.png"),
        };
        if (!_autoMute.Enabled)
            pauseItem.BackColor = Color.FromArgb(214, 234, 250);
        pauseItem.MouseDown += (_, _) => _keepMenuOpen = true;
        pauseItem.Click += (_, _) =>
        {
            TogglePause();
            RefreshMenu();
        };
        menu.Items.Add(pauseItem);

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Image = LoadEmbeddedImage("startup.png"),
        };
        if (IsStartupEnabled())
            startupItem.BackColor = Color.FromArgb(214, 234, 250);
        startupItem.MouseDown += (_, _) => _keepMenuOpen = true;
        startupItem.Click += (_, _) =>
        {
            ToggleStartup();
            RefreshMenu();
        };
        menu.Items.Add(startupItem);

        var notifItem = new ToolStripMenuItem("Notifications")
        {
            Image = LoadEmbeddedImage("notification.png"),
        };
        if (_settings.NotificationsEnabled)
            notifItem.BackColor = Color.FromArgb(214, 234, 250);
        notifItem.MouseDown += (_, _) => _keepMenuOpen = true;
        notifItem.Click += (_, _) =>
        {
            _settings.NotificationsEnabled = !_settings.NotificationsEnabled;
            SaveSettings();
            RefreshMenu();
        };
        menu.Items.Add(notifItem);

        var helpItem = new ToolStripMenuItem("About");
        helpItem.Click += (_, _) => ShowHelp();
        menu.Items.Add(helpItem);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => Exit();
        menu.Items.Add(quit);
    }

    private void OnMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (_keepMenuOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
        {
            e.Cancel = true;
            _keepMenuOpen = false;
        }
    }

    private void RefreshMenu()
    {
        var menu = _notifyIcon.ContextMenuStrip!;
        OnMenuOpening(menu, new System.ComponentModel.CancelEventArgs());
    }

    private static Image? LoadEmbeddedImage(string name)
    {
        var asm = typeof(TrayApplicationContext).Assembly;
        using var stream = asm.GetManifestResourceStream(name);
        return stream is not null ? Image.FromStream(stream) : null;
    }

    private static Bitmap? GetProcessIcon(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;

        try
        {
            var icon = Icon.ExtractAssociatedIcon(exePath);
            return icon?.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    private void ToggleManaged(AudioSession session)
    {
        if (string.IsNullOrEmpty(session.ProcessName)) return;

        if (_autoMute.IsManaged(session.ProcessName))
            _autoMute.RemoveManagedApp(session.ProcessName);
        else
            _autoMute.AddManagedApp(new ManagedApp
            {
                ProcessName = session.ProcessName,
                ExePath = session.ExecutablePath,
                DisplayName = session.DisplayName,
            });

        SaveSettings();
    }

    private void TogglePause()
    {
        _autoMute.Enabled = !_autoMute.Enabled;

        if (!_autoMute.Enabled)
        {
            _autoMute.RestoreAll();
        }

        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings = new MutenSettings
        {
            ManagedApps = _autoMute.ManagedApps.Values.ToList(),
            AutoMuteEnabled = _autoMute.Enabled,
            NotificationsEnabled = _settings.NotificationsEnabled,
        };

        SettingsManager.Save(_settings);
    }

    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "muten";

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
        return key?.GetValue(StartupValueName) is not null;
    }

    private static void ToggleStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true)!;
        if (key.GetValue(StartupValueName) is not null)
        {
            key.DeleteValue(StartupValueName);
        }
        else
        {
            var exePath = Application.ExecutablePath;
            key.SetValue(StartupValueName, $"\"{exePath}\"");
        }
    }

    private static void ShowHelp()
    {
        using var form = new AboutForm();
        form.ShowDialog();
    }

    private void Exit()
    {
        _autoMute.RestoreAll();
        _watcher.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _manager.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoMute.RestoreAll();
            _watcher.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _manager.Dispose();
        }

        base.Dispose(disposing);
    }
}
