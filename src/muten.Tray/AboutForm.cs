using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace muten.Tray;

// About window, styled like Settings: the app card (logo, MUTEN in Teko, tagline), how it works, and a footer with Done
public class AboutForm : Form
{
    private const int ClientW = 372, Pad = 18;

    private static readonly string[] Steps =
    [
        "Click the tray icon to see your apps",
        "Click an app to muten it",
        "Set its background volume in Settings",
        "Switch back to hear it at full volume",
    ];

    private readonly Icon? _icon;
    private readonly List<IDisposable> _owned = [];

    private int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    public AboutForm()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = "About muten";
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = Own(Theme.Ui(9f));
        try { Icon = _icon = AppIcons.CreateWindowIcon(); } catch { }

        int left = S(Pad), y = S(16);

        // ---- app card: logo, name, tagline
        // logo.png is wider than tall: size the box to its real proportions so nothing is stretched or padded
        var logoImage = AppIcons.LoadEmbedded("logo.png");
        int logoW = S(76);
        int logoH = logoImage is null ? logoW : (int)Math.Round(logoW * logoImage.Height / (double)logoImage.Width);
        var logo = new PictureBox
        {
            Image = logoImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(logoW, logoH),
            Location = new Point(left, y),
        };
        var tagline = new Label
        {
            Text = "App audio control for Windows",
            Font = Own(Theme.Ui(9f)),
            ForeColor = Color.FromArgb(68, 68, 68),
            AutoSize = true,
        };
        // The name is drawn at 80% of the tagline's width
        var name = new NameMark("MUTEN", (int)Math.Round(tagline.PreferredWidth * 0.8));
        int textX = logo.Right + S(14);
        int textH = name.Height + S(6) + tagline.PreferredHeight;
        int cardH = Math.Max(logo.Height, textH);
        logo.Top = y + (cardH - logo.Height) / 2;
        name.Location = new Point(textX, y + (cardH - textH) / 2);
        tagline.Location = new Point(textX - S(1), name.Bottom + S(6));
        y += cardH + S(16);

        Controls.AddRange([logo, name, tagline]);

        // ---- how it works
        int clientW = S(ClientW);

        // Center the app card (logo + name + tagline) across the window
        int cardW = name.Right - logo.Left > tagline.Right - logo.Left ? name.Right - logo.Left : tagline.Right - logo.Left;
        int shift = (clientW - cardW) / 2 - logo.Left;
        foreach (Control c in new Control[] { logo, name, tagline }) c.Left += shift;

        Controls.Add(Line(0, y, clientW));
        y += 1 + S(14);

        var header = new Label
        {
            Text = "HOW IT WORKS",
            Font = Own(Theme.Ui(7.9f)),
            ForeColor = Theme.TextDim,
            AutoSize = true,
            Location = new Point(left - S(2), y),
        };
        Controls.Add(header);
        y = header.Top + header.PreferredHeight + S(8);

        var stepFont = Own(Theme.Ui(9f));
        for (int i = 0; i < Steps.Length; i++)
        {
            int rowH = S(20);
            var badge = new StepBadge(i + 1) { Size = new Size(S(18), S(18)), Location = new Point(left, y + (rowH - S(18)) / 2) };
            var step = new Label
            {
                Text = Steps[i],
                Font = stepFont,
                ForeColor = Color.FromArgb(51, 51, 51),
                AutoSize = true,
            };
            step.Location = new Point(badge.Right + S(9), y + (rowH - step.PreferredHeight) / 2);
            Controls.AddRange([badge, step]);
            y += rowH + S(5);
        }
        y += S(11);

        // ---- footer: Done
        Controls.Add(Line(0, y, clientW));
        y += 1;
        int footerH = S(46);
        var footer = new Panel { BackColor = Color.White, Bounds = new Rectangle(0, y, clientW, footerH) };

        var done = new Button
        {
            Text = "Done",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Red,
            ForeColor = Color.White,
            Font = Own(Theme.Ui(9f)),
            Size = new Size(S(84), S(28)),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            DialogResult = DialogResult.OK,
        };
        done.FlatAppearance.BorderSize = 0;
        done.FlatAppearance.MouseOverBackColor = Color.FromArgb(208, 60, 60);
        done.FlatAppearance.MouseDownBackColor = Color.FromArgb(190, 50, 50);
        done.Location = new Point(clientW - left - done.Width, (footerH - done.Height) / 2);

        footer.Controls.Add(done);
        Controls.Add(footer);
        AcceptButton = done;
        CancelButton = done;

        ClientSize = new Size(clientW, footer.Bottom);
        ResumeLayout(false);
    }

    private Label Line(int x, int y, int width) => new()
    {
        BackColor = Color.FromArgb(238, 238, 238),
        AutoSize = false,
        Bounds = new Rectangle(x, y, width, 1),
    };

    private T Own<T>(T item) where T : IDisposable
    {
        _owned.Add(item);
        return item;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var item in _owned) item.Dispose();
            _icon?.Dispose();
        }
        base.Dispose(disposing);
    }

    // The app name in Teko (embedded font, OFL), filled red. Drawn as a path so it can be sized to an exact
    // width and trimmed to the letters (Teko has a lot of space above and below them).
    private sealed class NameMark : Control
    {
        private static FontFamily? _teko;
        private static PrivateFontCollection? _fonts;
        private readonly GraphicsPath _path;

        public NameMark(string text, int width)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            var family = Teko ?? new FontFamily(Theme.FontName);

            // Lay the text out at 100px, then scale it so its letters are exactly 'width' wide
            // Winding fill: Teko's overlapping strokes would leave white gaps with the default alternate fill
            _path = new GraphicsPath(FillMode.Winding);
            _path.AddString(text, family, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
            var bounds = _path.GetBounds();
            float scale = width / bounds.Width;
            using var matrix = new Matrix();
            matrix.Scale(scale, scale);
            matrix.Translate(-bounds.X, -bounds.Y);
            _path.Transform(matrix);
            Size = new Size(width, (int)Math.Ceiling(bounds.Height * scale) + 1);
        }

        private static FontFamily? Teko
        {
            get
            {
                if (_teko is not null || _fonts is not null) return _teko;
                _fonts = new PrivateFontCollection();
                try
                {
                    using var stream = typeof(AboutForm).Assembly.GetManifestResourceStream("Teko-SemiBold.ttf");
                    if (stream is null) return null;
                    var data = new byte[stream.Length];
                    stream.ReadExactly(data);
                    // The collection keeps reading from this memory, so it stays allocated for the app's lifetime
                    IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, memory, data.Length);
                    _fonts.AddMemoryFont(memory, data.Length);
                    _teko = _fonts.Families.FirstOrDefault();
                }
                catch { _teko = null; }
                return _teko;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var brush = new SolidBrush(Theme.Red);
            e.Graphics.FillPath(brush, _path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _path.Dispose();
            base.Dispose(disposing);
        }
    }

    // Step number in muten's pink badge style
    private sealed class StepBadge : Control
    {
        private readonly string _text;
        private readonly Font _font = Theme.Ui(7.6f, FontStyle.Bold);

        public StepBadge(int number)
        {
            _text = number.ToString();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var fill = new SolidBrush(Theme.RedSoft)) g.FillEllipse(fill, r);
            using (var pen = new Pen(Theme.RedBorder)) g.DrawEllipse(pen, r);
            TextRenderer.DrawText(g, _text, _font, ClientRectangle, Theme.RedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }
    }
}
