using System;
using System.Data.SqlClient;
using System.Web;

namespace NetFramework48Sample
{
    /// <summary>
    /// Example of wiring up Global.asax (put the same code in your real web project's Global.asax.cs).
    /// In this sample it is only compiled, never run.
    /// </summary>
    public class SampleGlobal : HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            // Don't read settings here. A failure here leaves the app half-initialized, so the check happens in BeginRequest instead.
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            if (ConfigGuard.BlockIfNotReady(Context)) return; // not ready: the request ends with 503
        }
    }

    /// <summary>Usage example: a repository class that reads the connection string each time it needs one.</summary>
    public static class OrderRepository
    {
        public static SqlConnection Open()
        {
            // Don't cache the connection string in a static field. That way, a password changed in the editor takes effect on the very next connection.
            var conn = new SqlConnection(AppConfig.Require(AppConfig.DB_MAIN));
            conn.Open();
            return conn;
        }
    }
}
