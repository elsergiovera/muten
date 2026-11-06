using System.Drawing;
using System.Drawing.Drawing2D;

namespace muten.Tray;

public record TrayAppRow(
    string ProcessName,
    string Name,
    string? ExePath,
    bool Managed,
    int BackgroundVolume,   // managed apps: percent while in the background, 0 = muted
    int VolumePercent,      // the session's current volume
    bool SessionMuted);

public class TrayMenuModel
{
    public List<TrayAppRow> Apps { get; init; } = [];
    public bool Paused { get; init; }
    public string? UpdateVersion { get; init; } // non-null shows the green update bar
    public bool Installing { get; init; }
}

// The whole tray menu as one owner-drawn control, hosted in the ContextMenuStrip through a ToolStripControlHost.
// Clicks inside a hosted control never close the menu, so toggling apps keeps it open.
public class TrayMenuView : Control
{
    private enum HitKind { App, Add, Pause, Settings, Quit, Install }

    private record Hit(HitKind Kind, Rectangle Bounds, TrayAppRow? Row = null);

    private TrayMenuModel _model = new();
    private readonly List<Hit> _hits = [];
    private Hit? _hover;
    private Hit? _pressed;
    private Size _size;

    private readonly Font _font = Theme.Ui(9f);
    private readonly Font _small = Theme.Ui(8.25f);
    private readonly Font _badge = Theme.Ui(7.5f);
    private readonly Font _bold = Theme.Ui(9f, FontStyle.Bold);
    private readonly Font _button = Theme.Ui(8.6f);
    private readonly Font _buttonBold = Theme.Ui(8.6f, FontStyle.Bold);
    private readonly Font _strip = Theme.Ui(8.25f);
    private readonly Font _stripBold = Theme.Ui(8.25f, FontStyle.Bold);

    public event Action<TrayAppRow>? AppClicked;
    public event Action? AddClicked;
    public event Action? PauseClicked;
    public event Action? SettingsClicked;
    public event Action? QuitClicked;
    public event Action? InstallClicked;

    public TrayMenuView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        Margin = Padding.Empty;
        SetModel(new TrayMenuModel());
    }

    public TrayMenuModel Model => _model;

    private int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    public void SetModel(TrayMenuModel model)
    {
        _model = model;
        _hover = null;
        _pressed = null;
        _size = Measure(MeasureWidth());
        Size = _size;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) => _size;

    // The host may give the view more width than it asked for; lay the rows out across whatever it got
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width > 0) Measure(Width);
        Invalidate();
    }

    // ---- layout (logical 96-dpi units scaled with S)

    private const int Pad = 3, RowH = 26, StripH = 22, FooterH = 30, UpdateH = 40;

    private int MeasureWidth()
    {
        int widest = Theme.MeasureText("Add an app…", _font).Width;
        foreach (var row in _model.Apps)
            widest = Math.Max(widest, Theme.MeasureText(row.Name, _font).Width);
        // icon + gaps + right-hand badge
        int width = widest + S(8 + 16 + 7 + 10 + 44 + 8);
        return Math.Clamp(width, S(250), S(330));
    }

    private Size Measure(int w)
    {
        _hits.Clear();
        int y = S(Pad);

        if (_model.Paused) y += S(StripH);

        foreach (var row in _model.Apps)
        {
            _hits.Add(new Hit(HitKind.App, new Rectangle(0, y, w, S(RowH)), row));
            y += S(RowH);
        }
        if (_model.Apps.Count == 0) y += S(RowH); // "Nothing is playing"

        _hits.Add(new Hit(HitKind.Add, new Rectangle(0, y, w, S(RowH))));
        y += S(RowH);

        y += S(3) + 1 + S(3); // separator

        int footerX = S(3), gap = S(2);
        int buttonW = (w - footerX * 2 - gap * 2) / 3;
        var kinds = new[] { HitKind.Pause, HitKind.Settings, HitKind.Quit };
        for (int i = 0; i < 3; i++)
        {
            int x = footerX + i * (buttonW + gap);
            int bw = i == 2 ? w - footerX - x : buttonW;
            _hits.Add(new Hit(kinds[i], new Rectangle(x, y, bw, S(FooterH))));
        }
        y += S(FooterH);

        if (_model.UpdateVersion is not null)
        {
            y += S(4);
            var bar = new Rectangle(S(4), y, w - S(8), S(UpdateH));
            var installSize = Theme.MeasureText(_model.Installing ? "Installing…" : "Install", _buttonBold);
            var button = new Rectangle(bar.Right - S(8) - installSize.Width - S(20), bar.Y + (bar.Height - S(22)) / 2,
                installSize.Width + S(20), S(22));
            _hits.Add(new Hit(HitKind.Install, button));
            y += S(UpdateH) + S(1);
        }

        y += S(Pad);
        return new Size(w, y);
    }

    // ---- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        int w = Width;
        int y = S(Pad);

        if (_model.Paused)
        {
            DrawPausedStrip(g, new Rectangle(0, y, w, S(StripH)));
            y += S(StripH);
        }

        foreach (var hit in _hits)
        {
            switch (hit.Kind)
            {
                case HitKind.App:
                    DrawAppRow(g, hit);
                    break;
                case HitKind.Add:
                    if (_model.Apps.Count == 0)
                        DrawEmptyRow(g, new Rectangle(0, hit.Bounds.Y - S(RowH), w, S(RowH)));
                    DrawAddRow(g, hit);
                    using (var pen = new Pen(Theme.Separator))
                    {
                        int sy = hit.Bounds.Bottom + S(3);
                        g.DrawLine(pen, 0, sy, w, sy);
                    }
                    break;
                case HitKind.Pause:
                case HitKind.Settings:
                case HitKind.Quit:
                    DrawFooterButton(g, hit);
                    break;
                case HitKind.Install:
                    DrawUpdateBar(g, hit);
                    break;
            }
        }
    }

    private void DrawPausedStrip(Graphics g, Rectangle r)
    {
        using (var brush = new SolidBrush(Theme.PausedStrip)) g.FillRectangle(brush, r);
        int x = r.X + S(8);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PauseGlyph(g, Theme.PausedText, new RectangleF(x, r.Y + (r.Height - S(9)) / 2f, S(9), S(9)));
        g.SmoothingMode = SmoothingMode.None;
        x += S(9) + S(6);
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding
                                      | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var boldSize = Theme.MeasureText("Paused", _stripBold);
        Theme.DrawText(g, "Paused", _stripBold, Theme.PausedText, new Rectangle(x, r.Y, boldSize.Width + S(2), r.Height), flags);
        x += boldSize.Width + S(5);
        Theme.DrawText(g, "· everything plays", _strip, Theme.PausedText, new Rectangle(x, r.Y, r.Right - x - S(8), r.Height),
            flags | TextFormatFlags.EndEllipsis);
    }

    private void DrawAppRow(Graphics g, Hit hit)
    {
        var row = hit.Row!;
        var r = hit.Bounds;
        bool hover = hit == _hover;
        bool dim = row.Managed && _model.Paused;

        Color bg = row.Managed
            ? (dim ? Theme.ManagedDim : hover ? Theme.ManagedHover : Theme.Managed)
            : hover ? Theme.Hover : Color.White;
        using (var brush = new SolidBrush(bg)) g.FillRectangle(brush, r);

        int iconSize = S(16);
        AppIcons.Draw(g, row.ExePath, row.Name, new Rectangle(r.X + S(8), r.Y + (r.Height - iconSize) / 2, iconSize, iconSize), dim);

        // Right-hand side: badge for managed apps, plain volume otherwise
        int right = r.Right - S(8);
        int textLeft = r.X + S(8) + iconSize + S(7);

        if (row.Managed && !_model.Paused)
        {
            bool muted = row.BackgroundVolume <= 0;
            var text = muted ? "muted" : $"{row.BackgroundVolume}%";
            var size = Theme.MeasureText(text, _badge);
            var badge = new RectangleF(right - size.Width - S(12), r.Y + (r.Height - S(15)) / 2f, size.Width + S(12), S(15));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Color.White, badge, badge.Height / 2);
            Theme.DrawRound(g, muted ? Theme.MutedBadgeBorder : Theme.DuckBadgeBorder, badge, badge.Height / 2);
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, text, _badge, Rectangle.Round(badge), muted ? Theme.MutedBadgeText : Theme.DuckBadgeText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            right = (int)badge.X - S(6);
        }
        else
        {
            var text = row.SessionMuted ? "muted" : $"{row.VolumePercent}%";
            var size = Theme.MeasureText(text, _small);
            Theme.DrawText(g, text, _small, dim ? Theme.TextFaint : Theme.TextDim,
                new Rectangle(right - size.Width - S(2), r.Y, size.Width + S(2), r.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            right -= size.Width + S(8);
        }

        Theme.DrawText(g, row.Name, _font, dim ? Theme.TextDim : Theme.Text, new Rectangle(textLeft, r.Y, right - textLeft, r.Height));
    }

    private void DrawEmptyRow(Graphics g, Rectangle r)
    {
        Theme.DrawText(g, "Nothing is playing", _font, Theme.TextFaint, new Rectangle(r.X + S(8), r.Y, r.Width - S(16), r.Height));
    }

    private void DrawAddRow(Graphics g, Hit hit)
    {
        var r = hit.Bounds;
        if (hit == _hover)
            using (var brush = new SolidBrush(Theme.Hover)) g.FillRectangle(brush, r);
        int iconSize = S(16);
        AppIcons.DrawPlaceholder(g, new Rectangle(r.X + S(8), r.Y + (r.Height - iconSize) / 2, iconSize, iconSize));
        int textLeft = r.X + S(8) + iconSize + S(7);
        Theme.DrawText(g, "Add an app…", _font, Color.FromArgb(119, 119, 119), new Rectangle(textLeft, r.Y, r.Right - textLeft - S(8), r.Height));
    }

    private void DrawFooterButton(Graphics g, Hit hit)
    {
        var r = hit.Bounds;
        bool paused = hit.Kind == HitKind.Pause && _model.Paused;
        bool hover = hit == _hover;

        Color? bg = paused ? (hover ? Color.FromArgb(255, 231, 166) : Theme.PausedButton) : hover ? Theme.Hover : null;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (bg is { } color) Theme.FillRound(g, color, r, S(3));

        var text = hit.Kind switch
        {
            HitKind.Pause => paused ? "Resume" : "Pause",
            HitKind.Settings => "Settings",
            _ => "Quit",
        };
        var font = paused ? _buttonBold : _button;
        var textColor = paused ? Theme.PausedText : Color.FromArgb(51, 51, 51);
        var glyphColor = paused ? Theme.PausedText : Color.FromArgb(85, 85, 85);

        int glyph = S(12), gap = S(5);
        var textSize = Theme.MeasureText(text, font);
        int total = glyph + gap + textSize.Width;
        int x = r.X + (r.Width - total) / 2;
        var glyphRect = new RectangleF(x, r.Y + (r.Height - glyph) / 2f, glyph, glyph);

        switch (hit.Kind)
        {
            case HitKind.Pause when paused: Theme.PlayGlyph(g, glyphColor, glyphRect); break;
            case HitKind.Pause: Theme.PauseGlyph(g, glyphColor, glyphRect); break;
            case HitKind.Settings: Theme.GearGlyph(g, glyphColor, glyphRect); break;
            default: Theme.PowerGlyph(g, glyphColor, glyphRect); break;
        }
        g.SmoothingMode = SmoothingMode.None;

        Theme.DrawText(g, text, font, textColor, new Rectangle(x + glyph + gap, r.Y, textSize.Width + S(4), r.Height),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    private void DrawUpdateBar(Graphics g, Hit install)
    {
        var bar = new Rectangle(S(4), install.Bounds.Y + install.Bounds.Height / 2 - S(UpdateH) / 2, Width - S(8), S(UpdateH));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRound(g, Theme.Green, bar, S(3));

        int circle = S(20);
        var c = new RectangleF(bar.X + S(8), bar.Y + (bar.Height - circle) / 2f, circle, circle);
        using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, c);
        Theme.ArrowUpGlyph(g, Theme.Green, RectangleF.Inflate(c, -S(5), -S(4.5f)));

        var button = install.Bounds;
        bool hover = install == _hover && !_model.Installing;
        Theme.FillRound(g, hover ? Color.FromArgb(232, 245, 236) : Color.White, button, S(3));
        g.SmoothingMode = SmoothingMode.None;
        TextRenderer.DrawText(g, _model.Installing ? "Installing…" : "Install", _buttonBold, button,
            _model.Installing ? Color.FromArgb(120, 160, 130) : Theme.Green,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        int textX = (int)c.Right + S(8);
        int textW = button.X - S(6) - textX;
        int lineH = S(15);
        int top = bar.Y + (bar.Height - lineH * 2) / 2;
        Theme.DrawText(g, "Update available", _bold, Color.White, new Rectangle(textX, top, textW, lineH));
        Theme.DrawText(g, $"muten {_model.UpdateVersion}", _small, Color.FromArgb(225, 240, 228), new Rectangle(textX, top + lineH, textW, lineH));
    }

    // ---- input

    private Hit? HitTest(Point p) => _hits.FirstOrDefault(h => h.Bounds.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit != _hover)
        {
            _hover = hit;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is not null)
        {
            _hover = null;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = e.Button == MouseButtons.Left ? HitTest(e.Location) : null;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var hit = HitTest(e.Location);
        var pressed = _pressed;
        _pressed = null;
        if (hit is null || hit != pressed) return;

        switch (hit.Kind)
        {
            case HitKind.App: AppClicked?.Invoke(hit.Row!); break;
            case HitKind.Add: AddClicked?.Invoke(); break;
            case HitKind.Pause: PauseClicked?.Invoke(); break;
            case HitKind.Settings: SettingsClicked?.Invoke(); break;
            case HitKind.Quit: QuitClicked?.Invoke(); break;
            case HitKind.Install when !_model.Installing: InstallClicked?.Invoke(); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose(); _small.Dispose(); _badge.Dispose(); _bold.Dispose();
            _button.Dispose(); _buttonBold.Dispose(); _strip.Dispose(); _stripBold.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ContextMenuStrip sized to exactly wrap its hosted view plus a 1px border. A plain ContextMenuStrip re-applies
// menu padding (room for images/check marks) and adds width for text and shortcut columns.
public sealed class TrayMenuStrip : ContextMenuStrip
{
    private static readonly Padding BorderPadding = new(1);

    public TrayMenuStrip()
    {
        ShowImageMargin = false;
        ShowCheckMargin = false;
        LayoutStyle = ToolStripLayoutStyle.Table;
        Padding = BorderPadding;
    }

    protected override Padding DefaultPadding => BorderPadding;

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Padding != BorderPadding) Padding = BorderPadding;
        base.OnLayout(e);
    }

    // ToolStripControlHost does not always create its control's window when the popup opens; without one the
    // view never paints and the menu shows up empty
    protected override void OnOpened(EventArgs e)
    {
        foreach (var host in Items.OfType<ToolStripControlHost>())
        {
            var control = host.Control;
            if (!control.IsHandleCreated)
            {
                control.CreateControl();
                if (!control.IsHandleCreated) _ = control.Handle; // forces creation even if not yet marked visible
                control.Visible = true;
            }
            control.Invalidate();
        }
        base.OnOpened(e);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Items.Count == 0) return base.GetPreferredSize(proposedSize);
        var item = Items[0].Size;
        return new Size(item.Width + BorderPadding.Horizontal, item.Height + BorderPadding.Vertical);
    }
}

// Plain white menu with a thin grey border, matching the light Windows look
internal class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
        e.Graphics.Clear(Color.White);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
}
