# SecureConfig Developer Guide

Everything you need to use **encrypted, per-environment server settings** in your own project.

- **Servers / PCs:** Windows Server 2016, 2019, 2022, 2025 · Windows 10 / 11 (64-bit)
- **App runtimes:** .NET Framework 4.6.2 – 4.8.1 (4.7.2+ recommended, including ASP.NET 4.8) · .NET 8 / 9 / 10
- **Web hosting:** IIS 10 (app pool environment variables) · IIS Express / Kestrel on developer PCs
- **Deliverables:** `SecureConfig.dll` (read-only, netstandard2.0 + net8.0, zero dependencies) and `SecureConfigEditor.exe` (editor, no .NET install needed)
- In your app code you only need **one class: `SecureAppConfig`.**

Contents: [1. Concepts](#1-concepts--5-minute-summary) · [2. Quick start](#2-quick-start) · [3. Referencing the DLL](#3-referencing-the-dll) · [4. Config variable & runtime environment](#4-config-variable-and-runtime-environment) · [5. Code](#5-code-examples) · [6. Live reload & fail-fast](#6-live-reload-and-fail-fast) · [7. Server setup](#7-server-setup-operators) · [8. Developer PC](#8-developer-pc-setup) · [9. Backup & recovery](#9-backup-recovery-and-key-rotation) · [10. API](#10-api-summary) · [11. Security notes](#11-security-notes) · [12. Limits](#12-limits) · [13. Troubleshooting](#13-troubleshooting)

---

## 1. Concepts — 5-minute summary

Like GitHub Environments, you first create **environments (local / dev / stg / prod)** and put **variables** in them.

```json
{
  "local": { "DB_MAIN": "Server=dev-db;...", "API_KEY": "dev-key", "OTP_MODE": "off" },
  "stg":   { "DB_MAIN": "Server=stg-db;...", "API_KEY": "stg-key" },
  "prod":  { "DB_MAIN": "Server=prd-db;...", "API_KEY": "prd-key", "OTP_MODE": "required" }
}
```

1. The editor **encrypts this whole JSON with AES-256-GCM** and stores it in **one system environment variable** (e.g. `MYAPP_CONFIG`).
2. The app reads it with `SecureAppConfig` from `SecureConfig.dll`, **decrypts and parses** it, and uses the values of **its own runtime environment**.
3. The decryption key (master key) is kept separately in an **ACL-protected file** on each server. Leaking the environment variable alone keeps the values safe.
4. When you save in the editor, **running apps pick up the change on their next call — no restart.**
5. Developers/administrators keep the plaintext JSON somewhere safe. If a server dies, **import the JSON on a new server and save** to recover.

```
 [plaintext JSON] ──import──▶ [SecureConfigEditor.exe] ──encrypt & save──▶ system env var MYAPP_CONFIG = "enc:v1:..."
 (kept by admins)                    │ create / save key                          │ (registry)
                                     ▼                                            ▼
              %ProgramData%\SecureConfig\master.key  ◀──read key──  [app + SecureConfig.dll]
              (readable only by Administrators, SYSTEM, IIS_IUSRS)        decrypt → use "prod" values
```

### Terms

| Term | Meaning | Example |
|---|---|---|
| Config variable | The **system environment variable** holding the ciphertext. One per app | `MYAPP_CONFIG`, `ORDERAPI_CONFIG` |
| Environment | First-level section of the settings | `local`, `dev`, `stg`, `prod` |
| Runtime environment | Which environment this app reads right now. **Decided by the app** | Debug build → `local`, production server → `prod` |
| Variable | A name = value pair inside an environment | `DB_MAIN = Server=...` |
| Master key | One per server (PC), shared by all config variables on it | `C:\ProgramData\SecureConfig\master.key` |

---

## 2. Quick start

1. **Reference the library**: NuGet `SecureConfig`, or copy the DLL into your project → [§3](#3-referencing-the-dll)
2. **Pick names**: config variable `MYAPP_CONFIG`, runtime environment variable `MYAPP_ENV` → [§4](#4-config-variable-and-runtime-environment)
3. **Write code**: one `new SecureAppConfig(...)` + `Require("DB_MAIN")` → [§5](#5-code-examples)
4. **Prepare your PC**: create a key in the editor (grant the current user read access) → create `MYAPP_CONFIG` → add a `local` environment → save → [§8](#8-developer-pc-setup)
5. **Prepare the server**: create a key → import the plaintext JSON → save → set `MYAPP_ENV=prod` on the app pool → [§7](#7-server-setup-operators)

Want to see it working first? Run the projects in [`samples/`](../samples/README.md).

---

## 3. Referencing the DLL

**Option A — NuGet package (recommended for .NET 8+ / SDK-style projects)**

```bash
dotnet add package SecureConfig
```

> On an air-gapped network, publish `dist\nuget\SecureConfig.*.nupkg` to an internal NuGet feed or a local folder feed.

**Option B — direct DLL reference (.NET Framework 4.x, air-gapped networks)**

Build from the source repository and copy the DLL into your project:

```bat
cd SecureConfig
build.bat ..\MyApp\lib\SecureConfig
```

This copies `SecureConfig.dll`, `.xml` (IntelliSense docs), and `.pdb` (debugging). **Commit that `lib` folder** so other developers and the build server use the same DLL.

### .NET Framework (legacy csproj)

```xml
<ItemGroup>
  <Reference Include="SecureConfig">
    <HintPath>..\lib\SecureConfig\SecureConfig.dll</HintPath>
    <Private>True</Private>
  </Reference>
</ItemGroup>
```

- This is a netstandard2.0 DLL, so **.NET Framework 4.7.2 or later is recommended** (4.6.2–4.7.1 need extra netstandard facade files, which are copied automatically).
- If a web project references a class library that uses this DLL, it is copied to the web `bin` automatically. If not, add the same reference to the web project.

### .NET (SDK-style csproj)

```xml
<ItemGroup>
  <Reference Include="SecureConfig">
    <HintPath>..\lib\SecureConfig\net8.0\SecureConfig.dll</HintPath>
  </Reference>
</ItemGroup>
```

---

## 4. Config variable and runtime environment

### 4-1. Config variable name (one per app)

- Rule: **starts with an uppercase letter; uppercase letters, digits and `_`; 2–64 characters** (e.g. `MYAPP_CONFIG`, `ORDERAPI_CONFIG`).
- Multiple apps on one server never mix as long as their names differ.

### 4-2. Runtime environment — the app decides and passes it to `SecureAppConfig`

The DLL doesn't know your build configuration (Debug/Release) or hosting model, so **the app decides which environment to read.** There are three ways; earlier ones win.

| Argument | Meaning | Typical use |
|---|---|---|
| `environment: "value"` | Specify the environment name directly | ASP.NET Core (`EnvironmentName`), `"local"` in Debug builds |
| `environmentVariable: "MYAPP_ENV"` | Name of an **environment variable** the DLL reads (process → system) | ASP.NET 4.8 in production (app pool environment variable) |
| `fallbackEnvironment: "value"` | Used when neither of the above decided | Only when needed |

- **IIS app pool environment variables are passed to the w3wp process automatically**, so on 4.8 you only pass the variable **name** (no IIS API needed).
- Environment aliases are mapped automatically: `Production` / `prd` → `prod`, `Staging` / `stage` → `stg`, `Development` → `dev`, `Local` → `local` (case-insensitive).
- Allowed environments default to `local, dev, stg, prod`; change them with `allowedEnvironments:`.
- **If nothing decides the environment, or the value is unknown, it throws** → the startup check blocks the app (prevents connecting to the wrong DB).
- The runtime environment is fixed once resolved; changing it requires an app (pool) restart. **Variable values apply without a restart.**

### 4-3. Recommended setup

| App | Debug build (developer PC) | Release build (server) |
|---|---|---|
| ASP.NET 4.8 | `environment: "local"` | `environmentVariable: "MYAPP_ENV"` — **app pool variable required**; startup blocked without it |
| ASP.NET Core | `"ASPNETCORE_ENVIRONMENT": "Local"` in `launchSettings.json` | `ASPNETCORE_ENVIRONMENT` on the server (`Production` / `Staging` / `Development`) |
| Console / service | command-line argument or `"local"` | service environment variable `MYAPP_ENV` |

> Why no default (`prod`) in Release: if someone forgets to set the environment on a dev server, the app could silently connect with production settings. **Refusing to start** is the safe behavior.

---

## 5. Code examples

Runnable versions of all of these are in [`samples/`](../samples).

### 5-1. ASP.NET 4.8 / .NET Framework — one static class

```csharp
using SecureConfig;

public static class AppConfig
{
#if DEBUG
    // Debug build: always local
    public static readonly SecureAppConfig Config = new SecureAppConfig("MYAPP_CONFIG", environment: "local");
#else
    // Release build: IIS app pool environment variable MYAPP_ENV (dev|stg|prod) is required
    public static readonly SecureAppConfig Config = new SecureAppConfig("MYAPP_CONFIG", environmentVariable: "MYAPP_ENV");
#endif

    // Variable name constants (prevents typos)
    public const string DB_MAIN = "DB_MAIN";
    public const string API_KEY = "API_KEY";
    public static readonly string[] RequiredNames = { DB_MAIN };

    public static string Environment { get { return Config.Environment; } }
    public static bool IsProd { get { return Config.IsEnvironment("prod"); } }
    public static string Get(string name, string defaultValue = null) { return Config.Get(name, defaultValue); }
    public static string Require(string name) { return Config.Require(name); }
}

// Usage — read every time you need it (so editor changes apply immediately)
using (var conn = new SqlConnection(AppConfig.Require(AppConfig.DB_MAIN))) { ... }
```

Full version: [`samples/NetFramework48Sample/AppConfig.cs`](../samples/NetFramework48Sample/AppConfig.cs).

### 5-2. ASP.NET Core / .NET 8+

```csharp
using SecureConfig;

// Program.cs
var cfg = new SecureAppConfig("MYAPP_CONFIG", environment: builder.Environment.EnvironmentName);
builder.Services.AddSingleton(cfg);

// Usage via DI — call Get every time so live reload works
app.MapGet("/", (SecureAppConfig c) => c.Get("OTP_MODE", "off"));

public class OrderRepository
{
    private readonly SecureAppConfig _cfg;
    public OrderRepository(SecureAppConfig cfg) { _cfg = cfg; }
    public SqlConnection Open() => new SqlConnection(_cfg.Require("DB_MAIN"));
}
```

On a developer PC set `"ASPNETCORE_ENVIRONMENT": "Local"` in `Properties/launchSettings.json` to read `local`.

> If you copy values into `IConfiguration` once, **live reload stops working.** Read from `SecureAppConfig` whenever you need a value.
> A typed wrapper is shown in [`samples/AspNetCoreSample/AppSecrets.cs`](../samples/AspNetCoreSample/AppSecrets.cs).

### 5-3. Console / Windows service

```csharp
var cfg = new SecureAppConfig("MYJOB_CONFIG", environmentVariable: "MYJOB_ENV", fallbackEnvironment: "local");
string conn = cfg.Require("DB_MAIN");
```

The service account must be able to read the key file → [§7-3](#7-3-key-file-permissions-for-non-iis-apps).

### 5-4. Main `SecureAppConfig` members

| Member | Description |
|---|---|
| `Get(name, defaultValue = null)` | Value for the current environment, or the default (trimmed; empty counts as missing) |
| `Require(name)` | Required value. Throws `InvalidOperationException` with the cause |
| `CheckRequired(names, out error)` | Checks required values and the runtime environment at once. For fail-fast |
| `Environment` / `EnvironmentSource` | Current runtime environment and what decided it (for logs) |
| `IsEnvironment("prod")` | Compare the current environment (aliases allowed) |
| `Source` / `Error` / `State` | Config source, latest error, detailed state (logs / health checks) |
| `DescribeName(name)` | Display form for messages: `"prod.DB_MAIN"` |

---

## 6. Live reload and fail-fast

### 6-1. Live reload — don't cache values

- Every `Get` / `Require` call **reads only the ciphertext string and compares it** with the previous one (a few microseconds).
- **Only when it changed** does it load the key, decrypt, parse, and swap the internal snapshot atomically.
- So **storing a value once in a static field breaks live reload.** Read it when you need it.

```csharp
// ✗ read once at startup → editor changes are never picked up
private static readonly int Limit = int.Parse(AppConfig.Get("RATE_LIMIT", "600"));

// ○ read on use → applies on the next request
private static int Limit => int.TryParse(AppConfig.Get("RATE_LIMIT", "600"), out var v) ? v : 600;
```

Exception: values used only once at startup (log paths etc.) naturally need a restart. Document those.

### 6-2. When a new value is broken

| Situation | Behavior |
|---|---|
| Settings break while running (wrong key, JSON error, variable deleted) | **Keeps using the last good values**; `Error` holds the cause |
| Same broken value | Retried at most once every 10 seconds (recovers automatically if only the key file was fixed) |
| Never readable from the start | `Require` / `CheckRequired` report the cause |

### 6-3. Fail-fast — don't serve requests without settings

If required values (DB connection strings etc.) or the runtime environment can't be determined, the app **must not accept requests** — and it should open automatically once fixed, without a restart.

**ASP.NET 4.8** — if `Application_Start` throws, only the first request fails and later requests run on a half-initialized app. So **return 503 instead of throwing.** Ready-to-copy implementation: [`samples/NetFramework48Sample/ConfigGuard.cs`](../samples/NetFramework48Sample/ConfigGuard.cs).

```csharp
// Global.asax.cs
protected void Application_BeginRequest(object sender, EventArgs e)
{
    if (ConfigGuard.BlockIfNotReady(Context)) return;   // 503 until ready
}
```

**ASP.NET Core** — the same idea as middleware: [`samples/AspNetCoreSample/Program.cs`](../samples/AspNetCoreSample/Program.cs).

```csharp
var ready = false;
string[] required = { "DB_MAIN" };
app.Use(async (ctx, next) =>
{
    if (!ready)
    {
        if (cfg.CheckRequired(required, out var error)) ready = true;
        else
        {
            app.Logger.LogError("Returning 503 due to config error: {Error}", error);
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsync("Service is starting up.");
            return;
        }
    }
    await next();
});
```

### 6-4. What to log

```csharp
log.Info($"Started - environment: {cfg.Environment} (from: {cfg.EnvironmentSource}), config: {cfg.Source}");
if (cfg.Error != null) log.Error("Config warning: " + cfg.Error);
```

> Error messages contain variable **names** only, never **values**. They are safe to log as-is.

---

## 7. Server setup (operators)

### 7-1. First time

1. Copy `SecureConfigEditor.exe` to the server and run it (UAC prompt appears; no .NET installation needed)
2. **Manage key... → Generate new key → Save to key file** → creates `C:\ProgramData\SecureConfig\master.key`
   - Permissions: Administrators & SYSTEM full control, **IIS_IUSRS read only** (inheritance removed). IIS app pool identities are members of IIS_IUSRS automatically.
3. **New config variable...** → `MYAPP_CONFIG`
4. **Import (JSON)...** → choose the plaintext JSON → review the merge/replace preview → **Save**
5. Set the runtime environment on the app pool → recycle the app pool

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
& $appcmd set config -section:system.applicationHost/applicationPools "/+[name='MyApp'].environmentVariables.[name='MYAPP_ENV',value='prod']" /commit:apphost
& $appcmd recycle apppool /apppool.name:MyApp
```

### 7-2. Day-to-day changes

Run the editor → select the config variable → select the environment → check **Show / edit values** → edit → **Save**. Apps pick it up on the next request.
The previous value is backed up (still encrypted) in the `backup\` folder next to the editor (**Restore from backup...**).

### 7-3. Key file permissions for non-IIS apps

Windows services, scheduled tasks and other non-IIS accounts are not in IIS_IUSRS, so grant read access explicitly:

```bat
icacls "C:\ProgramData\SecureConfig\master.key" /grant "NT SERVICE\MyService:R"
icacls "C:\ProgramData\SecureConfig\master.key" /grant "DOMAIN\svc_account:R"
```

> Saving (rotating) the key in the editor resets the ACL to the defaults, so run these commands again afterwards.

### 7-4. Moving the key file (optional)

Set the system environment variable `SECURECONFIG_MASTER_KEY_FILE` to a path and both the editor and apps use that file.

---

## 8. Developer PC setup

Visual Studio / IIS Express run under **your own account**, not as administrator, so your account needs read access to the key file.

1. Run `SecureConfigEditor.exe`
2. **Manage key... → Generate new key → check "Also grant read access to the current Windows user" → Save to key file**
3. **New config variable...** → `MYAPP_CONFIG` → add environment `local` → enter development values → **Save**
4. Run your app in Debug from Visual Studio (even if it's already running — values are read from the registry directly, so no restart)

Put **only `local` values** on developer PCs. There is no reason for production values to be there.

Alternative: instead of a key file, put the key (Base64) in the **user environment variable** `SECURECONFIG_MASTER_KEY` (read as a process environment variable; takes precedence over the key file). Don't do this on servers.

---

## 9. Backup, recovery and key rotation

| Task | How |
|---|---|
| Backup | Editor **Export → All environments** → plaintext JSON. Keep it **in a password manager or similar** |
| Server failure → new server | Copy the editor → create & save a key → new config variable → import JSON → save → set the runtime environment |
| Multiple servers | Import the same JSON on each server. **Keys may differ per server** (each encrypts with its own) |
| Key rotation | Manage key → generate → save → **all config variables on that server are re-encrypted** with the new key (previous values backed up) |
| Undo a bad save | **Restore from backup...** → preview → save |

> With the plaintext JSON you can recover even if you lose the key. **The plaintext JSON is your most important secret.**

---

## 10. API summary

| Type | Purpose |
|---|---|
| **`SecureAppConfig`** | **Entry point for apps** (see §5-4) |
| `SecureConfigReader` | Low-level reader for when you pass the environment yourself: `SecureConfigReader.For("MYAPP_CONFIG").Get("prod", "DB_MAIN")` |
| `ConfigState` | Read result (`IsConfigured`, `Snapshot`, `Error`) |
| `ConfigSnapshot` | Read-only copy of the decrypted settings (`TryGet`, `GetEnvironment`, `EnvironmentNames`, `KeySource`) |
| `ConfigDocument` / `ConfigEnvironment` / `ConfigVariable` | Parsed JSON model (used by the editor) |
| `MasterKey` | Key lookup (`TryLoad`, `ResolveKeyFilePath`) |
| `SecretFormat` | Ciphertext format constants and decryption |

All public members have XML documentation, so IntelliSense shows the details.

---

## 11. Security notes

**Protects against**
- Value leaks via `applicationHost.config`, the registry, backup files, screenshots, accidentally shared files, source code and AI tools
- Tampering with ciphertext (detected by the GCM authentication tag → rejected)

**Does not protect against**
- Compromise of server administrator rights, or code execution inside the app process (the app must know the plaintext)
- Other IIS apps on the same server: the master key is shared per server, so apps are not isolated from each other

**Must do**
- Keep plaintext JSON and exported files only in safe places; delete them after use
- Don't widen key file permissions (no Everyone / Users)
- Only `local` values on developer PCs

---

## 12. Limits

| Item | Details |
|---|---|
| Platform | Windows only: Windows Server 2016+ / Windows 10+ (system environment variable, Windows ACLs, IIS app pools) |
| Size | Up to 32,766 characters after encryption (about 24 KB of plaintext JSON). The editor checks before saving |
| Structure | Two levels only: environment → variable. Values are strings (numbers / true / false are read as strings) |
| Names | Environment: letters, digits, `_`, `-`, 1–32 chars. Variable: no whitespace, `=`, or control characters. Case-insensitive |
| When changes apply | Values: next call (restart needed if the app caches them). Runtime environment: app restart |

---

## 13. Troubleshooting

| Symptom (error message) | Cause | Fix |
|---|---|---|
| `The runtime environment is not set. Set the environment variable MYAPP_ENV …` | Release build without an app pool environment variable | Set the app pool variable and recycle the app pool |
| `Unknown runtime environment 'qa' (from MYAPP_ENV) …` | Typo or an environment that isn't allowed | Use `local/dev/stg/prod` (or `allowedEnvironments`) |
| `Environment variable MYAPP_CONFIG is not set. Save the settings with the editor.` | Settings were never saved on this PC/server | Save with the editor |
| `Master key not found (key file: …)` | No key file | Create/save one in the editor's key management |
| `No permission to read the master key file: … (running as: …)` | The app account can't read the key | Dev PC: check "grant current user read access" and save the key again / services: §7-3 `icacls` |
| `Decryption failed: the master key is different or the value was tampered with.` | Ciphertext copied from another server, or a variable missed during key rotation | Import the plaintext JSON on this server and save again |
| `MYAPP_CONFIG has no 'prod' environment …` | Runtime environment and settings don't match | Add the environment in the editor, or check the runtime environment value |
| `'DB_MAIN' is missing from the 'prod' environment in MYAPP_CONFIG …` | Missing variable | Add it in the editor |
| Changed in the editor but the app doesn't see it | The app caches the value in a static field | See §6-1 — read on use |
| Permission error when saving in the editor | Not running as administrator | Run the editor as administrator |
