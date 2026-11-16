using System;
using System.Collections.Generic;

namespace SecureConfig
{
    /// <summary>
    /// A single environment within the config (e.g. <c>dev</c>). Variable order is preserved.
    /// See <see cref="ConfigDocument.ValidateEnvironmentName"/> for the naming rule.
    /// </summary>
    public sealed class ConfigEnvironment
    {
        private readonly List<ConfigVariable> _variables = new List<ConfigVariable>();

        /// <summary>Creates an empty environment</summary>
        /// <param name="name">Environment name (e.g. local, dev, stg, prod)</param>
        public ConfigEnvironment(string name) { Name = name; }

        /// <summary>Environment name (e.g. prod)</summary>
        public string Name { get; set; }

        /// <summary>Variables (in saved/display order)</summary>
        public List<ConfigVariable> Variables { get { return _variables; } }

        /// <summary>Finds a variable by name (case-insensitive). Returns null if not found.</summary>
        public ConfigVariable Find(string name)
        {
            foreach (var v in _variables)
                if (string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        /// <summary>Deep copy (used by the duplicate-environment feature)</summary>
        public ConfigEnvironment Clone()
        {
            var e = new ConfigEnvironment(Name);
            foreach (var v in _variables) e.Variables.Add(new ConfigVariable(v.Name, v.Value));
            return e;
        }
    }
}
