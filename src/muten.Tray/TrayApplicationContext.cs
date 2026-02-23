using muten.Core;

namespace muten.Tray;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly AudioSessionManager _manager;

    public TrayApplicationContext()
    {
        _manager = new AudioSessionManager();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "muten",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };

        _notifyIcon.ContextMenuStrip!.Opening += OnMenuOpening;
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var menu = _notifyIcon.ContextMenuStrip!;
        menu.Items.Clear();

        var sessions = _manager.GetSessions();

        if (sessions.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("No active audio sessions") { Enabled = false });
        }
        else
        {
            foreach (var session in sessions)
            {
                var label = session.IsMuted
                    ? $"[MUTED] {session.ProcessName}"
                    : $"{session.ProcessName} ({session.Volume * 100:F0}%)";

                var item = new ToolStripMenuItem(label);
                var pid = session.ProcessId;
                item.Click += (_, _) => _manager.ToggleMute(pid);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => Exit();
        menu.Items.Add(quit);
    }

    private void Exit()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _manager.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _manager.Dispose();
        }

        base.Dispose(disposing);
    }
}
