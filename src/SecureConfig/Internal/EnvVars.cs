using System;
using System.Security;

namespace SecureConfig.Internal
{
    /// <summary>
    /// Environment variable helpers. They differ only in whether the process or the machine scope is checked first.
    /// The machine scope is read directly from the registry (HKLM), so values changed after the process started are visible.
    /// </summary>
    internal static class EnvVars
    {
        /// <summary>Process, then machine. Prefers values supplied by the app's run-as account / app pool.</summary>
        public static string ProcessThenMachine(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            return IsBlank(v) ? Machine(name) : v;
        }

        /// <summary>Machine, then process. Prefers the latest value saved by the editor (the process copy is not refreshed).</summary>
        public static string MachineThenProcess(string name)
        {
            string v = Machine(name);
            return IsBlank(v) ? Environment.GetEnvironmentVariable(name) : v;
        }

        /// <summary>Process environment variable only</summary>
        public static string Process(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        private static string Machine(string name)
        {
            try { return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine); }
            catch (SecurityException) { return null; }
        }

        private static bool IsBlank(string v)
        {
            return v == null || v.Trim().Length == 0;
        }
    }
}
