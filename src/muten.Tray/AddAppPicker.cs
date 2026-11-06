using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace muten.Tray;

// "＋ Add" dropdown: search box, running apps (playing/silent) and "Browse for an .exe…"
public class AddAppPicker : ToolStripDropDown
{
    private const int RowH = 26, MaxRows = 6, PickerW = 240;

    private readonly List<RunningApp> _all;
    private readonly Panel _panel;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly Label _empty;
    private readonly BrowseRow _browse;
    private readonly Font _font = Theme.Ui(9f);
    private readonly Font _small = Theme.Ui(7.5f);

    public event Action<RunningApp>? Picked;
    public event Action? BrowseClicked;

    private int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    public AddAppPicker(IReadOnlyList<RunningApp> apps)
    {
        _all = apps.ToList();
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        DropShadowEnabled = true;
        Renderer = new TrayMenuRenderer();

        int pad = S(4), width = S(PickerW);
        _panel = new Panel { BackColor = Color.White, Margin = Padding.Empty, Padding = Padding.Empty };

        // Search box: a borderless TextBox inside a bordered panel with a magnifier on the left
        var searchFrame = new SearchFrame { Location = new Point(pad, pad), Size = new Size(width - 2 * pad, S(24)) };
        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.Ui(9f),
            Location = new Point(S(22), (S(24) - S(16)) / 2),
            Width = searchFrame.Width - S(22) - S(6),
        };
        searchFrame.Controls.Add(_search);
        _search.HandleCreated += (_, _) => SendMessage(_search.Handle, EM_SETCUEBANNER, (IntPtr)1, "Search running apps");
        _search.TextChanged += (_, _) => Filter();
        _search.KeyDown += OnSearchKeyDown;

        _list = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = S(RowH),
            IntegralHeight = false,
            Location = new Point(pad, searchFrame.Bottom + S(4)),
            Width = width - 2 * pad,
        };
        _list.DrawItem += OnDrawItem;
        _list.MouseMove += (_, e) =>
        {
            int index = _list.IndexFromPoint(e.Location);
            if (index >= 0 && index != _list.SelectedIndex) _list.SelectedIndex = index;
        };
        _list.MouseClick += (_, e) =>
        {
            int index = _list.IndexFromPoint(e.Location);
            if (index >= 0) Pick((RunningApp)_list.Items[index]);
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _list.SelectedItem is RunningApp app)
            {
                e.Handled = true;
                Pick(app);
            }
        };

        _empty = new Label
        {
            Text = "No other running apps",
            ForeColor = Theme.TextFaint,
            Font = Theme.Ui(9f),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = _list.Location,
            Size = new Size(_list.Width, S(RowH)),
            Visible = false,
        };

        _browse = new BrowseRow { Width = width - 2 * pad, Height = S(30) };
        _browse.Click += (_, _) =>
        {
            Close(ToolStripDropDownCloseReason.ItemClicked);
            BrowseClicked?.Invoke();
        };

        _panel.Controls.AddRange([searchFrame, _list, _empty, _browse]);

        var host = new ToolStripControlHost(_panel)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        Items.Add(host);
        CreateControl(); // hidden; makes sure the hosted panel has its windows before the first show

        Filter();
        Opened += (_, _) => _search.Focus();
    }

    public TextBox SearchBox => _search;

    public void Filter()
    {
        var text = _search.Text.Trim();
        var matches = _all.Where(a => text.Length == 0
                                      || a.DisplayName.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                                      || a.ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var app in matches) _list.Items.Add(app);
        _list.EndUpdate();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        _empty.Text = _all.Count == 0 ? "No other running apps" : "No match";
        _empty.Visible = matches.Count == 0;
        _list.Visible = matches.Count > 0;

        int rows = Math.Clamp(matches.Count, 1, MaxRows);
        _list.Height = rows * S(RowH);
        _browse.Location = new Point(_list.Left, _list.Bottom + S(4));
        _panel.Size = new Size(S(PickerW), _browse.Bottom + S(2));
        Items[0].Size = _panel.Size;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down when _list.Items.Count > 0:
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                break;
            case Keys.Up when _list.Items.Count > 0:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                break;
            case Keys.Enter when _list.SelectedItem is RunningApp app:
                Pick(app);
                break;
            case Keys.Escape:
                Close(ToolStripDropDownCloseReason.Keyboard);
                break;
            default:
                return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void Pick(RunningApp app)
    {
        Close(ToolStripDropDownCloseReason.ItemClicked);
        Picked?.Invoke(app);
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var app = (RunningApp)_list.Items[e.Index];
        var g = e.Graphics;
        var r = e.Bounds;
        bool selected = (e.State & DrawItemState.Selected) != 0;

        using (var bg = new SolidBrush(Color.White)) g.FillRectangle(bg, r);
        if (selected)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Theme.RedSoft, r, S(3));
            g.SmoothingMode = SmoothingMode.None;
        }

        int icon = S(16);
        AppIcons.Draw(g, app.ExePath, app.DisplayName, new Rectangle(r.X + S(4), r.Y + (r.Height - icon) / 2, icon, icon));

        var tag = app.Playing ? "playing" : "silent";
        var tagSize = Theme.MeasureText(tag, _small);
        TextRenderer.DrawText(g, tag, _small, new Rectangle(r.Right - S(4) - tagSize.Width, r.Y, tagSize.Width, r.Height),
            Color.FromArgb(153, 153, 153), TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        int x = r.X + S(4) + icon + S(7);
        Theme.DrawText(g, app.DisplayName, _font, Theme.Text, new Rectangle(x, r.Y, r.Right - S(4) - tagSize.Width - S(8) - x, r.Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
            _small.Dispose();
        }
        base.Dispose(disposing);
    }

    private const int EM_SETCUEBANNER = 0x1501;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    private class SearchFrame : Panel
    {
        public SearchFrame()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(Color.FromArgb(200, 200, 200)))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = this.S(12);
            Theme.SearchGlyph(g, Color.FromArgb(110, 110, 110), new RectangleF(this.S(6), (Height - size) / 2f, size, size));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Controls[0].Focus();
        }
    }

    private class BrowseRow : Control
    {
        private bool _hover;
        private readonly Font _font = Theme.Ui(8.6f);

        public BrowseRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            using (var pen = new Pen(Color.FromArgb(238, 238, 238))) g.DrawLine(pen, 0, 0, Width, 0);
            var body = new Rectangle(0, this.S(2), Width, Height - this.S(2));
            if (_hover)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.FillRound(g, Theme.Hover, body, this.S(3));
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float icon = this.S(14);
            Theme.FolderGlyph(g, new RectangleF(this.S(5), body.Y + (body.Height - icon) / 2f, icon, icon * 0.85f));
            g.SmoothingMode = SmoothingMode.None;
            Theme.DrawText(g, "Browse for an .exe…", _font, Color.FromArgb(85, 85, 85),
                new Rectangle(this.S(5) + (int)icon + this.S(7), body.Y, Width, body.Height));
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }
    }
}
