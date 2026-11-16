using System;
using System.Diagnostics;
using System.Web;

namespace NetFramework48Sample
{
    /// <summary>
    /// Fail-fast guard for ASP.NET 4.8. Call it from Application_BeginRequest in Global.asax.
    ///
    /// Why not throw from Application_Start?
    ///   When Application_Start throws, ASP.NET fails only the first request; subsequent requests
    ///   run against a partially initialized app. So instead of throwing, we return 503 on every request.
    ///
    /// Behavior
    ///   - Every request gets 503 until the required settings and the runtime environment are available
    ///   - Once you fix the settings in the editor and save, the gate opens on the next request without a restart
    ///   - Once open, it never closes again (if the settings later break, the library keeps using the last good values)
    /// </summary>
    public static class ConfigGuard
    {
        private static volatile bool _ready;
        private static string _lastLoggedError;

        /// <summary>If not ready, writes a 503, ends the request and returns true. Returns false when ready.</summary>
        public static bool BlockIfNotReady(HttpContext context)
        {
            if (_ready) return false;

            string error;
            if (AppConfig.Config.CheckRequired(AppConfig.RequiredNames, out error))
            {
                _ready = true;
                Trace.TraceInformation("Settings verified - environment: {0} (resolved from: {1}), config: {2}",
                    AppConfig.Config.Environment, AppConfig.Config.EnvironmentSource, AppConfig.Config.Source);
                return false;
            }

            // Log each distinct cause only once (avoids flooding the log on every request). The message contains names only, never values.
            if (!string.Equals(error, _lastLoggedError, StringComparison.Ordinal))
            {
                _lastLoggedError = error;
                Trace.TraceError("Returning 503 due to a config error: " + error);
            }

            context.Response.StatusCode = 503;
            context.Response.TrySkipIisCustomErrors = true;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Write("Service is starting up.");
            context.ApplicationInstance.CompleteRequest();
            return true;
        }
    }
}
