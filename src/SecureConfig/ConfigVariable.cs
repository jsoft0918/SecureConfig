namespace SecureConfig
{
    /// <summary>
    /// A single name=value pair within an environment (e.g. <c>DB_MAIN = server=...</c>).
    /// See <see cref="ConfigDocument.ValidateVariableName"/> for the naming rule.
    /// </summary>
    public sealed class ConfigVariable
    {
        /// <summary>Creates a variable</summary>
        /// <param name="name">Variable name (e.g. DB_MAIN)</param>
        /// <param name="value">Value (plaintext). Null is stored as an empty string.</param>
        public ConfigVariable(string name, string value) { Name = name; Value = value; }

        /// <summary>Variable name. Case-insensitive.</summary>
        public string Name { get; set; }

        /// <summary>Value (decrypted plaintext). Never write it to logs.</summary>
        public string Value { get; set; }
    }
}
