using SecureConfig;

namespace AspNetCoreSample;

/// <summary>
/// App-specific settings wrapper (recommended pattern).
///
/// - Keeps variable names as constants in one place to prevent typos and to show at a glance which settings the app uses.
/// - Handles type conversion (int, bool) and defaults here, so calling code stays clean.
/// - Properties read from SecureAppConfig on every access, so values changed in the editor take effect on the next request.
///   (Copying values into fields in the constructor would break live reload.)
/// </summary>
public sealed class AppSecrets
{
    public const string DbMainKey = "DB_MAIN";
    public const string PaymentApiKeyKey = "PAYMENT_API_KEY";
    public const string OtpModeKey = "OTP_MODE";
    public const string RateLimitPerMinKey = "RATE_LIMIT_PER_MIN";
    public const string FeatureNewCheckoutKey = "FEATURE_NEW_CHECKOUT";

    /// <summary>Required values; the service stays closed until they are present (used by the startup check).</summary>
    public static readonly string[] Required = { DbMainKey, PaymentApiKeyKey };

    private readonly SecureAppConfig _config;

    public AppSecrets(SecureAppConfig config) => _config = config;

    /// <summary>The underlying config object (for status checks and logging).</summary>
    public SecureAppConfig Config => _config;

    public string DbMain => _config.Require(DbMainKey);
    public string PaymentApiKey => _config.Require(PaymentApiKeyKey);
    public string OtpMode => _config.Get(OtpModeKey, "off")!;
    public int RateLimitPerMin => int.TryParse(_config.Get(RateLimitPerMinKey), out var v) ? v : 600;
    public bool FeatureNewCheckout => bool.TryParse(_config.Get(FeatureNewCheckoutKey), out var v) && v;
}
