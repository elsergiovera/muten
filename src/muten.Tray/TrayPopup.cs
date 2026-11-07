using System.Drawing;

namespace muten.Tray;

// The main screen: the tray menu's view in a small borderless window that stays on top of everything (games too),
// so it can also be opened from a notification. Looks like the old tray menu: white, 1px grey border, menu shadow.
// It closes like a menu: clicking elsewhere, Esc, or opening Settings.
public sealed class TrayPopup : Form
{
    private const int CS_DROPSHADOW = 0x00020000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly TrayMenuView _view;
    private bool _growUp;

    // When it last closed because the user clicked elsewhere; a click on the tray icon closes it that way first, and
    // must not reopen it straight away
    public DateTime LastDeactivated { get; private set; }

    public TrayPopup(TrayMenuView view)
    {
        _view = view;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Theme.Border; // shows as the 1px border around the view
        Padding = new Padding(1);
        Text = "muten";

        _view.Location = new Point(1, 1);
        Controls.Add(_view);
        _view.SizeChanged += (_, _) => FitToView();
        FitToView();

        // Create the windows now (hidden), so the first show never appears before the view can paint
        CreateControl();
        if (!_view.IsHandleCreated) _ = _view.Handle;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;     // the same shadow Windows draws under menus
            cp.ExStyle |= WS_EX_TOOLWINDOW;     // no Alt+Tab entry
            return cp;
        }
    }

    // Opens next to 'anchor' (the cursor on the tray icon), or at the bottom-right corner when null (above where
    // notifications appear). Upwards when there is no room below, like the tray menu did.
    public void ShowAt(Point? anchor)
    {
        var area = Screen.FromPoint(anchor ?? Cursor.Position).WorkingArea;
        FitToView();

        int x, y;
        if (anchor is { } p)
        {
            x = p.X + Width <= area.Right ? p.X : p.X - Width;
            _growUp = p.Y > area.Top + area.Height / 2;
            y = _growUp ? Math.Min(p.Y, area.Bottom) - Height : p.Y;
        }
        else
        {
            x = area.Right - Width - 12;
            y = area.Bottom - Height - 12;
            _growUp = true;
        }
        Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)),
                             Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height)));

        if (!Visible) Show();
        Activate();
        BringToFront();
    }

    // The view changes size when the list changes; keep the window wrapped around it. When opened upwards (above the
    // tray), it grows and shrinks at the top so it stays above the taskbar.
    private void FitToView()
    {
        var size = new Size(_view.Width + 2, _view.Height + 2);
        if (size == Size) return;
        int bottom = Bottom;
        Size = size;
        if (Visible && _growUp) Top = bottom - Height;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!Visible) return;
        LastDeactivated = DateTime.UtcNow;
        Hide();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Closing the window (e.g. Alt+F4) only hides it; the app disposes it on exit
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
