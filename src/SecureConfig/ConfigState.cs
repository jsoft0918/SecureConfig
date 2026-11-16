namespace SecureConfig
{
    /// <summary>Result of a read (immutable)</summary>
    public sealed class ConfigState
    {
        internal ConfigState(string rawValue, bool isConfigured, ConfigSnapshot snapshot, string error)
        {
            RawValue = rawValue;
            IsConfigured = isConfigured;
            Snapshot = snapshot;
            Error = error;
        }

        /// <summary>The raw string (ciphertext) this state was built from. Used for change detection</summary>
        internal string RawValue { get; private set; }

        /// <summary>Whether the config variable is set (or has been read successfully at least once)</summary>
        public bool IsConfigured { get; private set; }

        /// <summary>The config to use. Null if no read has ever succeeded.</summary>
        public ConfigSnapshot Snapshot { get; private set; }

        /// <summary>Error from the most recent read, or null if OK. (If Snapshot is set, the last known good config is still in use.)</summary>
        public string Error { get; private set; }
    }
}
