using System.Text;
using SecureConfig;
using SecureConfig.Editor.Core;

namespace SecureConfig.Editor.UI;

/// <summary>Config variable selection, loading, environment list, variable grid</summary>
internal sealed partial class MainForm
{
    // ================================================================ Config variable

    /// <summary>Re-reads this server's encrypted config variables into the combo box. extra: a new variable not saved yet</summary>
    private void RefreshVariableList(string select, string extra = null)
    {
        var names = ConfigStore.ListEncryptedVariables();
        if (extra != null && !names.Contains(extra, StringComparer.OrdinalIgnoreCase)) names.Add(extra);
        if (_varName != null && !names.Contains(_varName, StringComparer.OrdinalIgnoreCase) && select == null) select = names.FirstOrDefault();

        _suppress = true;
        _varCombo.Items.Clear();
        foreach (var n in names) _varCombo.Items.Add(n);
        select ??= _varName ?? names.FirstOrDefault();
        int idx = select == null ? -1 : names.FindIndex(n => string.Equals(n, select, StringComparison.OrdinalIgnoreCase));
        _varCombo.SelectedIndex = idx;
        _suppress = false;

        _varName = idx >= 0 ? names[idx] : null;
        Reload(false);
    }

    /// <summary>When another config variable is picked in the combo box, checks for unsaved changes and then loads it</summary>
    private void OnVariableSelected()
    {
        if (_suppress) return;
        string selected = _varCombo.SelectedItem as string;
        if (string.Equals(selected, _varName, StringComparison.Ordinal)) return;
        if (!ConfirmDiscard("switch to another config variable"))
        {
            _suppress = true;
            _varCombo.SelectedItem = _varName;
            _suppress = false;
            return;
        }
        _varName = selected;
        Reload(false);
    }

    /// <summary>Asks for a new config variable name (with validation). It is actually created on the first [Save]</summary>
    private void NewVariable()
    {
        if (!ConfirmDiscard("create a new config variable")) return;
        string initial = "";
        while (true)
        {
            using var f = new PromptForm("New Config Variable", "System environment variable name (one per app, e.g. MYAPP_CONFIG)", initial);
            if (f.ShowDialog(this) != DialogResult.OK) return;
            string name = f.Value.ToUpperInvariant();
            string err = null;
            try { SecureConfigReader.ValidateVariableName(name); }
            catch (ArgumentException ex) { err = ex.Message; }
            if (err == null && MasterKey.IsReservedName(name)) err = "Names reserved for the master key cannot be used.";
            if (err == null)
            {
                string existing = ConfigStore.ReadRaw(name);
                if (!string.IsNullOrEmpty(existing) && !SecretFormat.IsEncrypted(existing))
                    err = "'" + name + "' is already used as a regular system environment variable. Choose another name.";
            }
            if (err == null)
            {
                _varName = name;
                RefreshVariableList(name, name);
                if (!_rawExists) SetMessage("New config variable '" + name + "' — add an environment or import JSON, then click [Save].");
                return;
            }
            MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            initial = name;
        }
    }

    // ================================================================ Loading

    /// <summary>Looks up the master key again. On failure _key = null and the reason is in _keyError</summary>
    private void LoadKey()
    {
        _key = null;
        _keySource = null;
        _keyError = null;
        try
        {
            _key = MasterKey.TryLoad(out _keySource);
            if (_key == null) _keyError = "no master key";
        }
        catch (Exception ex)
        {
            _keyError = ex.Message;
        }
    }

    /// <summary>Reads, decrypts and parses the selected config variable. confirm: ask first if there are unsaved changes</summary>
    private void Reload(bool confirm)
    {
        if (confirm && !ConfirmDiscard("reload")) return;

        LoadKey();
        _loadError = null;
        string raw = null;
        if (_varName != null)
        {
            try { raw = ConfigStore.ReadRaw(_varName); }
            catch (Exception ex) { _loadError = ex.Message; }
        }

        _rawExists = !string.IsNullOrWhiteSpace(raw);
        var doc = new ConfigDocument();
        if (_rawExists)
        {
            if (_key == null)
            {
                _loadError = "The stored settings cannot be decrypted because there is no master key. (" + _keyError + ")";
            }
            else
            {
                try { doc = ConfigDocument.Parse(SecretFormat.Decrypt(raw, _key)); }
                catch (Exception ex) { _loadError = ex.Message; }
            }
        }

        SetDocument(doc, true, _currentEnv?.Name);

        if (_varName == null)
        {
            SetMessage("There are no config variables on this server. Create one with [New config variable].");
        }
        else if (_loadError != null)
        {
            SetMessage("Could not read the stored settings.");
            MessageBox.Show(this,
                "Could not read the stored settings (" + _varName + ").\n\n" + _loadError +
                "\n\n· If you have the correct key, set it in [Manage key].\n· If you have the plaintext JSON, you can [Import] it and [Save] to recreate the settings (the existing value is backed up to the backup folder).",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            SetMessage(_rawExists ? "Loaded '" + _varName + "'." : "'" + _varName + "' has not been saved yet.");
        }
    }

    /// <summary>Replaces the document being edited. markSaved: treat this content as the 'saved state' (baseline for change detection)</summary>
    private void SetDocument(ConfigDocument doc, bool markSaved, string selectEnv = null)
    {
        _doc = doc;
        if (markSaved) _savedJson = ConfigJson.Write(doc, false);
        _currentEnv = null;
        RefreshEnvList(selectEnv);
    }

    /// <summary>Redraws the environment list on the left and selects selectName (or the first environment)</summary>
    private void RefreshEnvList(string selectName)
    {
        _suppress = true;
        _envList.BeginUpdate();
        _envList.Items.Clear();
        foreach (var e in _doc.Environments) _envList.Items.Add(e.Name);
        _envList.EndUpdate();

        int idx = selectName == null ? -1
            : _doc.Environments.FindIndex(e => string.Equals(e.Name, selectName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 && _doc.Environments.Count > 0) idx = 0;
        _envList.SelectedIndex = idx;
        _suppress = false;

        ShowEnvironment(idx >= 0 ? _doc.Environments[idx] : null);
    }

    // ================================================================ Environments

    /// <summary>When the environment selection changes, commits the grid to the current environment and shows the new one</summary>
    private void OnEnvSelected()
    {
        if (_suppress) return;
        _grid.EndEdit();
        FlushGrid();
        int idx = _envList.SelectedIndex;
        ShowEnvironment(idx >= 0 ? _doc.Environments[idx] : null);
    }

    /// <summary>Fills the right-hand grid with the environment's variables</summary>
    private void ShowEnvironment(ConfigEnvironment env)
    {
        _currentEnv = env;
        _suppress = true;
        _grid.Rows.Clear();
        if (env != null)
            foreach (var v in env.Variables) _grid.Rows.Add(v.Name, v.Value);
        _suppress = false;
        ApplyMaskState();
    }

    /// <summary>Environment name dialog. Validates rules and duplicates; returns null if cancelled. except: the environment itself when renaming</summary>
    private string AskEnvironmentName(string title, string initial, ConfigEnvironment except)
    {
        while (true)
        {
            using var f = new PromptForm(title, "Environment name (e.g. dev, stg, prod)", initial);
            if (f.ShowDialog(this) != DialogResult.OK) return null;
            string name = f.Value;
            string err = ConfigDocument.ValidateEnvironmentName(name);
            if (err == null)
            {
                var exist = _doc.FindEnvironment(name);
                if (exist != null && exist != except) err = "Environment '" + name + "' already exists.";
            }
            if (err == null) return name;
            MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            initial = name;
        }
    }

    /// <summary>Adds an empty environment</summary>
    private void AddEnvironment()
    {
        if (_varName == null) { NewVariable(); if (_varName == null) return; }
        _grid.EndEdit();
        FlushGrid();
        string name = AskEnvironmentName("Add Environment", "", null);
        if (name == null) return;
        _doc.Environments.Add(new ConfigEnvironment(name));
        RefreshEnvList(name);
        SetMessage("Added environment '" + name + "'. Save to apply it to the server.");
    }

    /// <summary>Renames the selected environment</summary>
    private void RenameEnvironment()
    {
        if (_currentEnv == null) return;
        _grid.EndEdit();
        FlushGrid();
        var env = _currentEnv;
        string name = AskEnvironmentName("Rename Environment", env.Name, env);
        if (name == null || name == env.Name) return;
        env.Name = name;
        RefreshEnvList(name);
    }

    /// <summary>Duplicates the selected environment under a new name (e.g. create prod from stg, then edit only the values)</summary>
    private void CopyEnvironment()
    {
        if (_currentEnv == null) return;
        _grid.EndEdit();
        FlushGrid();
        string name = AskEnvironmentName("Duplicate '" + _currentEnv.Name + "'", "", null);
        if (name == null) return;
        var copy = _currentEnv.Clone();
        copy.Name = name;
        _doc.Environments.Add(copy);
        RefreshEnvList(name);
        SetMessage("Created environment '" + name + "'. Adjust its values for that environment, then save.");
    }

    /// <summary>Deletes the selected environment (applied to the server on save)</summary>
    private void DeleteEnvironment()
    {
        if (_currentEnv == null) return;
        var env = _currentEnv;
        if (MessageBox.Show(this, $"Delete environment '{env.Name}' and its {env.Variables.Count} variables?\nSave to apply this to the server.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _doc.Environments.Remove(env);
        _currentEnv = null;
        RefreshEnvList(null);
    }

    // ================================================================ Variable grid

    /// <summary>Copies the current grid contents into _currentEnv.Variables. Empty rows are dropped</summary>
    private void FlushGrid()
    {
        if (_currentEnv == null) return;
        _currentEnv.Variables.Clear();
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (r.IsNewRow) continue;
            string n = (Convert.ToString(r.Cells[0].Value) ?? "").Trim();
            string v = Convert.ToString(r.Cells[1].Value) ?? "";
            if (n.Length == 0 && v.Length == 0) continue;
            _currentEnv.Variables.Add(new ConfigVariable(n, v));
        }
    }

    /// <summary>On cell edit / row delete, updates the document and refreshes the UI state</summary>
    private void OnGridChanged()
    {
        if (_suppress) return;
        FlushGrid();
        UpdateUi();
    }

    /// <summary>When "Show / edit values" is off, masks the value column with ●●● (against shoulder surfing / screen sharing)</summary>
    private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_showValues || e.ColumnIndex != 1) return;
        if (e.Value is string s && s.Length > 0)
        {
            e.Value = Mask;
            e.FormattingApplied = true;
        }
    }

    /// <summary>Sets whether values can be edited and new rows added, depending on "Show / edit values"</summary>
    private void ApplyMaskState()
    {
        bool editable = _currentEnv != null;
        _grid.Enabled = editable;
        _grid.Columns[1].ReadOnly = !_showValues;
        _grid.AllowUserToAddRows = editable && _showValues;
        _grid.Invalidate();
        UpdateUi();
    }

    /// <summary>Deletes the variables of the selected cells/rows</summary>
    private void DeleteSelectedRows()
    {
        var rows = new HashSet<DataGridViewRow>();
        foreach (DataGridViewCell c in _grid.SelectedCells) rows.Add(c.OwningRow);
        foreach (DataGridViewRow r in _grid.SelectedRows) rows.Add(r);
        foreach (var r in rows.Where(r => !r.IsNewRow)) _grid.Rows.Remove(r);
        OnGridChanged();
    }
}
