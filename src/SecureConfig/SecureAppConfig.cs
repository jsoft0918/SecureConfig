using System;
using System.Collections.Generic;
using SecureConfig.Internal;

namespace SecureConfig
{
    /// <summary>
    /// Entry point for applications. Combines a "config variable + runtime environment" so values for the
    /// current environment can be read by name alone.
    ///
    /// <code>
    /// // ASP.NET 4.8: app pool environment variable MYAPP_ENV. Debug builds fall back to local; Release builds must set it (otherwise an exception is thrown)
    /// #if DEBUG
    ///     const string fallback = "local";
    /// #else
    ///     const string fallback = null;
    /// #endif
    /// var cfg = new SecureAppConfig("MYAPP_CONFIG", environmentVariable: "MYAPP_ENV", fallbackEnvironment: fallback);
    ///
    /// // ASP.NET Core: pass the host environment name as is (Production → prod, Staging → stg, Development → dev, Local → local)
    /// var cfg = new SecureAppConfig("MYAPP_CONFIG", environment: builder.Environment.EnvironmentName);
    ///
    /// string db = cfg.Require("DB_MAIN");
    /// string mode = cfg.Get("OTP_MODE", "off");
    /// </code>
    ///
    /// Runtime environment resolution order (resolved once and then fixed; restart the app to change it):
    ///  1) the environment argument (e.g. ASP.NET Core's EnvironmentName)
    ///  2) the environment variable named by environmentVariable (process, then machine). IIS app pool environment variables arrive here
    ///  3) the fallbackEnvironment argument (a default chosen by the app, e.g. from the build configuration)
    ///  An unknown environment name throws, so the app never connects to the wrong environment (e.g. the wrong database).
    ///
    /// Values are read on every call (decrypted only when the stored value changes). Changes saved in the editor take effect on the next call.
    /// </summary>
    public sealed class SecureAppConfig
    {
        /// <summary>Default allowed environments</summary>
        public static readonly string[] DefaultEnvironments = { "local", "dev", "stg", "prod" };

        // Environment name aliases (case-insensitive), e.g. ASP.NET Core's "Production" / "Staging" / "Development".
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "local", "local" },
            { "dev", "dev" }, { "development", "dev" },
            { "stg", "stg" }, { "stage", "stg" }, { "staging", "stg" },
            { "prod", "prod" }, { "prd", "prod" }, { "production", "prod" },
        };

        private readonly SecureConfigReader _reader;
        private readonly string _environmentArgument;
        private readonly string _environmentVariable;
        private readonly string _fallbackEnvironment;
        private readonly string _fallbackReason;
        private readonly HashSet<string> _allowed;
        private readonly List<string> _allowedOrdered = new List<string>();
        private readonly object _envLock = new object();
        private volatile string _environment;
        private string _environmentSource;

        /// <param name="configVariable">Name of the environment variable holding the encrypted config (e.g. MYAPP_CONFIG)</param>
        /// <param name="environmentVariable">Name of the environment variable holding the runtime environment (e.g. MYAPP_ENV), or null</param>
        /// <param name="fallbackEnvironment">Environment to use when none of the above resolves it (e.g. "local" for Debug builds, "prod" for Release), or null</param>
        /// <param name="environment">Runtime environment specified directly (e.g. ASP.NET Core's EnvironmentName). Takes highest precedence when set</param>
        /// <param name="allowedEnvironments">Allowed environments. If null: local, dev, stg, prod</param>
        /// <param name="fallbackReason">Reason for the fallback, for logging (e.g. "build config (Debug)")</param>
        public SecureAppConfig(string configVariable, string environmentVariable = null, string fallbackEnvironment = null,
            string environment = null, IEnumerable<string> allowedEnvironments = null, string fallbackReason = null)
        {
            _reader = SecureConfigReader.For(configVariable);
            _environmentArgument = environment;
            _environmentVariable = string.IsNullOrWhiteSpace(environmentVariable) ? null : environmentVariable.Trim();
            _fallbackEnvironment = fallbackEnvironment;
            _fallbackReason = string.IsNullOrWhiteSpace(fallbackReason) ? "app default" : fallbackReason;

            _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in allowedEnvironments ?? DefaultEnvironments)
            {
                string err = ConfigDocument.ValidateEnvironmentName(e);
                if (err != null) throw new ArgumentException(err, "allowedEnvironments");
                string n = e.Trim().ToLowerInvariant();
                if (_allowed.Add(n)) _allowedOrdered.Add(n);
            }
        }

        /// <summary>Underlying reader (advanced use, e.g. inspecting state)</summary>
        public SecureConfigReader Reader { get { return _reader; } }

        /// <summary>Config variable name (e.g. MYAPP_CONFIG)</summary>
        public string ConfigVariable { get { return _reader.VariableName; } }

        /// <summary>Runtime environment variable name (e.g. MYAPP_ENV), or null</summary>
        public string EnvironmentVariable { get { return _environmentVariable; } }

        /// <summary>Allowed environments</summary>
        public IList<string> AllowedEnvironments { get { return _allowedOrdered.AsReadOnly(); } }

        // ------------------------------------------------------------ Runtime environment

        /// <summary>Current runtime environment (e.g. "local", "prod"). Throws InvalidOperationException if it cannot be resolved or is invalid</summary>
        public string Environment
        {
            get
            {
                EnsureEnvironment();
                return _environment;
            }
        }

        /// <summary>Where the runtime environment came from (for logging). E.g. "MYAPP_ENV", "explicit", "build config (Debug)"</summary>
        public string EnvironmentSource
        {
            get
            {
                EnsureEnvironment();
                return _environmentSource;
            }
        }

        /// <summary>Whether the current environment is <paramref name="name"/> (aliases allowed: "production" → prod)</summary>
        public bool IsEnvironment(string name)
        {
            string n = Normalize(name);
            return n != null && string.Equals(Environment, n, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes an environment name: maps aliases (production → prod, staging → stg, development → dev, prd → prod)
        /// and returns null if the result is not in the allowed list.
        /// </summary>
        public string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string v = value.Trim();
            string mapped;
            if (Aliases.TryGetValue(v, out mapped)) v = mapped;
            v = v.ToLowerInvariant();
            return _allowed.Contains(v) ? v : null;
        }

        private void EnsureEnvironment()
        {
            if (_environment != null) return;
            lock (_envLock)
            {
                if (_environment != null) return;

                string raw, source;
                if (!string.IsNullOrWhiteSpace(_environmentArgument))
                {
                    raw = _environmentArgument;
                    source = "explicit";
                }
                else if (_environmentVariable != null && !string.IsNullOrWhiteSpace(raw = ReadEnv(_environmentVariable)))
                {
                    source = _environmentVariable;
                }
                else if (!string.IsNullOrWhiteSpace(_fallbackEnvironment))
                {
                    raw = _fallbackEnvironment;
                    source = _fallbackReason;
                }
                else
                {
                    throw new InvalidOperationException(_environmentVariable != null
                        ? string.Format("The runtime environment is not set. Set the environment variable {0} ({1}). For IIS, set it as an application pool environment variable.",
                            _environmentVariable, string.Join(", ", AllowedEnvironments))
                        : "The runtime environment cannot be determined. Pass one of environment, environmentVariable, or fallbackEnvironment.");
                }

                string env = Normalize(raw);
                if (env == null)
                {
                    throw new InvalidOperationException(string.Format("Unknown runtime environment '{0}' (from {1}). Use one of: {2}.",
                        raw, source, string.Join(", ", AllowedEnvironments)));
                }

                _environmentSource = source;
                _environment = env;
            }
        }

        private static string ReadEnv(string name)
        {
            string v = EnvVars.ProcessThenMachine(name);
            return v == null ? null : v.Trim();
        }

        // ------------------------------------------------------------ Values

        /// <summary>Value for the current environment, or defaultValue if missing (trimmed; empty values count as missing)</summary>
        public string Get(string name, string defaultValue = null)
        {
            return _reader.Get(Environment, name, defaultValue);
        }

        /// <summary>Required value for the current environment. If missing, throws InvalidOperationException describing the cause (no config / no key / no environment / no entry)</summary>
        public string Require(string name)
        {
            string env = Environment;
            string v = _reader.Get(env, name);
            if (v != null) return v;
            throw new InvalidOperationException(_reader.DescribeMissing(env, name, EnvironmentSource));
        }

        /// <summary>
        /// Checks that all required values exist (for startup checks / returning 503). Runtime environment errors are caught here too. Returns true if everything is OK.
        /// </summary>
        public bool CheckRequired(IEnumerable<string> names, out string error)
        {
            try
            {
                EnsureEnvironment(); // also catches runtime environment errors
                if (names != null)
                    foreach (string n in names) Require(n);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Display name for error messages: "prod.DB_MAIN"</summary>
        public string DescribeName(string name)
        {
            string env;
            try { env = Environment; } catch (InvalidOperationException) { env = "?"; }
            return env + "." + name;
        }

        // ------------------------------------------------------------ State (for logging)

        /// <summary>Read state (IsConfigured / Snapshot / Error)</summary>
        public ConfigState State { get { return _reader.GetState(); } }

        /// <summary>Description of the config source. E.g. "MYAPP_CONFIG encrypted config, key file C:\ProgramData\..."</summary>
        public string Source
        {
            get
            {
                ConfigState st = _reader.GetState();
                if (!st.IsConfigured) return ConfigVariable + " not set";
                if (st.Snapshot == null) return ConfigVariable + " failed to load";
                return ConfigVariable + " encrypted config, " + st.Snapshot.KeySource;
            }
        }

        /// <summary>Most recent error (null if OK). Also set while still running on the last known good config.</summary>
        public string Error { get { return _reader.GetState().Error; } }
    }
}
