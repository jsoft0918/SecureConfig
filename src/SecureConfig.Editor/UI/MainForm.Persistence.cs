using System.Text;
using SecureConfig;
using SecureConfig.Editor.Core;

namespace SecureConfig.Editor.UI;

/// <summary>Save, import/export, restore from backup</summary>
internal sealed partial class MainForm
{
    // ================================================================ Save

    /// <summary>Validate → encrypt (+ decryption check) → size check → confirmation → back up previous value → write the system environment variable. Returns true on success</summary>
    private bool Save()
    {
        _grid.EndEdit();
        FlushGrid();

        if (_varName == null)
        {
            MessageBox.Show(this, "First use [New config variable] to choose the system environment variable name to save to.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        string err = _doc.Validate();
        if (err != null)
        {
            MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (_key == null)
        {
            MessageBox.Show(this, "There is no master key. Create or set one in [Manage key], then save.\n\n(" + _keyError + ")",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        string json = ConfigJson.Write(_doc, false);
        if (json == _savedJson && _loadError == null && _rawExists)
        {
            SetMessage("No changes.");
            return true;
        }

        string enc;
        try { enc = SecretWriter.EncryptVerified(json, _key); }
        catch (Exception ex) { ShowError("Encryption failed.", ex); return false; }
        if (enc.Length > ConfigStore.MaxLength)
        {
            MessageBox.Show(this, $"The settings are too large: {enc.Length:N0} characters after encryption (max {ConfigStore.MaxLength:N0}).",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (_rawExists && _loadError != null &&
            MessageBox.Show(this, "The existing stored settings could not be read.\nSaving will overwrite them (the existing value is backed up to the backup folder).\n\nContinue?",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return false;

        int varCount = _doc.Environments.Sum(e => e.Variables.Count);
        if (MessageBox.Show(this,
                $"Save to system environment variable {_varName}.\n{_doc.Environments.Count} environments, {varCount} variables\n\nRunning apps pick up the change on their next request.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return false;

        try
        {
            string backup = ConfigStore.BackupCurrent(_varName);
            ConfigStore.WriteRaw(_varName, enc);
            _savedJson = json;
            _rawExists = true;
            _loadError = null;
            SetMessage("Saved" + (backup != null ? "  (previous value backed up to: " + backup + ")" : ""));
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Save failed.", ex);
            return false;
        }
    }

    // ================================================================ Import / export / restore

    /// <summary>Reads a JSON file, previews merge/replace and applies it to the editor (written to the server on [Save])</summary>
    private void Import()
    {
        if (_varName == null) { NewVariable(); if (_varName == null) return; }
        _grid.EndEdit();
        FlushGrid();
        using var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        ConfigDocument file;
        try { file = ConfigDocument.Parse(File.ReadAllText(dlg.FileName, Encoding.UTF8)); }
        catch (Exception ex) { ShowError("Could not read the JSON file.", ex); return; }

        using var f = new ImportForm(dlg.FileName, _doc, file);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        SetDocument(f.Result, false, _currentEnv?.Name);
        SetMessage("Imported content applied to the editor. Click [Save] to write it to the server.");
    }

    /// <summary>Exports the content being edited as plaintext JSON. selectedOnly: only the selected environment</summary>
    private void Export(bool selectedOnly)
    {
        _grid.EndEdit();
        FlushGrid();
        if (selectedOnly && _currentEnv == null) return;
        if (IsDirty() && MessageBox.Show(this, "You have unsaved changes. Export the content as currently edited?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var doc = new ConfigDocument();
        if (selectedOnly) doc.Environments.Add(_currentEnv.Clone());
        else foreach (var e in _doc.Environments) doc.Environments.Add(e.Clone());

        string baseName = (_varName ?? "config").ToLowerInvariant();
        string name = (selectedOnly ? baseName + "_" + _currentEnv.Name : baseName) + "_" + DateTime.Now.ToString("yyyyMMdd") + ".json";
        using var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*", FileName = name };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, ConfigJson.Write(doc, true), new UTF8Encoding(false));
            SetMessage("Exported (plaintext): " + dlg.FileName + "  Note: it contains secrets, keep it somewhere safe.");
        }
        catch (Exception ex) { ShowError("Export failed.", ex); }
    }

    /// <summary>Opens an encrypted backup from the backup folder with the current key and applies it to the editor (written to the server on [Save])</summary>
    private void RestoreBackup()
    {
        if (_varName == null) { NewVariable(); if (_varName == null) return; }
        _grid.EndEdit();
        FlushGrid();
        if (_key == null)
        {
            MessageBox.Show(this, "The (encrypted) backup cannot be opened because there is no master key.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dlg = new OpenFileDialog
        {
            Filter = "Backup (*.txt)|*.txt|All files (*.*)|*.*",
            InitialDirectory = ConfigStore.BackupDirectory
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        ConfigDocument doc;
        try { doc = ConfigDocument.Parse(SecretFormat.Decrypt(File.ReadAllText(dlg.FileName), _key)); }
        catch (Exception ex) { ShowError("Could not open the backup (it requires the key that was in use when the backup was made).", ex); return; }

        string diff = ImportPlanner.Describe(_doc, doc, out int changes);
        if (changes == 0) { SetMessage("The backup is identical to the current editor content."); return; }
        if (MessageBox.Show(this, "Replace the editor content with the backup. Click [Save] to apply it to '" + _varName + "'.\n\n" + Truncate(diff, 1500),
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        SetDocument(doc, false, _currentEnv?.Name);
        SetMessage("Backup content applied to the editor. Click [Save] to write it to the server.");
    }
}
