using System.Text;
using SecureConfig;
using SecureConfig.Editor.Core;

namespace SecureConfig.Editor.UI;

/// <summary>Master key management (on key change, re-encrypts every config variable on this server with the new key)</summary>
internal sealed partial class MainForm
{
    // ================================================================ Key

    /// <summary>Manage key: save a new key. If the key changes, decrypts every config variable on this server with the old key and re-encrypts it with the new one (all verified before writing)</summary>
    private void ManageKey()
    {
        _grid.EndEdit();
        FlushGrid();
        string path = MasterKey.ResolveKeyFilePath();
        bool envOverride = MasterKey.ActiveKeyEnvName != null;

        using var f = new KeyForm(_key != null ? Convert.ToBase64String(_key) : null, _keySource, path, envOverride);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        byte[] newKey = SecretFormat.ParseKey(f.KeyToSave);
        bool same = _key != null && newKey.SequenceEqual(_key);

        // Key change: decrypt every encrypted config variable on this server with the old key and re-encrypt with the new one (verification done up front)
        var reencrypted = new List<(string Name, string Enc)>();
        var failed = new List<string>();
        if (!same && _key != null)
        {
            foreach (string name in ConfigStore.ListEncryptedVariables())
            {
                try
                {
                    string plain = SecretFormat.Decrypt(ConfigStore.ReadRaw(name), _key);
                    reencrypted.Add((name, SecretWriter.EncryptVerified(plain, newKey)));
                }
                catch (Exception)
                {
                    failed.Add(name);
                }
            }
        }

        var msg = new StringBuilder();
        msg.AppendLine("The key file will be saved to: " + path);
        if (reencrypted.Count > 0)
        {
            msg.AppendLine();
            msg.AppendLine("Because the key is changing, the following config variables will be re-encrypted with the new key and saved (previous values are backed up):");
            foreach (var r in reencrypted) msg.AppendLine("  · " + r.Name);
            msg.AppendLine("Unsaved changes in the editor are kept as they are.");
        }
        if (failed.Count > 0)
        {
            msg.AppendLine();
            msg.AppendLine("Note: these variables cannot be decrypted with the old key and are left unchanged (they will not be readable with the new key):");
            foreach (var n in failed) msg.AppendLine("  · " + n);
        }
        msg.AppendLine();
        msg.Append("Continue?");
        if (MessageBox.Show(this, msg.ToString(), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        try
        {
            KeyFileWriter.Write(path, f.KeyToSave, f.GrantCurrentUser);
            foreach (var r in reencrypted)
            {
                ConfigStore.BackupCurrent(r.Name);
                ConfigStore.WriteRaw(r.Name, r.Enc);
            }
            SetMessage(reencrypted.Count > 0
                ? $"Key changed and {reencrypted.Count} config variable(s) re-encrypted. (Backups open only with the previous key.)"
                : "Key file saved: " + path);
        }
        catch (Exception ex)
        {
            ShowError("Could not save the key.", ex);
            return;
        }

        if (envOverride)
        {
            MessageBox.Show(this, "The environment variable " + MasterKey.ActiveKeyEnvName + " takes precedence over the key file, so this editor keeps using that key.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        if (_rawExists && _loadError != null)
        {
            // Try reading the previously unreadable settings with the new key
            if (!IsDirty() || MessageBox.Show(this, "Reload the stored settings with the new key? Your current edits will be discarded.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Reload(false);
                return;
            }
        }

        LoadKey();
        UpdateUi();
    }
}
