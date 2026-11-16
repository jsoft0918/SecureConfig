using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SecureConfig.Internal;

namespace SecureConfig
{
    /// <summary>
    /// Config document. A two-level "environment → variables" structure, like GitHub Environments.
    /// <code>
    /// {
    ///   "dev":  { "DB_MAIN": "...", "OTP_MODE": "off" },
    ///   "prod": { "DB_MAIN": "..." }
    /// }
    /// </code>
    /// Environment and variable names are case-insensitive (like Windows environment variables). Order is preserved.
    /// This library only parses; writing JSON is done by the editor.
    /// </summary>
    public sealed class ConfigDocument
    {
        private readonly List<ConfigEnvironment> _environments = new List<ConfigEnvironment>();

        /// <summary>Environments (in saved/display order). The editor modifies this list directly.</summary>
        public List<ConfigEnvironment> Environments { get { return _environments; } }

        /// <summary>Finds an environment by name (case-insensitive). Returns null if not found.</summary>
        public ConfigEnvironment FindEnvironment(string name)
        {
            foreach (var e in _environments)
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>Deep copy (for computing things such as an import preview without touching the original)</summary>
        public ConfigDocument Clone()
        {
            var d = new ConfigDocument();
            foreach (var e in _environments) d.Environments.Add(e.Clone());
            return d;
        }

        private static readonly Regex EnvNamePattern = new Regex("^[A-Za-z0-9_-]{1,32}$");

        /// <summary>Environment naming rule: letters, digits, _, -; 1-32 characters</summary>
        public static string ValidateEnvironmentName(string name)
        {
            if (name == null || !EnvNamePattern.IsMatch(name))
                return "Environment name must be 1-32 characters of letters, digits, _, or -: '" + name + "'";
            return null;
        }

        /// <summary>Variable naming rule: not empty; no whitespace, '=', or control characters; at most 255 characters</summary>
        public static string ValidateVariableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Variable name is empty.";
            if (name.Length > 255) return "Variable name is too long (max 255 characters): '" + name + "'";
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '=')
                    return "Variable name cannot contain whitespace, '=', or control characters: '" + name + "'";
            }
            return null;
        }

        /// <summary>Validates the whole document. Returns null if there are no problems.</summary>
        public string Validate()
        {
            var envNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _environments)
            {
                string err = ValidateEnvironmentName(e.Name);
                if (err != null) return err;
                if (!envNames.Add(e.Name)) return "Duplicate environment '" + e.Name + "'.";

                var varNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in e.Variables)
                {
                    err = ValidateVariableName(v.Name);
                    if (err != null) return "[" + e.Name + "] " + err;
                    if (!varNames.Add(v.Name)) return "[" + e.Name + "] Duplicate variable '" + v.Name + "'.";
                }
            }
            return null;
        }

        /// <summary>
        /// Parses JSON. Throws FormatException (with line/column) if the structure or syntax is invalid.
        /// Values must be strings; numbers/true/false are accepted as strings and null as an empty string.
        /// </summary>
        public static ConfigDocument Parse(string json)
        {
            if (json == null) throw new FormatException("JSON is empty.");
            var parser = new MiniJsonParser(json);
            object root = parser.ParseDocument();

            var rootObj = root as List<KeyValuePair<string, object>>;
            if (rootObj == null)
                throw new FormatException("The root must be an object of the form { \"environment\": { \"variable\": \"value\" } }.");

            var doc = new ConfigDocument();
            foreach (var envPair in rootObj)
            {
                if (doc.FindEnvironment(envPair.Key) != null)
                    throw new FormatException("Duplicate environment '" + envPair.Key + "'.");
                var envObj = envPair.Value as List<KeyValuePair<string, object>>;
                if (envObj == null)
                    throw new FormatException("The value of environment '" + envPair.Key + "' must be an object of the form { \"variable\": \"value\" }.");

                var env = new ConfigEnvironment(envPair.Key);
                foreach (var varPair in envObj)
                {
                    if (env.Find(varPair.Key) != null)
                        throw new FormatException("[" + envPair.Key + "] Duplicate variable '" + varPair.Key + "'.");
                    object v = varPair.Value;
                    if (v != null && !(v is string))
                        throw new FormatException("[" + envPair.Key + "] The value of variable '" + varPair.Key + "' must be a string.");
                    env.Variables.Add(new ConfigVariable(varPair.Key, (string)v ?? ""));
                }
                doc.Environments.Add(env);
            }

            string err = doc.Validate();
            if (err != null) throw new FormatException(err);
            return doc;
        }
    }
}
