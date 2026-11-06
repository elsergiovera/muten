using System.Drawing;
using System.Drawing.Drawing2D;

namespace muten.Tray;

// Colors, fonts and small drawing helpers shared by the tray menu and the settings window
internal static class Theme
{
    public static readonly Color Text = Color.FromArgb(30, 30, 30);
    public static readonly Color TextDim = Color.FromArgb(136, 136, 136);
    public static readonly Color TextFaint = Color.FromArgb(170, 170, 170);
    public static readonly Color Border = Color.FromArgb(207, 207, 207);
    public static readonly Color Separator = Color.FromArgb(230, 230, 230);
    public static readonly Color Hover = Color.FromArgb(238, 238, 238);

    public static readonly Color Managed = Color.FromArgb(245, 173, 173);       // #f5adad, muten's pink rows
    public static readonly Color ManagedHover = Color.FromArgb(240, 158, 158);
    public static readonly Color ManagedDim = Color.FromArgb(249, 210, 210);     // paused: pink at ~55%

    public static readonly Color Red = Color.FromArgb(224, 72, 72);              // #e04848
    public static readonly Color RedText = Color.FromArgb(204, 51, 51);
    public static readonly Color RedSoft = Color.FromArgb(253, 232, 232);
    public static readonly Color RedBorder = Color.FromArgb(227, 160, 160);

    public static readonly Color MutedBadgeText = Color.FromArgb(170, 51, 51);
    public static readonly Color MutedBadgeBorder = Color.FromArgb(224, 138, 138);
    public static readonly Color DuckBadgeText = Color.FromArgb(138, 90, 0);
    public static readonly Color DuckBadgeBorder = Color.FromArgb(226, 182, 90);

    public static readonly Color PausedStrip = Color.FromArgb(255, 244, 214);
    public static readonly Color PausedText = Color.FromArgb(107, 76, 0);
    public static readonly Color PausedButton = Color.FromArgb(255, 240, 194);  // #fff0c2
    public static readonly Color PausedDot = Color.FromArgb(245, 180, 0);

    public static readonly Color Green = Color.FromArgb(31, 138, 60);            // #1f8a3c
    public static readonly Color GreenText = Color.FromArgb(42, 138, 62);

    public const string FontName = "Segoe UI";

    public static Font Ui(float size = 9f, FontStyle style = FontStyle.Regular) => new(FontName, size, style);

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = RoundRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        using var path = RoundRect(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds,
        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine)
    {
        TextRenderer.DrawText(g, text, font, bounds, color, flags);
    }

    public static Size MeasureText(string text, Font font) =>
        TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

    // ---- vector glyphs (drawn into a box of the given size, centered on the rect)

    public static void PauseGlyph(Graphics g, Color color, RectangleF r)
    {
        using var brush = new SolidBrush(color);
        float w = r.Width * 0.25f, h = r.Height * 0.84f, top = r.Y + (r.Height - h) / 2;
        g.FillRectangle(brush, r.X + r.Width * 0.17f, top, w, h);
        g.FillRectangle(brush, r.X + r.Width * 0.58f, top, w, h);
    }

    public static void PlayGlyph(Graphics g, Color color, RectangleF r)
    {
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[]
        {
            new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.08f),
            new PointF(r.X + r.Width * 0.92f, r.Y + r.Height * 0.5f),
            new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.92f),
        });
    }

    // A cog: 8 square-ish teeth around a solid ring, with a round hole in the middle
    public static void GearGlyph(Graphics g, Color color, RectangleF r)
    {
        const int teeth = 8;
        var c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
        float tip = r.Width * 0.5f, root = r.Width * 0.36f, hole = r.Width * 0.15f;
        double step = Math.PI * 2 / teeth, half = step * 0.22;
        var points = new List<PointF>();
        PointF At(double angle, float radius) => new(c.X + (float)(Math.Cos(angle) * radius), c.Y + (float)(Math.Sin(angle) * radius));
        for (int i = 0; i < teeth; i++)
        {
            double a = i * step;
            points.Add(At(a - half * 1.35, root));
            points.Add(At(a - half, tip));
            points.Add(At(a + half, tip));
            points.Add(At(a + half * 1.35, root));
        }
        using var path = new GraphicsPath(FillMode.Alternate);
        path.AddPolygon(points.ToArray());
        path.AddEllipse(c.X - hole, c.Y - hole, hole * 2, hole * 2);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void PowerGlyph(Graphics g, Color color, RectangleF r)
    {
        float stroke = Math.Max(1.2f, r.Width * 0.14f);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float rad = r.Width * 0.36f;
        var c = new PointF(r.X + r.Width / 2, r.Y + r.Height * 0.55f);
        g.DrawArc(pen, c.X - rad, c.Y - rad, rad * 2, rad * 2, -55, 290);
        g.DrawLine(pen, c.X, r.Y + r.Height * 0.06f, c.X, c.Y - rad * 0.1f);
    }

    public static void ArrowUpGlyph(Graphics g, Color color, RectangleF r)
    {
        float stroke = Math.Max(1.4f, r.Width * 0.16f);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float cx = r.X + r.Width / 2;
        g.DrawLine(pen, cx, r.Y + r.Height * 0.15f, cx, r.Bottom - r.Height * 0.12f);
        g.DrawLines(pen, new[]
        {
            new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.45f),
            new PointF(cx, r.Y + r.Height * 0.15f),
            new PointF(r.Right - r.Width * 0.2f, r.Y + r.Height * 0.45f),
        });
    }

    public static void CrossGlyph(Graphics g, Color color, RectangleF r, float stroke = 1.2f)
    {
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
        g.DrawLine(pen, r.Right, r.Top, r.Left, r.Bottom);
    }

    public static void SearchGlyph(Graphics g, Color color, RectangleF r)
    {
        float stroke = Math.Max(1.2f, r.Width * 0.12f);
        using var pen = new Pen(color, stroke) { EndCap = LineCap.Round };
        float d = r.Width * 0.62f;
        g.DrawEllipse(pen, r.X, r.Y, d, d);
        g.DrawLine(pen, r.X + d * 0.85f, r.Y + d * 0.85f, r.Right, r.Bottom);
    }

    public static void FolderGlyph(Graphics g, RectangleF r)
    {
        using var back = new SolidBrush(Color.FromArgb(232, 178, 60));
        using var front = new SolidBrush(Color.FromArgb(250, 205, 95));
        g.FillRectangle(back, r.X, r.Y + r.Height * 0.12f, r.Width * 0.45f, r.Height * 0.2f);
        g.FillRectangle(back, r.X, r.Y + r.Height * 0.25f, r.Width, r.Height * 0.65f);
        g.FillPolygon(front, new[]
        {
            new PointF(r.X + r.Width * 0.12f, r.Y + r.Height * 0.4f),
            new PointF(r.Right, r.Y + r.Height * 0.4f),
            new PointF(r.Right - r.Width * 0.1f, r.Y + r.Height * 0.9f),
            new PointF(r.X, r.Y + r.Height * 0.9f),
        });
    }
}
