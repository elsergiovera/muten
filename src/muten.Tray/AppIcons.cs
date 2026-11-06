using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace muten.Tray;

// App icons for menu/settings rows, cached per exe path, plus the tray icon with its status dot
internal static class AppIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static Bitmap? _appImage;

    public static Bitmap? Get(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (Cache.TryGetValue(exePath, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(exePath))
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                bitmap = icon?.ToBitmap();
            }
        }
        catch
        {
            // Access denied or not an icon-bearing file
        }

        Cache[exePath] = bitmap;
        return bitmap;
    }

    // Draws the app icon, or a grey rounded square with the name's first letter when there is none
    public static void Draw(Graphics g, string? exePath, string name, Rectangle r, bool dim = false)
    {
        var icon = Get(exePath);
        if (icon is not null)
        {
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (dim)
            {
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.5f });
                g.DrawImage(icon, r, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attrs);
            }
            else
            {
                g.DrawImage(icon, r);
            }
            g.Restore(state);
            return;
        }

        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRound(g, dim ? Color.FromArgb(200, 200, 200) : Color.FromArgb(150, 150, 150), r, r.Width * 0.2f);
        g.SmoothingMode = smoothing;
        var letter = string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();
        using var font = new Font(Theme.FontName, r.Height * 0.55f, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, letter, font, r, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    // Dashed placeholder used by "Add an app…"
    public static void DrawPlaceholder(Graphics g, Rectangle r)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Width * 0.2f);
        using var pen = new Pen(Color.FromArgb(170, 170, 170), 1f) { DashPattern = [2f, 2f] };
        g.DrawPath(pen, path);
        g.SmoothingMode = smoothing;
    }

    public static Bitmap? AppImage => _appImage ??= LoadEmbedded("icon.png");

    public static Bitmap? LoadEmbedded(string name)
    {
        try
        {
            using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream(name);
            return stream is null ? null : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // muten's icon with a small status dot in the bottom-right corner (red active, yellow paused)
    public static Icon CreateTrayIcon(Color dot) => CreateIcon(SystemInformation.SmallIconSize.Width, dot);

    // muten's icon for window title bars
    public static Icon CreateWindowIcon() => CreateIcon(32, null);

    private static Icon CreateIcon(int size, Color? dot)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var image = AppImage;
            if (image is not null)
                g.DrawImage(image, new Rectangle(0, 0, size, size));
            else
                using (var fallback = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    if (fallback is not null) g.DrawIcon(fallback, new Rectangle(0, 0, size, size));

            if (dot is { } color)
            {
                float d = size * 0.45f;
                float ring = Math.Max(1f, size / 16f);
                var outer = new RectangleF(size - d, size - d, d, d);
                using (var ringBrush = new SolidBrush(Color.FromArgb(220, 32, 32, 32)))
                    g.FillEllipse(ringBrush, outer);
                using var dotBrush = new SolidBrush(color);
                g.FillEllipse(dotBrush, RectangleF.Inflate(outer, -ring, -ring));
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
