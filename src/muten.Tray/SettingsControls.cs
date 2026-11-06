using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace muten.Tray;

internal static class Dpi
{
    public static int S(this Control c, float v) => (int)Math.Round(v * c.DeviceDpi / 96f);
}

// Small pill switch, red when on
internal class ToggleSwitch : Control
{
    private bool _checked;

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = new Size(this.S(30), this.S(16));
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Sets the state without raising CheckedChanged
    public void SetSilently(bool value)
    {
        _checked = value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = this.S(16), w = this.S(30);
        var track = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w - 1, h - 1);
        Theme.FillRound(g, _checked ? Theme.Red : Color.FromArgb(204, 204, 204), track, h / 2);
        float knob = h - this.S(4);
        float x = _checked ? track.Right - this.S(2) - knob : track.X + this.S(2);
        using var white = new SolidBrush(Color.White);
        g.FillEllipse(white, x, track.Y + this.S(2), knob, knob);

        if (Focused && ShowFocusCues)
            Theme.DrawRound(g, Color.FromArgb(120, 0, 0, 0), RectangleF.Inflate(track, 1.5f, 1.5f), h / 2 + 1.5f);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Checked = !Checked;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

// 0–Maximum slider (default 0–100 in steps of 5) with a red fill and a round thumb
internal class VolumeSlider : Control
{
    public int Step { get; init; } = 5;
    public int Maximum { get; init; } = 100;
    private int _value;
    private bool _dragging;

    public event EventHandler? ValueChanged;

    public VolumeSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        TabStop = true;
    }

    public int Value
    {
        get => _value;
        set
        {
            int snapped = Math.Clamp((int)Math.Round(value / (double)Step) * Step, 0, Maximum);
            if (snapped == _value) return;
            _value = snapped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float ThumbRadius => this.S(7);
    private float TrackLeft => ThumbRadius + 1;
    private float TrackWidth => Width - 2 * (ThumbRadius + 1);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f;
        float trackH = this.S(4);
        var track = new RectangleF(TrackLeft, cy - trackH / 2, TrackWidth, trackH);
        Theme.FillRound(g, Color.FromArgb(221, 221, 221), track, trackH / 2);

        float x = TrackLeft + TrackWidth * _value / (float)Maximum;
        if (_value > 0)
            Theme.FillRound(g, Theme.Red, new RectangleF(track.X, track.Y, x - track.X, trackH), trackH / 2);

        float r = ThumbRadius;
        float stroke = this.S(2);
        using var white = new SolidBrush(Color.White);
        using var pen = new Pen(Theme.Red, Focused && ShowFocusCues ? stroke + 1 : stroke);
        var thumb = new RectangleF(x - r + stroke / 2, cy - r + stroke / 2, 2 * r - stroke, 2 * r - stroke);
        g.FillEllipse(white, thumb);
        g.DrawEllipse(pen, thumb);
    }

    private void SetFromX(int x)
    {
        if (TrackWidth <= 0) return;
        Value = (int)Math.Round((x - TrackLeft) / TrackWidth * Maximum);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta) * Step;
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= Step; break;
            case Keys.Right or Keys.Up: Value += Step; break;
            case Keys.PageDown: Value -= Math.Max(Step, Maximum / 4); break;
            case Keys.PageUp: Value += Math.Max(Step, Maximum / 4); break;
            case Keys.Home: Value = 0; break;
            case Keys.End: Value = Maximum; break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

// Read-only box that records the next key combination pressed in it
internal class HotkeyBox : TextBox
{
    private Hotkey? _hotkey;

    // Raised with the new combination, or null when cleared with Backspace/Delete
    public event Action<Hotkey?>? HotkeyChosen;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = Color.White;
        TextAlign = HorizontalAlignment.Center;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
        ShowValue();
    }

    public Hotkey? Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            ShowValue();
        }
    }

    private void ShowValue() => Text = _hotkey?.ToString() ?? "None";

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private static bool WinDown => (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Text = "Press keys…";
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        ShowValue();
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Tab || (keyData & Keys.Modifiers) != 0;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Let the box see combinations that would otherwise be dialog/menu keys (Alt+letter, Ctrl+Tab...)
        if (Focused && (keyData & Keys.KeyCode) != Keys.Tab)
        {
            OnKeyDown(new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        var key = e.KeyCode;
        var modifiers = HotkeyModifiers.None;
        if (e.Control) modifiers |= HotkeyModifiers.Control;
        if (e.Alt) modifiers |= HotkeyModifiers.Alt;
        if (e.Shift) modifiers |= HotkeyModifiers.Shift;
        if (WinDown) modifiers |= HotkeyModifiers.Win;

        if (modifiers == HotkeyModifiers.None && key is Keys.Back or Keys.Delete)
        {
            _hotkey = null;
            Text = "None";
            HotkeyChosen?.Invoke(null);
            return;
        }

        if (modifiers == HotkeyModifiers.None && key == Keys.Escape)
        {
            Parent?.SelectNextControl(this, true, true, true, true);
            return;
        }

        if (muten.Tray.Hotkey.IsModifierKey(key))
        {
            // Show the modifiers held so far
            var partial = new muten.Tray.Hotkey(modifiers, Keys.None).ToString();
            Text = partial.EndsWith("+None") ? partial[..^4] + "…" : "Press keys…";
            return;
        }

        var hotkey = new muten.Tray.Hotkey(modifiers, key);
        if (!hotkey.IsValid)
        {
            Text = "Add Ctrl, Alt or Win";
            return;
        }

        _hotkey = hotkey;
        Text = hotkey.ToString();
        HotkeyChosen?.Invoke(hotkey);
    }

    protected override void OnKeyPress(KeyPressEventArgs e) => e.Handled = true;
}

// Round "i" with a tooltip
internal class InfoIcon : Control
{
    public InfoIcon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(this.S(14), this.S(14));
        Cursor = Cursors.Help;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float d = this.S(13);
        var r = new RectangleF((Width - d) / 2f, (Height - d) / 2f, d - 1, d - 1);
        using var pen = new Pen(Color.FromArgb(190, 190, 190));
        g.DrawEllipse(pen, r);
        using var font = new Font(Theme.FontName, d * 0.62f, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "i", font, Rectangle.Round(r), Color.FromArgb(150, 150, 150),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

// One managed app in Settings: icon, name, slider, value and remove button
internal class AppRowControl : Control
{
    private readonly VolumeSlider _slider;
    private readonly Font _font = Theme.Ui(9f);
    private readonly Font _value = Theme.Ui(8.25f);
    private readonly Font _valueBold = Theme.Ui(8.25f, FontStyle.Bold);
    private bool _removeHover;

    public string ProcessName { get; }
    public string DisplayName { get; }
    public string? ExePath { get; }

    public event Action<AppRowControl, int>? VolumeChanged;
    public event Action<AppRowControl>? RemoveClicked;

    public AppRowControl(string processName, string displayName, string? exePath, int backgroundVolume, bool shaded)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        ProcessName = processName;
        DisplayName = displayName;
        ExePath = exePath;
        BackColor = shaded ? Color.FromArgb(250, 250, 250) : Color.White;

        _slider = new VolumeSlider { BackColor = BackColor, AccessibleName = $"{displayName} background volume" };
        _slider.Value = backgroundVolume;
        _slider.ValueChanged += (_, _) =>
        {
            Invalidate(ValueRect);
            VolumeChanged?.Invoke(this, _slider.Value);
        };
        Controls.Add(_slider);
        Height = this.S(28);
    }

    public int Value => _slider.Value;

    // Columns from the mockup: 18 | name | 120 slider | 38 value | 14 remove, gap 8, padding 4
    private Rectangle RemoveRect => new(Width - this.S(4) - this.S(14), 0, this.S(14), Height);
    private Rectangle ValueRect => new(RemoveRect.X - this.S(6) - this.S(38), 0, this.S(38), Height);
    private Rectangle SliderRect => new(ValueRect.X - this.S(8) - this.S(124), (Height - this.S(18)) / 2, this.S(124), this.S(18));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _slider.Bounds = SliderRect;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int icon = this.S(16);
        AppIcons.Draw(g, ExePath, DisplayName, new Rectangle(this.S(4), (Height - icon) / 2, icon, icon));

        int nameX = this.S(4) + this.S(18) + this.S(8);
        Theme.DrawText(g, DisplayName, _font, Theme.Text, new Rectangle(nameX, 0, SliderRect.X - this.S(8) - nameX, Height));

        bool mute = _slider.Value == 0;
        TextRenderer.DrawText(g, mute ? "Mute" : $"{_slider.Value}%", mute ? _valueBold : _value, ValueRect,
            mute ? Theme.RedText : Color.FromArgb(68, 68, 68),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var x = RemoveRect;
        float size = this.S(7);
        var cross = new RectangleF(x.X + (x.Width - size) / 2f, (Height - size) / 2f, size, size);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.CrossGlyph(g, _removeHover ? Theme.RedText : Color.FromArgb(187, 187, 187), cross, Math.Max(1.2f, this.S(1.2f)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = RemoveRect.Contains(e.Location);
        if (hover != _removeHover)
        {
            _removeHover = hover;
            Cursor = hover ? Cursors.Hand : Cursors.Default;
            Invalidate(RemoveRect);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_removeHover)
        {
            _removeHover = false;
            Cursor = Cursors.Default;
            Invalidate(RemoveRect);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && RemoveRect.Contains(e.Location))
            RemoveClicked?.Invoke(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose(); _value.Dispose(); _valueBold.Dispose();
        }
        base.Dispose(disposing);
    }
}
