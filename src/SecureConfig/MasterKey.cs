using System;
using System.IO;
using SecureConfig.Internal;

namespace SecureConfig
{
    /// <summary>
    /// Locates the master key (read-only). One key per server (PC) is shared by all config variables on that server.
    ///
    /// Lookup order
    ///  1) process environment variable <c>SECURECONFIG_MASTER_KEY</c> (Base64) — for development PCs only
    ///  2) key file: the path in environment variable <c>SECURECONFIG_MASTER_KEY_FILE</c>,
    ///     otherwise the default path <c>%ProgramData%\SecureConfig\master.key</c>
    ///
    /// On Windows the key file is created by the editor with Full Control for Administrators / SYSTEM and read-only access for IIS_IUSRS.
    /// </summary>
    public static class MasterKey
    {
        /// <summary>Name of the environment variable that holds the key directly (Base64)</summary>
        public const string KeyEnvName = "SECURECONFIG_MASTER_KEY";

        /// <summary>Name of the environment variable that overrides the key file path</summary>
        public const string KeyFileEnvName = "SECURECONFIG_MASTER_KEY_FILE";

        /// <summary>Default key file path: <c>%ProgramData%\SecureConfig\master.key</c></summary>
        public static string DefaultKeyFilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "SecureConfig", "master.key");
            }
        }

        /// <summary>
        /// Name of the environment variable that currently supplies the key directly, or null if the key comes from a file.
        /// (The editor uses this to warn that an environment variable overrides the key file.)
        /// </summary>
        public static string ActiveKeyEnvName
        {
            get
            {
                return string.IsNullOrWhiteSpace(EnvVars.Process(KeyEnvName)) ? null : KeyEnvName;
            }
        }

        /// <summary>Whether the name is reserved for the master key and must not be used as a config variable</summary>
        public static bool IsReservedName(string name)
        {
            return name != null && name.StartsWith(KeyEnvName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Key file path actually used: the <c>SECURECONFIG_MASTER_KEY_FILE</c> path if set; otherwise the default path.
        /// </summary>
        public static string ResolveKeyFilePath()
        {
            string p = EnvVars.ProcessThenMachine(KeyFileEnvName);
            return string.IsNullOrWhiteSpace(p) ? DefaultKeyFilePath : Environment.ExpandEnvironmentVariables(p.Trim());
        }

        /// <summary>
        /// Loads the key. Returns null if no key is found anywhere.
        /// If the key file exists but cannot be read (no permission) or is malformed, throws <see cref="InvalidOperationException"/> describing the cause.
        /// </summary>
        /// <param name="source">Where the key was loaded from (for logging). Null if no key was found.</param>
        public static byte[] TryLoad(out string source)
        {
            string envName = ActiveKeyEnvName;
            if (envName != null)
            {
                source = "environment variable " + envName;
                return ParseOrThrow(EnvVars.Process(envName), source);
            }

            string path = ResolveKeyFilePath();
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (FileNotFoundException) { source = null; return null; }
            catch (DirectoryNotFoundException) { source = null; return null; }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("No permission to read the master key file: " + path +
                    " (running as: " + Environment.UserDomainName + "\\" + Environment.UserName + ")", ex);
            }

            source = "key file " + path;
            return ParseOrThrow(text, source);
        }

        private static byte[] ParseOrThrow(string base64, string source)
        {
            try { return SecretFormat.ParseKey(base64); }
            catch (FormatException ex) { throw new InvalidOperationException(source + ": " + ex.Message, ex); }
        }
    }
}
