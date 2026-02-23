using System.Drawing;
using System.Reflection;

namespace muten.Tray;

public class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);

        var pad = 16;
        var y = pad;

        // Logo on the left
        var logo = LoadResource("logo.png");
        var logoSize = 100;
        var logoBox = new PictureBox
        {
            Image = logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(logoSize, logoSize),
            Location = new Point(pad, y),
        };

        // Heading + steps to the right of logo
        var textX = pad + logoSize + 14;

        var heading = new Label
        {
            Text = "How to muten:",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 30),
            AutoSize = true,
            Location = new Point(textX, y),
        };

        var stepY = heading.Bottom + 8;
        var steps = new[]
        {
            "Click the tray icon to see audio sessions",
            "Click an app to muten it",
            "Mutened apps mute when inactive",
            "Switch back to unmute",
        };

        for (var i = 0; i < steps.Length; i++)
        {
            var number = new Label
            {
                Text = $"{i + 1}.",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 100, 100),
                Size = new Size(18, 18),
                Location = new Point(textX, stepY),
            };

            var step = new Label
            {
                Text = steps[i],
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(60, 60, 60),
                AutoSize = true,
                Location = new Point(textX + 20, stepY),
            };

            Controls.AddRange([number, step]);
            stepY = step.Bottom + 3;
        }

        y = Math.Max(logoBox.Bottom, stepY) + 14;

        // Separator
        var separator = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Size = new Size(0, 2),
            Location = new Point(pad, y),
        };

        y = separator.Bottom + 14;

        // Screenshot panel with subtle background
        var screenshot = LoadResource("tray.png");
        var imgWidth = screenshot?.Width ?? 0;
        var imgHeight = screenshot?.Height ?? 0;
        var contentWidth = Math.Max(textX - pad + 240, imgWidth + 24);

        var screenshotPanel = new Panel
        {
            BackColor = Color.FromArgb(245, 245, 245),
            Location = new Point(pad, y),
            Size = new Size(contentWidth, imgHeight + 16),
        };

        var pictureBox = new PictureBox
        {
            Image = screenshot,
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point((contentWidth - imgWidth) / 2, 8),
        };
        screenshotPanel.Controls.Add(pictureBox);

        y = screenshotPanel.Bottom + 14;

        // Ready button
        var closeBtn = new Button
        {
            Text = "Ready",
            Size = new Size(90, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(240, 240, 240),
            ForeColor = Color.FromArgb(30, 30, 30),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK,
        };
        closeBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        closeBtn.Location = new Point(pad + contentWidth - closeBtn.Width, y);

        y = closeBtn.Bottom + pad;

        // Set separator width now that contentWidth is known
        separator.Size = new Size(contentWidth, 2);

        Controls.AddRange([logoBox, heading, separator, screenshotPanel, closeBtn]);

        AcceptButton = closeBtn;
        ClientSize = new Size(contentWidth + pad * 2, y);
    }

    private static Image? LoadResource(string name)
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(name);
        return stream != null ? Image.FromStream(stream) : null;
    }
}
