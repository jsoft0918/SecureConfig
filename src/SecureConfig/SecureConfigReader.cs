using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using SecureConfig.Internal;

namespace SecureConfig
{
    /// <summary>
    /// Low-level reader for the encrypted config JSON stored in an environment variable (e.g. <c>MYAPP_CONFIG</c>). Read-only and thread-safe.
    /// Each app uses its own variable name: <c>SecureConfigReader.For("MYAPP_CONFIG")</c>.
    /// Most apps should use <see cref="SecureAppConfig"/>, which also handles the runtime environment.
    ///
    /// <list type="bullet">
    /// <item>Each call reads only the raw string (ciphertext) and compares it with the previous one (a few microseconds).</item>
    /// <item>Only when the raw value changes does it load the key, decrypt, parse, and swap in a whole new snapshot
    ///       → changes saved in the editor take effect on the next call (no app restart needed).</item>
    /// <item>If the new value is bad (key mismatch, invalid JSON, deleted), the last known good snapshot keeps being used and Error holds the cause.
    ///       The same bad value is retried at most once per <see cref="ErrorRetryInterval"/> (so fixing only the key file recovers automatically).</item>
    /// <item>The machine-level environment variable (registry) is read first, then the process environment variable
    ///       (the process environment variable is mainly used by tests).</item>
    /// </list>
    /// </summary>
    public sealed class SecureConfigReader
    {
        private static readonly ConcurrentDictionary<string, SecureConfigReader> Instances =
            new ConcurrentDictionary<string, SecureConfigReader>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex VariableNamePattern = new Regex("^[A-Z][A-Z0-9_]{1,63}$", RegexOptions.CultureInvariant);

        /// <summary>Shared instance per variable name (use this in apps). The same name always returns the same instance.</summary>
        public static SecureConfigReader For(string variableName)
        {
            ValidateVariableName(variableName);
            return Instances.GetOrAdd(variableName, n => new SecureConfigReader(n));
        }

        /// <summary>Config variable naming rule: starts with an uppercase letter; uppercase letters, digits, and _; 2-64 characters (e.g. MYAPP_CONFIG)</summary>
        public static void ValidateVariableName(string variableName)
        {
            if (variableName == null || !VariableNamePattern.IsMatch(variableName))
                throw new ArgumentException("Config variable name must start with an uppercase letter and contain only uppercase letters, digits, and _ (2-64 characters): '" + variableName + "'");
        }

        private readonly string _variableName;
        private readonly object _sync = new object();
        private volatile ConfigState _state; // the raw string (RawValue) and the result are bundled in one object and swapped atomically
        private ConfigSnapshot _lastGood;
        private long _nextRetryTicks; // UTC ticks. Read without the lock, so access only via Interlocked/Volatile

        /// <summary>Creates a new instance (for tests). Apps should use <see cref="For"/>.</summary>
        public SecureConfigReader(string variableName)
        {
            ValidateVariableName(variableName);
            _variableName = variableName;
            ErrorRetryInterval = TimeSpan.FromSeconds(10);
        }

        /// <summary>Name of the environment variable being read</summary>
        public string VariableName { get { return _variableName; } }

        /// <summary>Interval for retrying the same value while in an error state (default 10 seconds)</summary>
        public TimeSpan ErrorRetryInterval { get; set; }

        /// <summary>Current state. If the value has changed, it is reloaded during this call.</summary>
        public ConfigState GetState()
        {
            string raw = ReadRaw();
            ConfigState st = _state;
            if (IsFresh(st, raw)) return st;

            lock (_sync)
            {
                st = _state;
                return IsFresh(st, raw) ? st : Reload(raw);
            }
        }

        /// <summary>Reads a value, or returns defaultValue if missing. (Values are trimmed; empty strings count as missing.)</summary>
        public string Get(string environment, string name, string defaultValue = null)
        {
            string v;
            if (TryGet(environment, name, out v) && v != null)
            {
                v = v.Trim();
                if (v.Length > 0) return v;
            }
            return defaultValue;
        }

        /// <summary>Reads a required value. If missing, throws <see cref="InvalidOperationException"/> describing the cause (variable not set / no key / no environment / no entry).</summary>
        public string Require(string environment, string name)
        {
            string v = Get(environment, name);
            if (v != null) return v;
            throw new InvalidOperationException(DescribeMissing(environment, name, null));
        }

        /// <summary>Looks up a value in the current state. Returns false if there is no snapshot or no value.</summary>
        public bool TryGet(string environment, string name, out string value)
        {
            ConfigSnapshot snap = GetState().Snapshot;
            if (snap != null) return snap.TryGet(environment, name, out value);
            value = null;
            return false;
        }

        /// <summary>Explains why a value was not found. environmentSource is where the runtime environment came from (or null).</summary>
        internal string DescribeMissing(string environment, string name, string environmentSource)
        {
            ConfigState st = GetState();
            if (!st.IsConfigured)
                return "Environment variable " + _variableName + " is not set. Save the settings with the editor.";
            if (st.Snapshot == null)
                return st.Error ?? ("Failed to read " + _variableName + ".");
            if (!st.Snapshot.HasEnvironment(environment))
                return _variableName + " has no '" + environment + "' environment" +
                       (environmentSource != null ? " (environment from: " + environmentSource + ")" : "") +
                       ". Add the '" + environment + "' environment in the editor.";
            return "'" + name + "' is missing from the '" + environment + "' environment in " + _variableName + ". Add it in the editor.";
        }

        private bool IsFresh(ConfigState st, string raw)
        {
            if (st == null || !string.Equals(raw, st.RawValue, StringComparison.Ordinal)) return false;
            return st.Error == null || DateTime.UtcNow.Ticks < Volatile.Read(ref _nextRetryTicks);
        }

        private ConfigState Reload(string raw)
        {
            ConfigState st;
            if (string.IsNullOrWhiteSpace(raw))
            {
                st = _lastGood == null
                    ? new ConfigState(raw, false, null, null)
                    : new ConfigState(raw, true, _lastGood,
                        "Environment variable " + _variableName + " was removed. Continuing with the last known good config.");
            }
            else
            {
                try
                {
                    string keySource;
                    byte[] key = MasterKey.TryLoad(out keySource);
                    if (key == null)
                        throw new InvalidOperationException("Master key not found (key file: " + MasterKey.ResolveKeyFilePath() + ").");

                    ConfigDocument doc = ConfigDocument.Parse(SecretFormat.Decrypt(raw, key));
                    _lastGood = new ConfigSnapshot(doc, keySource);
                    st = new ConfigState(raw, true, _lastGood, null);
                }
                catch (Exception ex)
                {
                    string msg = "Failed to read " + _variableName + ": " + ex.Message;
                    if (_lastGood != null) msg += " (continuing with the last known good config)";
                    st = new ConfigState(raw, true, _lastGood, msg);
                }
            }

            if (st.Error != null)
                Volatile.Write(ref _nextRetryTicks, DateTime.UtcNow.Add(ErrorRetryInterval).Ticks);

            _state = st;
            return st;
        }

        private string ReadRaw()
        {
            return EnvVars.MachineThenProcess(_variableName);
        }
    }
}
