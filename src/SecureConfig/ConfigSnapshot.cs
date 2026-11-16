using System;
using System.Collections.Generic;

namespace SecureConfig
{
    /// <summary>Immutable copy of the decrypted config. Environment and variable names are case-insensitive.</summary>
    public sealed class ConfigSnapshot
    {
        private readonly Dictionary<string, Dictionary<string, string>> _envs =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _envNames = new List<string>();

        internal ConfigSnapshot(ConfigDocument doc, string keySource)
        {
            foreach (var e in doc.Environments)
            {
                var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in e.Variables) vars[v.Name] = v.Value ?? "";
                _envs[e.Name] = vars;
                _envNames.Add(e.Name);
            }
            KeySource = keySource;
            LoadedAtUtc = DateTime.UtcNow;
        }

        /// <summary>Where the decryption key was loaded from (for logging)</summary>
        public string KeySource { get; private set; }

        /// <summary>When this snapshot was created (UTC)</summary>
        public DateTime LoadedAtUtc { get; private set; }

        /// <summary>Environment names (in saved order)</summary>
        public IList<string> EnvironmentNames { get { return _envNames.AsReadOnly(); } }

        /// <summary>Whether this config contains the environment (case-insensitive)</summary>
        public bool HasEnvironment(string environment)
        {
            return environment != null && _envs.ContainsKey(environment);
        }

        /// <summary>Looks up a value by environment and name. The value is returned as stored, unprocessed (including whitespace). Returns false if not found.</summary>
        public bool TryGet(string environment, string name, out string value)
        {
            value = null;
            Dictionary<string, string> vars;
            if (environment == null || name == null || !_envs.TryGetValue(environment, out vars)) return false;
            return vars.TryGetValue(name, out value);
        }

        /// <summary>Copy of the environment's variables. Null if the environment does not exist.</summary>
        public IDictionary<string, string> GetEnvironment(string environment)
        {
            Dictionary<string, string> vars;
            if (environment == null || !_envs.TryGetValue(environment, out vars)) return null;
            return new Dictionary<string, string>(vars, StringComparer.OrdinalIgnoreCase);
        }
    }
}
