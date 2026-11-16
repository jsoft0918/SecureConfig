using System;

namespace NetFramework48Sample
{
    /// <summary>
    /// .NET Framework 4.8 console demo. Uses the same AppConfig as the web app.
    ///
    /// Setup: samples/README.md (use the editor to import sample-config.json into SAMPLE_CONFIG, then save)
    /// Run:   dotnet run --project samples/NetFramework48Sample                (Debug -> local)
    ///        dotnet run --project samples/NetFramework48Sample -c Release     (Release -> requires SAMPLE_ENV)
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Runtime: .NET Framework " + Environment.Version);

            string error;
            if (!AppConfig.Config.CheckRequired(AppConfig.RequiredNames, out error))
            {
                // In a web app, ConfigGuard would return 503 at this point
                Console.WriteLine("Cannot read settings: " + error);
                return 1;
            }

            Console.WriteLine("Environment: " + AppConfig.Environment + " (resolved from: " + AppConfig.Config.EnvironmentSource + ")");
            Console.WriteLine("Source     : " + AppConfig.Config.Source);
            Console.WriteLine("Is prod    : " + AppConfig.IsProd);
            Console.WriteLine("OTP_MODE   : " + AppConfig.Get(AppConfig.OTP_MODE, "off"));
            Console.WriteLine("RATE_LIMIT : " + AppConfig.RateLimitPerMin);
            Console.WriteLine("DB_MAIN    : " + Mask(AppConfig.Require(AppConfig.DB_MAIN)));
            return 0;
        }

        private static string Mask(string value)
        {
            return value.Length <= 6 ? "******" : value.Substring(0, 6) + "******";
        }
    }
}
