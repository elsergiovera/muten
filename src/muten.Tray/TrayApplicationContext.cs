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

    private void OnForegroundChanged(string processName, int pid)
    {
        _autoMute.OnForegroundChanged(processName);
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
                var name = session.ProcessName;
                var isManaged = _autoMute.IsManaged(name);

                var label = session.IsMuted
                    ? $"{name} (muted)"
                    : $"{name} ({session.Volume * 100:F0}%)";

                var item = new ToolStripMenuItem(label)
                {
                    Checked = isManaged,
                    CheckOnClick = false,
                    Image = GetProcessIcon(session.ExecutablePath),
                };

                item.Click += (_, _) => ToggleManaged(name);
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

    private void ToggleManaged(string processName)
    {
        if (_autoMute.IsManaged(processName))
            _autoMute.RemoveManagedApp(processName);
        else
            _autoMute.AddManagedApp(processName);

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
            ManagedApps = _autoMute.ManagedApps.ToList(),
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
