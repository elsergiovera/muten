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

    public TrayApplicationContext()
    {
        _manager = new AudioSessionManager();
        _settings = SettingsManager.Load();

        _autoMute = new AutoMuteService(_manager) { Enabled = _settings.AutoMuteEnabled };
        foreach (var app in _settings.ManagedApps)
        {
            _autoMute.AddManagedApp(app);
        }

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

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var menu = _notifyIcon.ContextMenuStrip!;
        menu.Items.Clear();

        var sessions = _manager.GetSessions()
            .Where(s => s.IsActive || _autoMute.IsManaged(s.ExecutablePath))
            .ToList();

        if (sessions.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("No active audio sessions") { Enabled = false });
        }
        else
        {
            foreach (var session in sessions)
            {
                var isManaged = _autoMute.IsManaged(session.ExecutablePath);

                var label = session.IsMuted
                    ? $"{session.DisplayName} (muted)"
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

                item.Click += (_, _) => ToggleManaged(session);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        var pauseLabel = _autoMute.Enabled ? "Pause auto-mute" : "Resume auto-mute";
        var pauseItem = new ToolStripMenuItem(pauseLabel);
        pauseItem.Click += (_, _) => TogglePause();
        menu.Items.Add(pauseItem);

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = IsStartupEnabled(),
            CheckOnClick = false,
        };
        startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(startupItem);

        var helpItem = new ToolStripMenuItem("About");
        helpItem.Click += (_, _) => ShowHelp();
        menu.Items.Add(helpItem);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => Exit();
        menu.Items.Add(quit);
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
        if (string.IsNullOrEmpty(session.ExecutablePath)) return;

        if (_autoMute.IsManaged(session.ExecutablePath))
            _autoMute.RemoveManagedApp(session.ExecutablePath);
        else
            _autoMute.AddManagedApp(new ManagedApp
            {
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
