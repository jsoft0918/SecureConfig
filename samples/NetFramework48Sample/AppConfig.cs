using SecureConfig;

namespace NetFramework48Sample
{
    /// <summary>
    /// App-wide settings (recommended pattern for ASP.NET 4.8 / .NET Framework). Copy this file and just rename it.
    ///
    /// How the runtime environment is chosen
    ///   Debug build   (developer machine, Visual Studio / IIS Express) -> always "local"
    ///   Release build (server, IIS)                                     -> IIS app pool environment variable SAMPLE_ENV (dev|stg|prod)
    ///                                                                      if missing, startup is blocked (ConfigGuard returns 503)
    ///
    /// Why Release has no default ("prod"):
    ///   If someone forgets to set the environment on a dev server, the app could silently connect
    ///   using production settings (the production database). Refusing to open when nothing is set is the safe choice.
    ///
    /// Setting the app pool environment variable (elevated PowerShell):
    ///   appcmd set config -section:system.applicationHost/applicationPools
    ///     "/+[name='MyApp'].environmentVariables.[name='SAMPLE_ENV',value='prod']" /commit:apphost
    /// </summary>
    public static class AppConfig
    {
#if DEBUG
        public static readonly SecureAppConfig Config =
            new SecureAppConfig("SAMPLE_CONFIG", environment: "local");
#else
        public static readonly SecureAppConfig Config =
            new SecureAppConfig("SAMPLE_CONFIG", environmentVariable: "SAMPLE_ENV");
#endif

        // ---- Variable name constants (prevents typos and lists the settings this app uses)
        public const string DB_MAIN = "DB_MAIN";
        public const string PAYMENT_API_KEY = "PAYMENT_API_KEY";
        public const string OTP_MODE = "OTP_MODE";
        public const string RATE_LIMIT_PER_MIN = "RATE_LIMIT_PER_MIN";

        /// <summary>Required values; the service stays closed until they are present.</summary>
        public static readonly string[] RequiredNames = { DB_MAIN, PAYMENT_API_KEY };

        // ---- Convenience members. Values are read on every call, so changes saved in the editor take effect on the next request.
        public static string Environment { get { return Config.Environment; } }
        public static bool IsProd { get { return Config.IsEnvironment("prod"); } }

        public static string Get(string name, string defaultValue = null) { return Config.Get(name, defaultValue); }
        public static string Require(string name) { return Config.Require(name); }

        /// <summary>
        /// Numeric setting example. Wrong: storing it once in a static readonly field (changes are never picked up).
        /// Right: a property that reads it every time.
        /// </summary>
        public static int RateLimitPerMin
        {
            get
            {
                int v;
                return int.TryParse(Get(RATE_LIMIT_PER_MIN), out v) ? v : 600;
            }
        }
    }
}
