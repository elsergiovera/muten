using System.Drawing;
using muten.Core;

namespace muten.Tray;

// Single small Settings window: managed apps with their background volume, behavior options, version/update footer
public class SettingsForm : Form
{
    private const int ClientW = 400, Pad = 14, MaxVisibleRows = 8;

    private record Choice(string Text, int Ms)
    {
        public override string ToString() => Text;
    }

    private static readonly Choice[] FadeChoices = [new("Off", 0), new("Short", 150), new("Long", 400)];

    private readonly ISettingsHost _host;
    private readonly Label _appsHeader;
    private readonly Button _addButton;
    private readonly Panel _appsPanel;
    private readonly Label _behaviorHeader;
    private readonly Control[] _behavior;
    private readonly ComboBox _fade;
    private readonly Panel _wait;
    private readonly VolumeSlider _waitSlider;
    private readonly Label _waitValue;
    private readonly ToggleSwitch _notifications;
    private readonly ToggleSwitch _startup;
    private readonly HotkeyBox _hotkey;
    private readonly Label _footerLine;
    private readonly Panel _footer;
    private readonly Button _done;
    private readonly ToolTip _toolTip = new() { InitialDelay = 200, AutoPopDelay = 8000 };
    private readonly Font _headerFont = Theme.Ui(7.9f);
    private readonly Font _labelFont = Theme.Ui(9f);
    private readonly Font _footerFont = Theme.Ui(8.25f);

    private readonly Icon? _icon;
    private AddAppPicker? _picker;
    private bool _updating;

    private int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);

    public SettingsForm(ISettingsHost host)
    {
        _host = host;

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = "muten";
        Font = _labelFont;
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        try { Icon = _icon = AppIcons.CreateWindowIcon(); } catch { }

        // ---- apps section
        _appsHeader = SectionHeader("APPS · VOLUME IN THE BACKGROUND");
        _addButton = new Button
        {
            Text = "+ Add",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Theme.RedText,
            Font = Theme.Ui(8.6f),
            Size = new Size(S(58), S(23)),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        _addButton.FlatAppearance.BorderColor = Theme.RedBorder;
        _addButton.FlatAppearance.MouseOverBackColor = Theme.RedSoft;
        _addButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(250, 214, 214);
        _addButton.Click += (_, _) => OpenPicker();

        _appsPanel = new Panel { BackColor = Color.White, AutoScroll = false };

        // ---- behavior section
        _behaviorHeader = SectionHeader("BEHAVIOR");

        _fade = Combo(FadeChoices, _host.FadeMs);
        _fade.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && _fade.SelectedItem is Choice c) _host.FadeMs = c.Ms;
        };

        // Wait: a 0–1 s slider in 0.1 s steps, with the value next to it
        _waitSlider = new VolumeSlider { Maximum = 10, Step = 1, Size = new Size(S(70), S(22)), BackColor = Color.White, AccessibleName = "Wait before muting" };
        _waitValue = new Label { AutoSize = false, Size = new Size(S(38), S(22)), Font = _labelFont, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(68, 68, 68) };
        _wait = new Panel { Size = new Size(S(70 + 38 + 2), S(22)), BackColor = Color.White };
        _waitSlider.Location = new Point(0, 0);
        _waitValue.Location = new Point(S(72), 0);
        _wait.Controls.AddRange([_waitSlider, _waitValue]);
        _waitSlider.Value = (int)Math.Round(Math.Clamp(_host.GraceMs, 0, 1000) / 100.0);
        ShowWait();
        _waitSlider.ValueChanged += (_, _) =>
        {
            ShowWait();
            _host.GraceMs = _waitSlider.Value * 100;
        };
        var waitInfo = new InfoIcon();
        _toolTip.SetToolTip(waitInfo, "Quick alt-tabs and pop-ups won't cut the sound");

        _notifications = new ToggleSwitch { AccessibleName = "Notifications" };
        _notifications.SetSilently(_host.Notifications);
        _notifications.CheckedChanged += (_, _) => _host.Notifications = _notifications.Checked;

        _startup = new ToggleSwitch { AccessibleName = "Start with Windows" };
        _startup.SetSilently(_host.StartWithWindows);
        _startup.CheckedChanged += (_, _) => OnStartupChanged();

        _hotkey = new HotkeyBox { Width = S(96), Hotkey = _host.PauseHotkey, AccessibleName = "Pause key" };
        _hotkey.GotFocus += (_, _) => _host.SuspendHotkey(true);
        _hotkey.LostFocus += (_, _) => _host.SuspendHotkey(false);
        _hotkey.HotkeyChosen += OnHotkeyChosen;
        _toolTip.SetToolTip(_hotkey, "Click, then press a key combination. Backspace clears it.");

        _behavior =
        [
            OptionLabel("Fade"), _fade,
            OptionLabel("Wait"), waitInfo, _wait,
            OptionLabel("Notifications"), _notifications,
            OptionLabel("Start with Windows"), _startup,
            OptionLabel("Pause key"), _hotkey,
        ];

        // ---- footer
        _footerLine = new Label { BackColor = Color.FromArgb(238, 238, 238), AutoSize = false };
        _footer = new Panel { BackColor = Color.White };
        _done = new Button
        {
            Text = "Done",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Red,
            ForeColor = Color.White,
            Font = Theme.Ui(9f),
            Size = new Size(S(84), S(28)),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        _done.FlatAppearance.BorderSize = 0;
        _done.FlatAppearance.MouseOverBackColor = Color.FromArgb(208, 60, 60);
        _done.FlatAppearance.MouseDownBackColor = Color.FromArgb(190, 50, 50);
        _done.Click += (_, _) => Close();
        _footer.Controls.Add(_done);

        Controls.AddRange([_appsHeader, _addButton, _appsPanel, _behaviorHeader]);
        Controls.AddRange(_behavior);
        Controls.AddRange([_footerLine, _footer]);
        AcceptButton = _done;

        RebuildApps();
        RefreshFooter();
        ResumeLayout(false);

        _host.StatusChanged += OnHostStatusChanged;
    }

    public AddAppPicker? Picker => _picker;

    // ---- building blocks

    private Label SectionHeader(string text) => new()
    {
        Text = text,
        Font = _headerFont,
        ForeColor = Theme.TextDim,
        AutoSize = true,
        BackColor = Color.White,
    };

    private Label OptionLabel(string text) => new()
    {
        Text = text,
        Font = _labelFont,
        ForeColor = Theme.Text,
        AutoSize = true,
        BackColor = Color.White,
    };

    private void ShowWait() => _waitValue.Text = _waitSlider.Value == 0 ? "Off" : $"{_waitSlider.Value / 10.0:0.0} s";

    private ComboBox Combo(Choice[] choices, int currentMs)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = S(76),
            Font = Theme.Ui(8.6f),
        };
        combo.Items.AddRange(choices);
        _updating = true;
        combo.SelectedItem = choices.MinBy(c => Math.Abs(c.Ms - currentMs));
        _updating = false;
        return combo;
    }

    // ---- layout

    private void LayoutAll()
    {
        int inner = S(ClientW - 2 * Pad);
        int left = S(Pad);
        int y = S(10);

        _addButton.Location = new Point(left + inner - _addButton.Width, y);
        _appsHeader.Location = new Point(left - S(2), y + (_addButton.Height - _appsHeader.PreferredHeight) / 2);
        y = _addButton.Bottom + S(5);

        int rowH = S(28);
        int rows = Math.Max(1, _appsPanel.Controls.Count);
        int visible = Math.Min(rows, MaxVisibleRows);
        _appsPanel.AutoScroll = rows > MaxVisibleRows;
        _appsPanel.Bounds = new Rectangle(left, y, inner, visible * rowH);
        int rowW = rows > MaxVisibleRows ? inner - SystemInformation.VerticalScrollBarWidth : inner;
        for (int i = 0; i < _appsPanel.Controls.Count; i++)
            _appsPanel.Controls[i].Bounds = new Rectangle(0, i * rowH + _appsPanel.AutoScrollPosition.Y, rowW, rowH);
        y = _appsPanel.Bottom + S(12);

        _behaviorHeader.Location = new Point(left - S(2), y);
        y = _behaviorHeader.Bottom + S(6);

        int colGap = S(14);
        int colW = (inner - colGap) / 2;
        int optH = S(26), optGap = S(6);
        int rightX = left + colW + colGap;

        void Place(Control label, Control control, int x, int rowY, Control? extra = null)
        {
            label.Location = new Point(x, rowY + (optH - label.PreferredSize.Height) / 2);
            control.Location = new Point(x + colW - control.Width, rowY + (optH - control.Height) / 2);
            if (extra is not null)
                extra.Location = new Point(label.Right + S(4), rowY + (optH - extra.Height) / 2);
        }

        var b = _behavior;
        Place(b[0], b[1], left, y);
        Place(b[2], b[4], rightX, y, b[3]);
        y += optH + optGap;
        Place(b[5], b[6], left, y);
        Place(b[7], b[8], rightX, y);
        y += optH + optGap;
        Place(b[9], b[10], left, y);
        y += optH + S(14);

        _footerLine.Bounds = new Rectangle(0, y, S(ClientW), 1);
        y += 1;
        int footerH = S(46);
        _footer.Bounds = new Rectangle(0, y, S(ClientW), footerH);
        _done.Location = new Point(_footer.Width - S(Pad) - _done.Width, (footerH - _done.Height) / 2);
        LayoutFooterStatus();
        y += footerH;

        ClientSize = new Size(S(ClientW), y);
    }

    // ---- apps

    private void RebuildApps()
    {
        _appsPanel.SuspendLayout();
        foreach (Control c in _appsPanel.Controls.Cast<Control>().ToList())
        {
            _appsPanel.Controls.Remove(c);
            c.Dispose();
        }

        var apps = _host.ManagedApps;
        if (apps.Count == 0)
        {
            _appsPanel.Controls.Add(new Label
            {
                Text = "No apps yet. Click + Add, or click an app in the tray menu.",
                ForeColor = Theme.TextFaint,
                Font = Theme.Ui(8.6f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(S(2), 0, 0, 0),
            });
        }
        else
        {
            int i = 0;
            foreach (var app in apps)
            {
                var row = new AppRowControl(app.ProcessName,
                    string.IsNullOrEmpty(app.DisplayName) ? app.ProcessName : app.DisplayName,
                    app.ExePath, app.BackgroundVolume, shaded: i % 2 == 0);
                row.VolumeChanged += (r, percent) => _host.SetBackgroundVolume(r.ProcessName, percent);
                row.RemoveClicked += r =>
                {
                    var answer = MessageBox.Show(this,
                        $"Remove {r.DisplayName} from muten?\n\nIt will play at its normal volume again, even in the background.",
                        "Remove app", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes) return;
                    _host.RemoveApp(r.ProcessName);
                    RebuildApps();
                };
                _toolTip.SetToolTip(row, $"{row.DisplayName} ({app.ProcessName})");
                _appsPanel.Controls.Add(row);
                i++;
            }
        }

        _appsPanel.ResumeLayout(false);
        LayoutAll();
    }

    private void OnHostStatusChanged()
    {
        if (IsDisposed) return;

        // Apps managed or unmanaged from the tray menu meanwhile
        var shown = _appsPanel.Controls.OfType<AppRowControl>().Select(r => r.ProcessName);
        if (!shown.SequenceEqual(_host.ManagedApps.Select(a => a.ProcessName), StringComparer.OrdinalIgnoreCase))
            RebuildApps();

        RefreshFooter();
    }

    public void OpenPicker()
    {
        if (_picker is { Visible: true }) return;

        var managed = _host.ManagedApps.Select(a => a.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apps = _host.GetRunningApps().Where(a => !managed.Contains(a.ProcessName)).ToList();

        _picker?.Dispose();
        _picker = new AddAppPicker(apps);
        _picker.Picked += app => AddApp(new ManagedApp
        {
            ProcessName = app.ProcessName,
            ExePath = app.ExePath,
            DisplayName = app.DisplayName,
            BackgroundVolume = 0,
        });
        _picker.BrowseClicked += () => BeginInvoke(BrowseForExe);
        _picker.Closed += (_, _) => _addButton.BackColor = Color.White;

        _addButton.BackColor = Theme.RedSoft;
        _picker.Show(_addButton, new Point(_addButton.Width, _addButton.Height + S(2)), ToolStripDropDownDirection.BelowLeft);
    }

    private void BrowseForExe()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an app for muten",
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
            RestoreDirectory = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var path = dialog.FileName;
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return;

        AddApp(new ManagedApp
        {
            ProcessName = name,
            ExePath = path,
            DisplayName = RunningApps.FriendlyName(name, path) ?? name,
            BackgroundVolume = 0,
        });
    }

    private void AddApp(ManagedApp app)
    {
        if (_host.ManagedApps.Any(a => a.ProcessName.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)))
            return;
        _host.AddApp(app);
        RebuildApps();
    }

    // ---- behavior

    private void OnStartupChanged()
    {
        try
        {
            _host.StartWithWindows = _startup.Checked;
        }
        catch (Exception ex)
        {
            _startup.SetSilently(_host.StartWithWindows);
            MessageBox.Show(this, $"Couldn't change Start with Windows: {ex.Message}", "muten",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnHotkeyChosen(Hotkey? hotkey)
    {
        var error = _host.SetPauseHotkey(hotkey);
        if (error is null) return;

        _hotkey.Hotkey = _host.PauseHotkey;
        MessageBox.Show(this, $"{error}\n\nPick another combination for the pause key.", "muten",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ---- footer

    private void RefreshFooter()
    {
        foreach (Control c in _footer.Controls.Cast<Control>().Where(c => c != _done).ToList())
        {
            _footer.Controls.Remove(c);
            c.Dispose();
        }

        var grey = Color.FromArgb(136, 136, 136);
        _footer.Controls.Add(FooterLabel($"muten {_host.Version} ·", grey));

        var state = _host.UpdateState;
        if (state == UpdateState.Available)
        {
            var link = FooterLink($"update {_host.AvailableVersion} available", Theme.GreenText);
            link.Font = Theme.Ui(8.25f, FontStyle.Bold);
            link.LinkClicked += (_, _) => _host.InstallUpdate();
            _toolTip.SetToolTip(link, "Install the update and restart muten");
            _footer.Controls.Add(link);
        }
        else if (state is UpdateState.UpToDate or UpdateState.CheckFailed)
        {
            // Click the status to check right away instead of waiting for the daily check
            var (text, color) = UpdateStatus();
            var link = FooterLink(text, color);
            link.LinkClicked += (_, _) => _host.CheckForUpdates();
            _toolTip.SetToolTip(link, "Check for updates now");
            _footer.Controls.Add(link);
        }
        else
        {
            var (text, color) = UpdateStatus();
            _footer.Controls.Add(FooterLabel(text, color));
        }

        _footer.Controls.Add(FooterLabel("·", grey));
        var about = FooterLink("About", grey);
        about.LinkClicked += (_, _) =>
        {
            using var form = new AboutForm();
            form.ShowDialog(this);
        };
        _footer.Controls.Add(about);

        LayoutFooterStatus();
    }

    // The update state as shown in the footer and the About window
    private (string Text, Color Color) UpdateStatus()
    {
        var grey = Color.FromArgb(136, 136, 136);
        return _host.UpdateState switch
        {
            UpdateState.UpToDate => ("up to date", Theme.GreenText),
            UpdateState.Checking => ("checking for updates…", grey),
            UpdateState.Available => ($"update {_host.AvailableVersion} available", Theme.GreenText),
            UpdateState.Installing => ("installing update…", Theme.GreenText),
            UpdateState.CheckFailed => ("couldn't check for updates", grey),
            _ => ("dev build", grey),
        };
    }

    private Label FooterLabel(string text, Color color) => new()
    {
        Text = text,
        Font = _footerFont,
        ForeColor = color,
        AutoSize = true,
        Padding = Padding.Empty,
        Margin = Padding.Empty,
    };

    private LinkLabel FooterLink(string text, Color color) => new()
    {
        Text = text,
        Font = _footerFont,
        AutoSize = true,
        LinkColor = color,
        ActiveLinkColor = color,
        VisitedLinkColor = color,
        LinkBehavior = LinkBehavior.AlwaysUnderline,
        Padding = Padding.Empty,
        Margin = Padding.Empty,
    };

    private void LayoutFooterStatus()
    {
        int x = S(Pad);
        foreach (Control c in _footer.Controls)
        {
            if (c == _done) continue;
            var size = c.PreferredSize;
            c.Location = new Point(x, (_footer.Height - size.Height) / 2);
            x += size.Width + S(1);
        }
    }

    // ---- form

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape && !_hotkey.Focused)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _host.StatusChanged -= OnHostStatusChanged;
        _host.SuspendHotkey(false);
        _picker?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _icon?.Dispose();
            _headerFont.Dispose();
            _labelFont.Dispose();
            _footerFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
