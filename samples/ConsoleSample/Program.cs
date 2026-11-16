// ============================================================================
//  SecureConfig console sample
//
//  One-time setup (see samples/README.md)
//    1) SecureConfigEditor.exe -> Key management -> Create new key -> check "Grant read access to current user" -> Save
//    2) New config variable SAMPLE_CONFIG -> Import samples/sample-config.json -> Save
//
//  Run
//    dotnet run --project samples/ConsoleSample                  # local environment (default)
//    $env:SAMPLE_ENV="prod"; dotnet run --project samples/ConsoleSample   # prod environment
//    dotnet run --project samples/ConsoleSample -- --watch       # watch values change live as you edit them in the editor
// ============================================================================

using SecureConfig;

// ---------------------------------------------------------------------------
// 1. Create the config object once per app (keep it in a static or a DI singleton).
//
//    "SAMPLE_CONFIG" : the config variable where the editor stores the encrypted settings (one per app)
//    "SAMPLE_ENV"    : the environment variable that tells this app which environment to read (dev/stg/prod per server)
//    fallback        : the environment to use when SAMPLE_ENV is not set. For production apps, leave it null
//                      so the app refuses to start without an explicit environment
//                      (this prevents accidentally connecting to the wrong database).
// ---------------------------------------------------------------------------
var config = new SecureAppConfig(
    configVariable: "SAMPLE_CONFIG",
    environmentVariable: "SAMPLE_ENV",
    fallbackEnvironment: "local",
    fallbackReason: "sample default");

// ---------------------------------------------------------------------------
// 2. Startup check: if a required value is missing, show the cause and exit right away.
//    Error messages contain only variable names, never values, so they are safe to log.
// ---------------------------------------------------------------------------
string[] required = { SampleKeys.DbMain, SampleKeys.PaymentApiKey };
if (!config.CheckRequired(required, out string? error))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Cannot read settings: " + error);
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Complete the setup steps in samples/README.md first.");
    return 1;
}

Console.WriteLine($"Environment : {config.Environment}  (resolved from: {config.EnvironmentSource})");
Console.WriteLine($"Source      : {config.Source}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 3. Read values
//    Require : required value; throws an exception describing the cause if missing
//    Get     : optional value; returns the default if missing
//    Values are always strings, so convert numbers/booleans in your app.
// ---------------------------------------------------------------------------
string db = config.Require(SampleKeys.DbMain);
string otpMode = config.Get(SampleKeys.OtpMode, "off")!;
int rateLimit = int.TryParse(config.Get(SampleKeys.RateLimitPerMin), out var n) ? n : 600;
bool newCheckout = bool.TryParse(config.Get(SampleKeys.FeatureNewCheckout), out var b) && b;

Console.WriteLine($"DB_MAIN              = {Mask(db)}");
Console.WriteLine($"PAYMENT_API_KEY      = {Mask(config.Require(SampleKeys.PaymentApiKey))}");
Console.WriteLine($"OTP_MODE             = {otpMode}");
Console.WriteLine($"RATE_LIMIT_PER_MIN   = {rateLimit}");
Console.WriteLine($"FEATURE_NEW_CHECKOUT = {newCheckout}");
Console.WriteLine($"missing + default    = {config.Get("NOT_EXISTS", "(default)")}");

// ---------------------------------------------------------------------------
// 4. Live reload demo (--watch)
//    Don't read a value once and keep it in a variable; call config.Get() whenever you need it.
//    Each call only compares the encrypted string (a few microseconds), so calling it per request is fine.
//    Change OTP_MODE in the editor and click [Save]; the next line shows the new value. (Windows)
// ---------------------------------------------------------------------------
if (args.Contains("--watch"))
{
    Console.WriteLine();
    Console.WriteLine("Reading OTP_MODE every 2 seconds. Change it in the editor and save. (Exit: Ctrl+C)");
    while (true)
    {
        string now = config.Get(SampleKeys.OtpMode, "off")!;
        string warn = config.Error is null ? "" : "   ! " + config.Error; // if a broken value arrives, keep running on the last good values
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  OTP_MODE = {now}{warn}");
        Thread.Sleep(2000);
    }
}

return 0;

// Display-only masking so secrets are never printed in full (shows the first 6 characters only)
static string Mask(string value) => value.Length <= 6 ? "******" : value[..6] + "******";

/// <summary>
/// Variable name constants. Keeping names in one place instead of scattering string
/// literals through the code prevents typos and shows at a glance which settings this app uses.
/// </summary>
internal static class SampleKeys
{
    public const string DbMain = "DB_MAIN";
    public const string PaymentApiKey = "PAYMENT_API_KEY";
    public const string OtpMode = "OTP_MODE";
    public const string RateLimitPerMin = "RATE_LIMIT_PER_MIN";
    public const string FeatureNewCheckout = "FEATURE_NEW_CHECKOUT";
}
