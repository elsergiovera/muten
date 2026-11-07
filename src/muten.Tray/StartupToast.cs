using System.Drawing;
using System.Drawing.Drawing2D;

namespace muten.Tray;

public static class StartupToast
{
    private class NoActivateForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;
    }
    /// <summary>
    /// Shows a toast and returns immediately (non-blocking). The toast fades out on its own, but stays while the
    /// pointer is over it. With onClick, clicking it closes it and runs onClick (e.g. opens the main screen).
    /// </summary>
    public static void Show(string message, Action? onClick = null)
    {
        var toast = new NoActivateForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = Color.FromArgb(30, 30, 30),
            Size = new Size(280, 50),
            Opacity = 0,
        };

        var iconSize = 24;
        var iconBox = new PictureBox
        {
            Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(iconSize, iconSize),
            Location = new Point(12, (50 - iconSize) / 2),
            BackColor = Color.Transparent,
        };
        toast.Controls.Add(iconBox);

        var label = new Label
        {
            Text = message,
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 9f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(42, 0),
            Size = new Size(230, 50),
        };
        toast.Controls.Add(label);

        // Round corners
        var radius = 10;
        var path = new GraphicsPath();
        path.AddArc(0, 0, radius, radius, 180, 90);
        path.AddArc(toast.Width - radius, 0, radius, radius, 270, 90);
        path.AddArc(toast.Width - radius, toast.Height - radius, radius, radius, 0, 90);
        path.AddArc(0, toast.Height - radius, radius, radius, 90, 90);
        path.CloseFigure();
        toast.Region = new Region(path);

        // Position above the tray (bottom-right of screen)
        var screen = Screen.PrimaryScreen!.WorkingArea;
        toast.Location = new Point(screen.Right - toast.Width - 16, screen.Bottom - toast.Height - 16);

        var step = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 30 };

        void Finish()
        {
            timer.Stop();
            timer.Dispose();
            toast.Close();
            toast.Dispose();
        }

        if (onClick is not null)
        {
            void Clicked(object? sender, EventArgs e)
            {
                if (toast.IsDisposed) return;
                Finish();
                onClick();
            }
            foreach (Control c in new Control[] { toast, iconBox, label })
            {
                c.Cursor = Cursors.Hand;
                c.Click += Clicked;
            }
        }

        toast.Show();

        // Fade in, hold, fade out. While the pointer is over it, it stays fully visible; after the pointer leaves it
        // holds a little longer before fading.
        timer.Tick += (_, _) =>
        {
            if (step >= 10 && toast.Bounds.Contains(Cursor.Position))
            {
                step = Math.Min(step, 60);
                toast.Opacity = 1;
                return;
            }
            step++;
            if (step <= 10)
            {
                toast.Opacity = step / 10.0;
            }
            else if (step <= 80) // hold ~2 seconds
            {
                // wait
            }
            else if (step <= 90)
            {
                toast.Opacity = (90 - step) / 10.0;
            }
            else
            {
                Finish();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Shows a toast and blocks until it finishes (for use without an existing message loop).
    /// </summary>
    public static void ShowBlocking(string message)
    {
        var toast = new NoActivateForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = Color.FromArgb(30, 30, 30),
            Size = new Size(280, 50),
            Opacity = 0,
        };

        var iconSize = 24;
        var iconBox = new PictureBox
        {
            Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(iconSize, iconSize),
            Location = new Point(12, (50 - iconSize) / 2),
            BackColor = Color.Transparent,
        };
        toast.Controls.Add(iconBox);

        var label = new Label
        {
            Text = message,
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 9f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(42, 0),
            Size = new Size(230, 50),
        };
        toast.Controls.Add(label);

        var radius = 10;
        var path = new GraphicsPath();
        path.AddArc(0, 0, radius, radius, 180, 90);
        path.AddArc(toast.Width - radius, 0, radius, radius, 270, 90);
        path.AddArc(toast.Width - radius, toast.Height - radius, radius, radius, 0, 90);
        path.AddArc(0, toast.Height - radius, radius, radius, 90, 90);
        path.CloseFigure();
        toast.Region = new Region(path);

        var screen = Screen.PrimaryScreen!.WorkingArea;
        toast.Location = new Point(screen.Right - toast.Width - 16, screen.Bottom - toast.Height - 16);

        var step = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            step++;
            if (step <= 10)
            {
                toast.Opacity = step / 10.0;
            }
            else if (step <= 80)
            {
                // wait
            }
            else if (step <= 90)
            {
                toast.Opacity = (90 - step) / 10.0;
            }
            else
            {
                timer.Stop();
                timer.Dispose();
                toast.Close();
                toast.Dispose();
                Application.ExitThread();
            }
        };
        timer.Start();

        Application.Run(toast);
    }
}
