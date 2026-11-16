using System.Text;
using SecureConfig;
using SecureConfig.Editor.Core;

namespace SecureConfig.Editor.UI;

/// <summary>
/// Editor main window.
///   Top: config variable selection (one per app, e.g. MYAPP_CONFIG)
///   Left: environments (dev/stg/prod ...)  Right: variables of the selected environment
///   [Save] serializes everything to JSON → AES-256-GCM encryption → the selected system environment variable
///
/// Partial files
///   MainForm.cs             layout, status display, shared helpers
///   MainForm.Document.cs    config variable selection, loading, environments, variable grid
///   MainForm.Persistence.cs save, import/export, restore from backup
///   MainForm.MasterKey.cs   master key management (re-encryption on key change)
/// </summary>
internal sealed partial class MainForm : Form
{
    private const string AppTitle = "SecureConfig Editor";
    private const string Mask = "●●●●●●●●";

    // State
    private string _varName;            // config variable being edited (null = none selected)
    private ConfigDocument _doc = new();  // settings being edited (plaintext, in memory only)
    private string _savedJson;          // content stored on the server (compact JSON), for change detection
    private byte[] _key;                // current master key (null if none)
    private string _keySource;          // where the key was found (for the status bar)
    private string _keyError;           // why the key could not be found
    private bool _rawExists;            // whether this config variable is already stored on the server
    private string _loadError;          // why the stored value could not be read (decrypted)
    private ConfigEnvironment _currentEnv; // environment shown in the right-hand grid
    private bool _suppress;             // controls are being changed from code (prevents event re-entry)
    private bool _showValues;           // whether "Show / edit values" is checked

    // Controls
    private readonly ToolStripComboBox _varCombo;
    private readonly ListBox _envList;
    private readonly DataGridView _grid;
    private readonly Label _envTitle;
    private readonly CheckBox _chkShow;
    private readonly Button _btnAddEnv, _btnRename, _btnCopy, _btnDeleteEnv, _btnDeleteRows;
    private readonly ToolStripButton _tbSave;
    private readonly ToolStripStatusLabel _stConfig, _stKey, _stMessage;

    public MainForm()
    {
        Ui.Prepare(this, AppTitle, new Size(1080, 660));
        MinimumSize = new Size(800, 460);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        // ---- Toolbar 1: config variable
        var tsVar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };
        tsVar.Items.Add(new ToolStripLabel("Config variable (system environment variable):") { Font = new Font(Ui.BaseFont, FontStyle.Bold) });
        _varCombo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Font = Ui.MonoFont };
        _varCombo.SelectedIndexChanged += (_, _) => OnVariableSelected();
        tsVar.Items.Add(_varCombo);
        tsVar.Items.Add(new ToolStripButton("New config variable...", null, (_, _) => NewVariable()));
        tsVar.Items.Add(new ToolStripSeparator());
        tsVar.Items.Add(new ToolStripButton("Manage key...", null, (_, _) => ManageKey()));

        // ---- Toolbar 2: actions
        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };
        ts.Items.Add(new ToolStripButton("Reload (F5)", null, (_, _) => Reload(true)));
        _tbSave = new ToolStripButton("Save (Ctrl+S)", null, (_, _) => Save()) { Font = new Font(Ui.BaseFont, FontStyle.Bold) };
        ts.Items.Add(_tbSave);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Import (JSON)...", null, (_, _) => Import()));
        var export = new ToolStripDropDownButton("Export (JSON)");
        export.DropDownItems.Add("All environments...", null, (_, _) => Export(false));
        export.DropDownItems.Add("Selected environment only...", null, (_, _) => Export(true));
        ts.Items.Add(export);
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(new ToolStripButton("Restore from backup...", null, (_, _) => RestoreBackup()));

        // ---- Status bar
        var ss = new StatusStrip();
        _stConfig = new ToolStripStatusLabel { BorderSides = ToolStripStatusLabelBorderSides.Right };
        _stKey = new ToolStripStatusLabel { BorderSides = ToolStripStatusLabelBorderSides.Right };
        _stMessage = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        ss.Items.AddRange(new ToolStripItem[] { _stConfig, _stKey, _stMessage });

        // ---- Left: environments
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        var lblEnv = new Label
        {
            Text = "Environments", Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0), Font = new Font(Ui.BaseFont, FontStyle.Bold)
        };
        _envList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 11F) };
        _envList.SelectedIndexChanged += (_, _) => OnEnvSelected();
        var envButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(2) };
        _btnAddEnv = Ui.Button("Add", 72);
        _btnRename = Ui.Button("Rename", 82);
        _btnCopy = Ui.Button("Duplicate", 72);
        _btnDeleteEnv = Ui.Button("Delete", 72);
        _btnAddEnv.Click += (_, _) => AddEnvironment();
        _btnRename.Click += (_, _) => RenameEnvironment();
        _btnCopy.Click += (_, _) => CopyEnvironment();
        _btnDeleteEnv.Click += (_, _) => DeleteEnvironment();
        envButtons.Controls.AddRange(new Control[] { _btnAddEnv, _btnRename, _btnCopy, _btnDeleteEnv });
        split.Panel1.Controls.Add(_envList);
        split.Panel1.Controls.Add(envButtons);
        split.Panel1.Controls.Add(lblEnv);
        _envList.BringToFront();

        // ---- Right: variables
        _envTitle = new Label
        {
            Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0), Font = new Font("Segoe UI", 11F, FontStyle.Bold)
        };
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToResizeRows = false,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            RowHeadersWidth = 28,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
        };
        _grid.DefaultCellStyle.Font = Ui.MonoFont;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", FillWeight = 35, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 65, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.CellFormatting += OnCellFormatting;
        _grid.CellValueChanged += (_, _) => OnGridChanged();
        _grid.RowsRemoved += (_, _) => OnGridChanged();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(4) };
        _chkShow = new CheckBox { Text = "Show / edit values", AutoSize = true, Margin = new Padding(4, 9, 12, 0) };
        _chkShow.CheckedChanged += (_, _) => { _showValues = _chkShow.Checked; ApplyMaskState(); };
        _btnDeleteRows = Ui.Button("Delete selected rows", 140);
        _btnDeleteRows.Click += (_, _) => DeleteSelectedRows();
        var hint = new Label
        {
            Text = "Turn on \"Show / edit values\" to edit. Type in the empty last row to add a variable.",
            AutoSize = true, Margin = new Padding(12, 9, 0, 0), ForeColor = SystemColors.GrayText
        };
        bottom.Controls.AddRange(new Control[] { _chkShow, _btnDeleteRows, hint });

        split.Panel2.Controls.Add(_grid);
        split.Panel2.Controls.Add(bottom);
        split.Panel2.Controls.Add(_envTitle);
        _grid.BringToFront();

        Controls.Add(split);
        Controls.Add(ts);
        Controls.Add(tsVar);
        Controls.Add(ss);
        split.BringToFront();

        Load += (_, _) =>
        {
            split.SplitterDistance = 230;
            RefreshVariableList(null);
        };
        FormClosing += (_, e) => { if (!ConfirmDiscard("exit")) e.Cancel = true; };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
        if (keyData == Keys.F5) { Reload(true); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ================================================================ Status display

    /// <summary>Whether the edited content differs from the saved content</summary>
    private bool IsDirty() => ConfigJson.Write(_doc, false) != _savedJson;

    /// <summary>Syncs button states, the title bar change marker and the status bar (config/key) with the current state</summary>
    private void UpdateUi()
    {
        bool dirty = IsDirty();
        bool hasEnv = _currentEnv != null;
        _btnRename.Enabled = _btnCopy.Enabled = _btnDeleteEnv.Enabled = _btnDeleteRows.Enabled = hasEnv;

        _envTitle.Text = hasEnv ? $"{_currentEnv.Name}   ({_currentEnv.Variables.Count} variables)" : "(Add or select an environment)";
        Text = AppTitle + (_varName != null ? " - " + _varName : "") + (dirty ? "  ●  Unsaved changes" : "");
        _tbSave.Enabled = _varName != null && (dirty || (_rawExists && _loadError != null && _doc.Environments.Count > 0));

        int varCount = _doc.Environments.Sum(e => e.Variables.Count);
        _stConfig.Text = _varName == null ? "Config variable: none"
            : !_rawExists ? _varName + ": not saved yet"
            : _loadError != null ? _varName + ": failed to read"
            : $"{_varName}: {_doc.Environments.Count} environments · {varCount} variables";
        _stConfig.ForeColor = _loadError != null ? Color.Firebrick : SystemColors.ControlText;
        _stKey.Text = _key != null ? "Key: " + _keySource : "Key: " + (_keyError ?? "none");
        _stKey.ForeColor = _key != null ? SystemColors.ControlText : Color.Firebrick;
    }

    /// <summary>Shows a status bar message</summary>
    private void SetMessage(string s)
    {
        _stMessage.Text = s;
        UpdateUi();
    }

    /// <summary>If there are unsaved changes, asks Save / Discard / Cancel. Returns true if it is OK to proceed</summary>
    private bool ConfirmDiscard(string action)
    {
        _grid.EndEdit();
        FlushGrid();
        if (!IsDirty()) return true;
        var r = MessageBox.Show(this, "You have unsaved changes. Save them?\n\n(No: discard the changes and " + action + ")",
            Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return Save();
        return true;
    }

    // ================================================================ Shared

    /// <summary>Shows the root cause of an exception. Adds a run-as-administrator hint for permission errors</summary>
    private void ShowError(string title, Exception ex)
    {
        Exception root = ex;
        while (root.InnerException != null && root is not InvalidOperationException) root = root.InnerException;
        string hint = root is UnauthorizedAccessException or System.Security.SecurityException
            ? "\n\nMake sure the editor is running as administrator." : "";
        MessageBox.Show(this, title + "\n\n" + root.Message + hint, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        SetMessage(title + " " + root.Message);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "\r\n...";
}
