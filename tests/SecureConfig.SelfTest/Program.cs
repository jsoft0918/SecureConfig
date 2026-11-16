using System.Security.AccessControl;
using System.Security.Principal;
using SecureConfig;
using SecureConfig.Editor.Core;

int fail = 0;
void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fail++; }
bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }

// Delete a permission-locked key file: the owner can change the DACL, so grant ourselves Full Control and then delete
static void ForceDelete(string path)
{
    var fi = new FileInfo(path);
    if (!fi.Exists) return;
    var sec = fi.GetAccessControl();
    sec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
    fi.SetAccessControl(sec);
    fi.Delete();
}

string outDir = args.Length > 0 ? args[0] : ".";

if (args.Length > 1 && args[1] == "acl")
{
    // Key file ACL check. Locked files left by a previous run are reclaimed via owner rights (WRITE_DAC) and deleted.
    string k = SecretWriter.GenerateKeyBase64();
    string p1 = Path.Combine(outDir, "acl", "granted.key");
    string p2 = Path.Combine(outDir, "acl", "locked.key");
    ForceDelete(p1);
    ForceDelete(p2);

    KeyFileWriter.Write(p1, k, grantCurrentUser: true);
    Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY_FILE", p1);
    var loaded = MasterKey.TryLoad(out var src);
    Check("key file (granted) readable", loaded != null && Convert.ToBase64String(loaded) == k);
    Console.WriteLine("     src: " + src);
    var acl = new FileInfo(p1).GetAccessControl();
    Check("key file inheritance removed", acl.AreAccessRulesProtected);
    foreach (FileSystemAccessRule r in acl.GetAccessRules(true, true, typeof(NTAccount)))
        Console.WriteLine($"     ACL {r.IdentityReference} {r.FileSystemRights} inherited={r.IsInherited}");

    KeyFileWriter.Write(p2, k, grantCurrentUser: false);
    Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY_FILE", p2);
    bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    if (elevated)
    {
        Console.WriteLine("SKIP key file (locked) denied for non-admin — cannot verify while running elevated (as administrator)");
    }
    else
    {
        try { MasterKey.TryLoad(out src); Check("key file (locked) denied for non-admin", false); }
        catch (InvalidOperationException ex) { Check("key file (locked) denied for non-admin", true); Console.WriteLine("     msg: " + ex.Message); }
    }

    ForceDelete(p1);
    ForceDelete(p2);
    Check("locked key files cleaned up", !File.Exists(p1) && !File.Exists(p2));
    Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY_FILE", null);
    return fail;
}

// 1. JSON round trip (special characters)
var doc = new ConfigDocument();
var dev = new ConfigEnvironment("dev");
dev.Variables.Add(new ConfigVariable("MYAPP_DB_OMS", "server=a;pwd=\"p\\w\";\r\n탭\t한글"));
dev.Variables.Add(new ConfigVariable("MYAPP_OTP_MODE", "off"));
doc.Environments.Add(dev);
doc.Environments.Add(new ConfigEnvironment("prod"));
string pretty = ConfigJson.Write(doc, true);
string compact = ConfigJson.Write(doc, false);
var back = ConfigDocument.Parse(pretty);
Check("json roundtrip pretty", ConfigJson.Write(back, false) == compact);
Check("json value preserved", back.FindEnvironment("DEV").Find("myapp_db_oms").Value == dev.Variables[0].Value);
Check("json empty env", back.FindEnvironment("prod").Variables.Count == 0);
Check("json dup env rejected", Throws<FormatException>(() => ConfigDocument.Parse("{\"dev\":{},\"DEV\":{}}")));
Check("json dup var rejected", Throws<FormatException>(() => ConfigDocument.Parse("{\"dev\":{\"A\":\"1\",\"a\":\"2\"}}")));
Check("json nested value rejected", Throws<FormatException>(() => ConfigDocument.Parse("{\"dev\":{\"A\":{}}}")));
Check("json bad env name rejected", Throws<FormatException>(() => ConfigDocument.Parse("{\"de v\":{}}")));
Check("json number to string", ConfigDocument.Parse("{\"dev\":{\"N\":600,\"B\":true,\"Z\":null}}").FindEnvironment("dev").Find("N").Value == "600");
try { ConfigDocument.Parse("{\n \"dev\": {\"A\": \"1\",}\n}"); } catch (FormatException ex) { Console.WriteLine("     msg: " + ex.Message); }

// 2. Encrypt (editor, .NET AesGcm) → decrypt (reader DLL, CNG)
byte[] key = SecretFormat.ParseKey(SecretWriter.GenerateKeyBase64());
string enc = SecretWriter.EncryptVerified(compact, key);
Check("encrypt prefix", enc.StartsWith("enc:v1:"));
Check("decrypt roundtrip", SecretFormat.Decrypt(enc, key) == compact);
Check("nonce random", SecretWriter.Encrypt(compact, key) != SecretWriter.Encrypt(compact, key));
byte[] wrong = SecretFormat.ParseKey(SecretWriter.GenerateKeyBase64());
Check("wrong key rejected", Throws<System.Security.Cryptography.CryptographicException>(() => SecretFormat.Decrypt(enc, wrong)));
var raw = Convert.FromBase64String(enc.Substring(7)); raw[20] ^= 1;
Check("tamper rejected", Throws<System.Security.Cryptography.CryptographicException>(() => SecretFormat.Decrypt("enc:v1:" + Convert.ToBase64String(raw), key)));
Check("bad key length", Throws<FormatException>(() => SecretFormat.ParseKey(Convert.ToBase64String(new byte[16]))));

// 3. Import merge/replace
var file = ConfigDocument.Parse("{\"dev\":{\"MYAPP_OTP_MODE\":\"optional\",\"NEW\":\"x\"},\"stg\":{\"A\":\"1\"}}");
var merged = ImportPlanner.Apply(doc, file, false);
Check("merge keeps existing", merged.FindEnvironment("dev").Find("MYAPP_DB_OMS") != null);
Check("merge overwrites", merged.FindEnvironment("dev").Find("MYAPP_OTP_MODE").Value == "optional");
Check("merge adds env", merged.FindEnvironment("stg") != null && merged.FindEnvironment("prod") != null);
var replaced = ImportPlanner.Apply(doc, file, true);
Check("replace drops", replaced.FindEnvironment("prod") == null && replaced.FindEnvironment("dev").Find("MYAPP_DB_OMS") == null);
Console.WriteLine(ImportPlanner.Describe(doc, replaced, out int changes).TrimEnd());
Check("describe counts", changes > 0);

// 4. Reader: change detection, broken value → keep last known good, recovery
Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY", Convert.ToBase64String(key));
var reader = new SecureConfigReader("MYAPP_CONFIG_TEST") { ErrorRetryInterval = TimeSpan.FromMilliseconds(200) };
Check("reader not configured", !reader.GetState().IsConfigured);
Environment.SetEnvironmentVariable("MYAPP_CONFIG_TEST", enc);
var st = reader.GetState();
Check("reader loaded", st.Snapshot != null && st.Error == null && st.Snapshot.TryGet("dev", "MYAPP_OTP_MODE", out var m) && m == "off");
Check("reader same snapshot", ReferenceEquals(reader.GetState().Snapshot, st.Snapshot));
var doc2 = doc.Clone(); doc2.FindEnvironment("dev").Find("MYAPP_OTP_MODE").Value = "required";
Environment.SetEnvironmentVariable("MYAPP_CONFIG_TEST", SecretWriter.Encrypt(ConfigJson.Write(doc2, false), key));
Check("reader live change", reader.TryGet("dev", "MYAPP_OTP_MODE", out m) && m == "required");
Environment.SetEnvironmentVariable("MYAPP_CONFIG_TEST", SecretWriter.Encrypt(compact, wrong));
st = reader.GetState();
Check("reader broken keeps last good", st.Error != null && st.Snapshot.TryGet("dev", "MYAPP_OTP_MODE", out m) && m == "required");
Console.WriteLine("     err: " + st.Error);
Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY", Convert.ToBase64String(wrong));
Thread.Sleep(300);
st = reader.GetState();
Check("reader retry after key fix", st.Error == null && st.Snapshot.TryGet("dev", "MYAPP_OTP_MODE", out m) && m == "off");

// 4b. SecureAppConfig: runtime environment resolution, aliases, required value checks
{
    Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY", Convert.ToBase64String(key));
    var envDoc = ConfigDocument.Parse("{\"local\":{\"DB\":\"local-db\"},\"prod\":{\"DB\":\"prod-db\",\"MODE\":\"  on  \"}}");
    Environment.SetEnvironmentVariable("APPCFG_TEST_CONFIG", SecretWriter.Encrypt(ConfigJson.Write(envDoc, false), key));

    var a1 = new SecureAppConfig("APPCFG_TEST_CONFIG", environmentVariable: "APPCFG_TEST_ENV", fallbackEnvironment: "local", fallbackReason: "build config (Debug)");
    Check("app: fallback when env var missing", a1.Environment == "local" && a1.EnvironmentSource == "build config (Debug)");
    Check("app: get", a1.Get("DB") == "local-db" && a1.Get("NOPE", "d") == "d");

    Environment.SetEnvironmentVariable("APPCFG_TEST_ENV", "PRD"); // alias for "production", case-insensitive
    var a2 = new SecureAppConfig("APPCFG_TEST_CONFIG", environmentVariable: "APPCFG_TEST_ENV", fallbackEnvironment: "local");
    Check("app: env var wins + alias PRD→prod", a2.Environment == "prod" && a2.EnvironmentSource == "APPCFG_TEST_ENV");
    Check("app: value trimmed", a2.Get("MODE") == "on");
    Check("app: IsEnvironment alias", a2.IsEnvironment("Production") && !a2.IsEnvironment("local"));

    var a3 = new SecureAppConfig("APPCFG_TEST_CONFIG", environment: "Production");
    Check("app: aspnetcore name Production→prod", a3.Environment == "prod" && a3.EnvironmentSource == "explicit");
    Check("app: Staging→stg / Development→dev / Local→local",
        new SecureAppConfig("APPCFG_TEST_CONFIG", environment: "Staging").Environment == "stg" &&
        new SecureAppConfig("APPCFG_TEST_CONFIG", environment: "Development").Environment == "dev" &&
        new SecureAppConfig("APPCFG_TEST_CONFIG", environment: "Local").Environment == "local");

    Environment.SetEnvironmentVariable("APPCFG_TEST_ENV", "qa");
    var a4 = new SecureAppConfig("APPCFG_TEST_CONFIG", environmentVariable: "APPCFG_TEST_ENV", fallbackEnvironment: "local");
    Check("app: unknown env rejected", Throws<InvalidOperationException>(() => { var _ = a4.Environment; }));
    Check("app: unknown env → CheckRequired false", !a4.CheckRequired(new[] { "DB" }, out var e4) && e4.Contains("qa"));
    var a5 = new SecureAppConfig("APPCFG_TEST_CONFIG", environmentVariable: "APPCFG_TEST_ENV", allowedEnvironments: new[] { "qa", "prod" });
    Check("app: custom allowed env", a5.Environment == "qa");
    Environment.SetEnvironmentVariable("APPCFG_TEST_ENV", null);

    Check("app: CheckRequired ok", a2.CheckRequired(new[] { "DB", "MODE" }, out var e2) && e2 == null);
    Check("app: CheckRequired missing var", !a2.CheckRequired(new[] { "DB", "API_KEY" }, out var e3) && e3.Contains("'API_KEY' is missing from the 'prod' environment"));
    var a6 = new SecureAppConfig("APPCFG_TEST_CONFIG", environment: "stg");
    Check("app: missing environment message", Throws<InvalidOperationException>(() => a6.Require("DB")) && !a6.CheckRequired(new[] { "DB" }, out var e6) && e6.Contains("has no 'stg' environment"));
    Check("app: no env source → error", Throws<InvalidOperationException>(() => { var _ = new SecureAppConfig("APPCFG_TEST_CONFIG").Environment; }));
    var rel = new SecureAppConfig("APPCFG_TEST_CONFIG", environmentVariable: "APPCFG_TEST_ENV"); // Release: no fallback
    Check("app: release without env var → not ready", !rel.CheckRequired(new[] { "DB" }, out var er) && er.Contains("Set the environment variable APPCFG_TEST_ENV"));
    Console.WriteLine("     msg: " + er);
    var a7 = new SecureAppConfig("APPCFG_NOT_SET_CONFIG", environment: "prod");
    Check("app: config not set message", !a7.CheckRequired(new[] { "DB" }, out var e7) && e7.Contains("Environment variable APPCFG_NOT_SET_CONFIG is not set"));
    Check("app: source/describe", a2.Source.StartsWith("APPCFG_TEST_CONFIG encrypted config") && a2.DescribeName("DB") == "prod.DB");
    Environment.SetEnvironmentVariable("APPCFG_TEST_CONFIG", null);
}

// 4c. Master key names: the key variable names are reserved and cannot be used as config variables
{
    Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY", Convert.ToBase64String(key));
    var envKey = MasterKey.TryLoad(out var envSrc);
    Check("key: env var read", envKey != null && envKey.SequenceEqual(key) && envSrc.Contains("SECURECONFIG_MASTER_KEY") && MasterKey.ActiveKeyEnvName == "SECURECONFIG_MASTER_KEY");
    Check("key: names reserved", MasterKey.IsReservedName("SECURECONFIG_MASTER_KEY") && MasterKey.IsReservedName("secureconfig_master_key_file") && !MasterKey.IsReservedName("MYAPP_CONFIG"));
}

// 5. Files for the net48 test
Environment.SetEnvironmentVariable("SECURECONFIG_MASTER_KEY", null);
var appDoc = ConfigDocument.Parse("{\"dev\":{\"MYAPP_DB_OMS\":\"dev-oms\",\"MYAPP_DB_WMS\":\"dev-wms\"},\"stg\":{\"MYAPP_DB_OMS\":\"stg-oms\",\"MYAPP_DB_WMS\":\"stg-wms\",\"MYAPP_RATE_LIMIT_PER_MIN\":\"100\"},\"prod\":{\"MYAPP_DB_OMS\":\"prod-oms\"}}");
File.WriteAllText(Path.Combine(outDir, "key.txt"), Convert.ToBase64String(key));
File.WriteAllText(Path.Combine(outDir, "enc1.txt"), SecretWriter.EncryptVerified(ConfigJson.Write(appDoc, false), key));
appDoc.FindEnvironment("stg").Find("MYAPP_RATE_LIMIT_PER_MIN").Value = "250";
File.WriteAllText(Path.Combine(outDir, "enc2.txt"), SecretWriter.EncryptVerified(ConfigJson.Write(appDoc, false), key));
File.WriteAllText(Path.Combine(outDir, "enc_bad.txt"), SecretWriter.Encrypt(ConfigJson.Write(appDoc, false), wrong));

Console.WriteLine(fail == 0 ? "ALL PASS" : fail + " FAILED");
return fail;
