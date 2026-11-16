using SecureConfig;
using SecureConfig.Editor.Core;

namespace SecureConfig.Editor.UI;

internal static class Ui
{
    public static readonly Font BaseFont = new("Segoe UI", 9F);
    public static readonly Font MonoFont = new("Consolas", 10F);

    public static void Prepare(Form f, string title, Size size)
    {
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.Text = title;
        f.Font = BaseFont;
        f.ClientSize = size;
    }

    public static void PrepareDialog(Form f, string title, Size size)
    {
        Prepare(f, title, size);
        f.StartPosition = FormStartPosition.CenterParent;
        f.ShowInTaskbar = false;
        f.MinimizeBox = false;
        f.MaximizeBox = false;
    }

    public static Button Button(string text, int width = 100) => new() { Text = text, Width = width, Height = 30 };
}

/// <summary>Single-line input</summary>
internal sealed class PromptForm : Form
{
    private readonly TextBox _text;
    public string Value => _text.Text.Trim();

    public PromptForm(string title, string label, string initial = "")
    {
        Ui.PrepareDialog(this, title, new Size(460, 130));
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var lbl = new Label { Text = label, Location = new Point(12, 12), AutoSize = true };
        _text = new TextBox { Text = initial, Location = new Point(12, 36), Width = 436 };
        var ok = Ui.Button("OK", 90);
        var cancel = Ui.Button("Cancel", 90);
        ok.Location = new Point(262, 84);
        cancel.Location = new Point(358, 84);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { lbl, _text, ok, cancel });
    }
}

/// <summary>Master key management</summary>
internal sealed class KeyForm : Form
{
    private readonly TextBox _key;
    private readonly CheckBox _show;
    private readonly CheckBox _grantUser;

    public string KeyToSave { get; private set; }
    public bool GrantCurrentUser => _grantUser.Checked;

    public KeyForm(string currentKeyBase64, string currentSource, string keyFilePath, bool envOverride)
    {
        Ui.PrepareDialog(this, "Manage Master Key", new Size(680, 400));
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var info = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(656, 110),
            Text =
                "Current key: " + (currentSource ?? "none") + "\r\n" +
                "Key file: " + keyFilePath + "\r\n" +
                "Permissions: Administrators/SYSTEM full control, IIS_IUSRS read-only (inheritance disabled)\r\n\r\n" +
                "Changing the key re-encrypts the settings stored on this server with the new key. Running apps pick up the new key on their next request."
        };

        var lblKey = new Label { Text = "Key (Base64, 32 bytes)", Location = new Point(12, 130), AutoSize = true };
        _key = new TextBox { Location = new Point(12, 152), Width = 560, Font = Ui.MonoFont, UseSystemPasswordChar = true };
        _show = new CheckBox { Text = "Show", Location = new Point(582, 154), AutoSize = true };
        _show.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !_show.Checked;

        var gen = Ui.Button("Generate new key", 130);
        gen.Location = new Point(12, 190);
        gen.Click += (_, _) => { _key.Text = SecretWriter.GenerateKeyBase64(); _show.Checked = true; };

        var showCurrent = Ui.Button("Show current key", 130);
        showCurrent.Location = new Point(150, 190);
        showCurrent.Enabled = currentKeyBase64 != null;
        showCurrent.Click += (_, _) => { _key.Text = currentKeyBase64; _show.Checked = true; };

        var copy = Ui.Button("Copy", 80);
        copy.Location = new Point(288, 190);
        copy.Click += (_, _) => { if (_key.Text.Length > 0) Clipboard.SetText(_key.Text); };

        _grantUser = new CheckBox
        {
            Text = "Also grant read access to the current Windows user (dev PC: Visual Studio / IIS Express)",
            Location = new Point(12, 232),
            AutoSize = true
        };

        var warn = new Label
        {
            Location = new Point(12, 264),
            Size = new Size(656, 48),
            ForeColor = Color.FromArgb(190, 70, 0),
            Text = envOverride
                ? "Note: this process has the environment variable " + MasterKey.ActiveKeyEnvName + ", which takes precedence over the key file. Remove this environment variable on servers."
                : "Also keep the key somewhere safe outside the server (e.g. a password manager). If you have a plaintext JSON backup, you can recover with a new key even if the key is lost."
        };

        var save = Ui.Button("Save to key file", 130);
        var close = Ui.Button("Close", 90);
        save.Location = new Point(442, 356);
        close.Location = new Point(578, 356);
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
        save.Click += (_, _) =>
        {
            try
            {
                SecretFormat.ParseKey(_key.Text);
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            KeyToSave = _key.Text.Trim();
            DialogResult = DialogResult.OK;
        };

        Controls.AddRange(new Control[] { info, lblKey, _key, _show, gen, showCurrent, copy, _grantUser, warn, save, close });
    }
}

/// <summary>Import preview (merge/replace)</summary>
internal sealed class ImportForm : Form
{
    private readonly ConfigDocument _current;
    private readonly ConfigDocument _file;
    private readonly RadioButton _merge;
    private readonly RadioButton _replace;
    private readonly TextBox _preview;
    private readonly Button _ok;

    public ConfigDocument Result { get; private set; }

    public ImportForm(string path, ConfigDocument current, ConfigDocument file)
    {
        _current = current;
        _file = file;
        Ui.PrepareDialog(this, "Import Preview", new Size(760, 520));
        MinimumSize = new Size(620, 380);
        MaximizeBox = true;

        var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(8) };
        var lblFile = new Label { Text = "File: " + path, Dock = DockStyle.Top, Height = 22, AutoEllipsis = true };
        _merge = new RadioButton { Text = "Merge (add/overwrite; keep anything not in the file)", Checked = true, AutoSize = true, Location = new Point(8, 34) };
        _replace = new RadioButton { Text = "Replace (everything with the file contents)", AutoSize = true, Location = new Point(400, 34) };
        _merge.CheckedChanged += (_, _) => Rebuild();
        top.Controls.Add(_merge);
        top.Controls.Add(_replace);
        top.Controls.Add(lblFile);

        _preview = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Font = Ui.MonoFont, BackColor = SystemColors.Window
        };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var cancel = Ui.Button("Cancel", 90);
        cancel.DialogResult = DialogResult.Cancel;
        _ok = Ui.Button("Apply to editor", 140);
        _ok.DialogResult = DialogResult.OK;
        var note = new Label { Text = "After applying, click [Save] to write it to the server.", AutoSize = true, Margin = new Padding(0, 9, 12, 0), ForeColor = SystemColors.GrayText };
        bottom.Controls.AddRange(new Control[] { cancel, _ok, note });
        CancelButton = cancel;

        Controls.Add(_preview);
        Controls.Add(bottom);
        Controls.Add(top);
        _preview.BringToFront();
        Rebuild();
    }

    private void Rebuild()
    {
        Result = ImportPlanner.Apply(_current, _file, _replace.Checked);
        string text = ImportPlanner.Describe(_current, Result, out int changes);
        _preview.Text = changes == 0 ? "No changes.\r\n\r\n" + text : text;
        _ok.Enabled = changes > 0;
    }
}
