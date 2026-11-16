using System.Text;

namespace SecureConfig.Editor.Core;

/// <summary>Reads/writes and backs up config variables (system environment variables, e.g. MYAPP_CONFIG). Writing requires administrator rights.</summary>
public static class ConfigStore
{
    /// <summary>Maximum length of a Windows environment variable value (less than 32,767 characters)</summary>
    public const int MaxLength = 32766;

    /// <summary>Raw value (ciphertext) of the system (Machine) environment variable, or null if it does not exist.</summary>
    public static string ReadRaw(string variableName) =>
        Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine);

    /// <summary>
    /// Writes the system environment variable and reads it back to verify (requires administrator rights).
    /// Windows broadcasts WM_SETTINGCHANGE on write, but running apps read the registry directly, so they pick up the change regardless.
    /// </summary>
    public static void WriteRaw(string variableName, string value)
    {
        SecureConfigReader.ValidateVariableName(variableName);
        if (value != null && value.Length > MaxLength)
            throw new InvalidOperationException($"Value is too long: {value.Length:N0} characters (max {MaxLength:N0})");
        Environment.SetEnvironmentVariable(variableName, value, EnvironmentVariableTarget.Machine);
        if (!string.Equals(ReadRaw(variableName), value, StringComparison.Ordinal))
            throw new InvalidOperationException("The value read back after saving does not match. Make sure the editor is running as administrator.");
    }

    /// <summary>Encrypted config variables on this server (system environment variables whose value starts with "enc:v1:")</summary>
    public static List<string> ListEncryptedVariables()
    {
        var list = new List<string>();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine))
        {
            if (e.Value is string v && SecretFormat.IsEncrypted(v)) list.Add((string)e.Key);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Backup folder: backup\ next to the editor exe; if it is not writable (e.g. Program Files), %TEMP%\SecureConfigEditor_backup.
    /// </summary>
    public static string BackupDirectory
    {
        get
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "backup");
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write_test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return dir;
            }
            catch
            {
                dir = Path.Combine(Path.GetTempPath(), "SecureConfigEditor_backup");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }

    /// <summary>Backs up the currently stored ciphertext (kept encrypted, so it opens only with that key). Returns null if nothing is stored.</summary>
    public static string BackupCurrent(string variableName)
    {
        string raw = ReadRaw(variableName);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string path = Path.Combine(BackupDirectory, variableName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
        File.WriteAllText(path, raw, new UTF8Encoding(false));
        return path;
    }
}
