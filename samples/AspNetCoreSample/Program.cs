// ============================================================================
//  SecureConfig ASP.NET Core sample
//
//  Setup: samples/README.md (use the editor to import sample-config.json into SAMPLE_CONFIG, then save)
//  Run:   dotnet run --project samples/AspNetCoreSample
//         -> http://localhost:5080         example of using config values
//         -> http://localhost:5080/health  config status (values are never exposed)
//
//  The runtime environment comes straight from ASP.NET Core's EnvironmentName.
//    Dev machine : ASPNETCORE_ENVIRONMENT = "Local" in Properties/launchSettings.json  -> local
//    Servers     : ASPNETCORE_ENVIRONMENT = Production / Staging / Development        -> prod / stg / dev
// ============================================================================

using AspNetCoreSample;
using SecureConfig;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Registration: SecureAppConfig is thread-safe, so a single singleton is enough.
//    Copying values into IConfiguration would break live reload, so read from this object when you need a value.
// ---------------------------------------------------------------------------
var secureConfig = new SecureAppConfig("SAMPLE_CONFIG", environment: builder.Environment.EnvironmentName);
builder.Services.AddSingleton(secureConfig);
builder.Services.AddSingleton<AppSecrets>();

var app = builder.Build();

app.Logger.LogInformation("Environment: {Env} (resolved from: {Source}), config: {ConfigSource}",
    SafeEnvironment(secureConfig), SafeEnvironmentSource(secureConfig), secureConfig.Source);

// ---------------------------------------------------------------------------
// 2. Fail-fast gate middleware
//    Until the required settings can be read, requests are answered with 503 instead of being served.
//    Once you fix the settings in the editor and save, the gate opens on the next request without a restart.
//    After it has opened, it never closes again: if the settings later break, the app keeps running on the last good values.
// ---------------------------------------------------------------------------
var ready = false;
app.Use(async (context, next) =>
{
    if (!ready && context.Request.Path != "/health")
    {
        if (secureConfig.CheckRequired(AppSecrets.Required, out var error))
        {
            ready = true;
            app.Logger.LogInformation("Required settings verified; opening the service.");
        }
        else
        {
            app.Logger.LogError("Returning 503 due to a config error: {Error}", error); // contains names only, never values, so it is safe to log
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Service is starting up. (Check the configuration: /health)");
            return;
        }
    }
    await next();
});

// ---------------------------------------------------------------------------
// 3. Usage: inject AppSecrets via DI and read its properties (always the latest values on every request)
// ---------------------------------------------------------------------------
app.MapGet("/", (AppSecrets secrets) => Results.Ok(new
{
    environment = secrets.Config.Environment,
    otpMode = secrets.OtpMode,
    rateLimitPerMin = secrets.RateLimitPerMin,
    featureNewCheckout = secrets.FeatureNewCheckout,
    dbServer = ServerOf(secrets.DbMain), // show only the server name, not the whole connection string
}));

// Health check: report status only, never values
app.MapGet("/health", (SecureAppConfig config) =>
{
    var ok = config.CheckRequired(AppSecrets.Required, out var error);
    var body = new
    {
        status = ok ? (config.Error is null ? "healthy" : "degraded") : "unhealthy",
        environment = SafeEnvironment(config),
        environmentSource = SafeEnvironmentSource(config),
        source = config.Source,
        error = error ?? config.Error, // degraded: the new value is broken, so the previous values are still in use
        loadedAtUtc = config.State.Snapshot?.LoadedAtUtc,
    };
    return ok ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

// Keep logging and the health check from throwing when the environment cannot be resolved
static string SafeEnvironment(SecureAppConfig c)
{
    try { return c.Environment; } catch (InvalidOperationException) { return "(not set)"; }
}

static string SafeEnvironmentSource(SecureAppConfig c)
{
    try { return c.EnvironmentSource; } catch (InvalidOperationException) { return "(not set)"; }
}

static string ServerOf(string connectionString) =>
    connectionString.Split(';')
        .Select(p => p.Split('=', 2))
        .FirstOrDefault(kv => kv.Length == 2 && kv[0].Trim().Equals("Server", StringComparison.OrdinalIgnoreCase))?[1] ?? "?";
