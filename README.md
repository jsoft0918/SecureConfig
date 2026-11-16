<div align="center">

# 🔐 SecureConfig

**Get the passwords out of your source code. The server keeps the secrets; your code keeps only the names.**

Encrypt DB connection strings, API keys, and other secrets **per environment (local / dev / stg / prod) with AES-256-GCM**, keep them on the Windows server,<br>
and read them from your app with **a single DLL**: `cfg.Require("DB_MAIN")`.

**A lightweight "AWS Parameter Store" for air-gapped and on-premise Windows / IIS servers.**

[![CI](https://github.com/jsoft0918/SecureConfig/actions/workflows/ci.yml/badge.svg)](https://github.com/jsoft0918/SecureConfig/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
![Windows Server 2016+](https://img.shields.io/badge/Windows%20Server-2016%2B-0078D6)
![.NET Framework 4.6.2+](https://img.shields.io/badge/.NET%20Framework-4.6.2%2B-512BD4)
![.NET 8 | 9 | 10](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)
![Dependencies: 0](https://img.shields.io/badge/dependencies-0-brightgreen)

[Requirements](#-requirements) · [Quick start](#-quick-start-5-minutes) · [Why](#-why-i-built-this) · [Samples](#-samples) · [Code](#-code-examples) · [FAQ](#-faq) · [Sponsor](#-support-the-project)

</div>

---

## ✨ At a glance

```csharp
using SecureConfig;

var cfg = new SecureAppConfig("MYAPP_CONFIG", environmentVariable: "MYAPP_ENV");

string db  = cfg.Require("DB_MAIN");        // DB connection string for the current environment
string otp = cfg.Get("OTP_MODE", "off");    // optional value with a default
```

- Run it on a developer PC → you get the `local` values
- Run it on the production server → you get the `prod` values
- **The code is identical. The only thing that changes is the environment name.**

| Editor (`SecureConfigEditor.exe`) | Your app (`SecureConfig.dll`) |
|---|---|
| Add / edit / delete variables per environment in a grid → **Save** | Reads by name only. **Changes apply on the next call — no restart** |
| Create / rotate the master key (automatic re-encryption on rotation) | Finds the key, decrypts, detects tampering |
| JSON import / export, automatic backup and restore | If a broken value shows up, **keeps running on the last good value** |

---

## 📋 Requirements

| | Supported |
|---|---|
| **Server / PC** | Windows Server **2016, 2019, 2022, 2025** · Windows **10 / 11** (64-bit) |
| **Apps that use the DLL** | .NET Framework **4.6.2 – 4.8.1** (4.7.2 or later recommended) · .NET **8, 9, 10** |
| **Web hosting** | IIS **10** (app pool environment variables) · IIS Express / Kestrel on developer PCs |
| **Editor** | No .NET installation needed (self-contained 64-bit exe). Runs as administrator |
| **Building from source** | .NET SDK 10 on Windows |

> SecureConfig is Windows-only: settings live in a Windows system environment variable and the master key file is protected with Windows ACLs.

---

## 🤔 Why I built this

> I built it because I needed it. Now I can't imagine working without it.

If you run several ASP.NET services on IIS servers inside a closed network, you have probably seen all of these:

- `web.config` holds **DB passwords in plain text**, and that file is **committed to Git**.
- Someone copies the dev server's config to production and **the production app connects to the dev DB**.
- Changing one password means **build → deploy → recycle the app pool**.
- In the cloud you would use AWS Parameter Store or Azure Key Vault — **but there is nothing like that on an air-gapped network.** HashiCorp Vault is far too heavy for a handful of servers.
- And these days **AI coding tools read your entire repository.** A password in your source code is a password that will leak sooner or later.

So I built exactly what was needed — nothing more:

1. Secrets live **completely outside source code and config files**, encrypted on the server.
2. The app **references one DLL** and reads values **by name**.
3. Changes are made in a **GUI editor → Save → applied immediately.** No redeploy, no restart.
4. **No extra servers, no internet connection, no third-party packages.**

It runs every day in production on our in-house services (ASP.NET 4.8 on IIS), and this repository is the polished result of that real-world use.
Once you try it, you will wonder why you didn't have it earlier. 😄

---

## 🚀 Highlights

### 🔒 1. Zero secrets in your source code

No passwords in code, `web.config`, or `appsettings.json`. Even if the whole repository leaks, or an AI assistant reads every file, **there are no secrets to leak.** Only **names** like `"DB_MAIN"` remain in the code.

### 🧩 2. One DLL and you're done

- **Zero dependencies.** One NuGet package, or one DLL file.
- Supports **both** .NET Framework 4.6.2+ (including ASP.NET 4.8 WebForms / MVC / Web API) and .NET 8 / 9 / 10.
- In practice your app uses **one class: `SecureAppConfig`.**

### 🖥️ 3. Developers run locally, servers run as servers

| Where | What decides the environment | Result |
|---|---|---|
| Developer PC (Visual Studio, Debug) | `environment: "local"` | `local` values on your PC |
| Dev / staging server | App pool env var `MYAPP_ENV=dev` / `stg` | `dev` / `stg` values on that server |
| Production server | App pool env var `MYAPP_ENV=prod` | `prod` values on the production server |

Aliases such as `Production` → `prod`, `Staging` → `stg`, `Development` → `dev` are mapped automatically, so you can pass ASP.NET Core's `EnvironmentName` directly.

### ⚡ 4. Save and it's live — no restart

Change a value in the editor and **Save**: running apps use the new value **on their next call**.
Each call only compares the stored ciphertext string (a few microseconds) and decrypts **only when it changed**. Performance is a non-issue.

### 🛡️ 5. Mistakes don't take your service down

- Wrong key, broken JSON, accidental deletion → the app **keeps running on the last good value** and reports the cause via `Error`.
- Fix the key and it **recovers automatically within 10 seconds.**
- If the runtime environment is missing or misspelled (`prdo`), the app **refuses to start**, so it can never silently connect to the wrong database.
- Every save **automatically backs up the previous value**; restore it with one click.

### 🏢 6. Built for air-gapped and on-premise servers

|  | **SecureConfig** | AWS Parameter Store | Azure Key Vault | HashiCorp Vault | Plain `web.config` |
|---|:---:|:---:|:---:|:---:|:---:|
| Works offline / air-gapped | ✅ | ❌ | ❌ | ✅ | ✅ |
| Extra server required | **None** | Cloud | Cloud | Yes (HA cluster) | None |
| Setup & operations | **Run an exe** | Medium | Medium | High | None |
| Cost | **Free (MIT)** | Per request | Per request | Free / paid | Free |
| Encryption | AES-256-GCM | KMS | HSM / software | ✅ | ❌ |
| .NET Framework 4.x | ✅ | SDK needed | SDK needed | Client needed | ✅ |
| Apply changes without restart | ✅ | DIY | DIY | DIY | ❌ |
| GUI editor | ✅ | Web console | Web console | Web UI | Notepad 😅 |

> In the cloud, use your cloud provider's service. For **a handful of on-premise or air-gapped Windows servers**, this is the lightest and most reliable option.

### ✍️ 7. Adding and changing values is trivial

Need a new secret? Editor → pick the environment → type `name / value` in the last row → **Save**. Done.
**Duplicate** a whole environment (`stg` → `prod`) and just edit the values, or **import/export JSON** to migrate or replicate servers.

---

## 🧠 How it works

```
 [plaintext JSON] ──import──▶ [SecureConfigEditor.exe] ──encrypt & save──▶ system env var MYAPP_CONFIG = "enc:v1:..."
 (kept by admins)                    │ create / save key                             │ (registry, HKLM)
                                     ▼                                               ▼
              %ProgramData%\SecureConfig\master.key  ◀──read key──  [your app + SecureConfig.dll]
              (readable only by Administrators, SYSTEM, IIS_IUSRS)    decrypt → use values of "prod"
```

Settings use a two-level **environment → variable** structure, like GitHub Environments:

```json
{
  "local": { "DB_MAIN": "Server=localhost;...", "API_KEY": "dev-key", "OTP_MODE": "off" },
  "stg":   { "DB_MAIN": "Server=stg-db;...",    "API_KEY": "stg-key" },
  "prod":  { "DB_MAIN": "Server=prd-db;...",    "API_KEY": "prd-key", "OTP_MODE": "required" }
}
```

1. The editor encrypts the whole JSON with **AES-256-GCM** and stores it in **one system environment variable**.
2. The decryption key (master key) lives in a **separate, ACL-protected file**. Leaking the environment variable alone reveals nothing.
3. Your app decrypts with the DLL and reads only the values of **its own runtime environment**.

---

## 📦 What's in the box

| Component | Target | Purpose |
|---|---|---|
| **`SecureConfig.dll`** | netstandard2.0 + net8.0 | **Read-only** library your app references. Zero dependencies |
| **`SecureConfigEditor.exe`** | Windows (single exe, no .NET install needed) | Editor: environments & variables, key management, import/export, backup/restore |
| **`samples/`** | Console · ASP.NET Core · .NET Framework 4.8 | Copy-paste-ready usage examples + `sample-config.json` |

> Write operations (encryption, saving, key file creation) exist **only in the editor**. Apps get the read-only DLL, so an app can never corrupt your settings.

---

## 🏁 Quick start (5 minutes)

### 0) Get it

```bash
git clone https://github.com/jsoft0918/SecureConfig.git
cd SecureConfig
```

- Just want the binaries? Download `SecureConfigEditor.exe` and the DLLs from [Releases](https://github.com/jsoft0918/SecureConfig/releases).
- Build it yourself: `build.bat` (requires .NET SDK 10) → everything lands in `dist\`.

### 1) Create a key and settings on your PC

1. Run `SecureConfigEditor.exe` (it asks for administrator rights)
2. **Manage key... → Generate new key → ☑ "Also grant read access to the current Windows user" → Save to key file**
3. **New config variable...** → `SAMPLE_CONFIG`
4. **Import (JSON)...** → choose [`samples/sample-config.json`](samples/sample-config.json) → review the preview → **Apply to editor**
5. **Save** (Ctrl+S)

### 2) Run a sample

```bash
dotnet run --project samples/ConsoleSample
```

You should see the `local` values (masked). Now run it with `-- --watch`, change `OTP_MODE` in the editor, save — and watch the running app pick it up.

### 3) Reference the DLL in your app

```bash
# .NET 8 / 9 / 10 (SDK-style project)
dotnet add package SecureConfig
```

```xml
<!-- .NET Framework 4.x / air-gapped: reference the DLL produced by build.bat -->
<Reference Include="SecureConfig">
  <HintPath>..\lib\SecureConfig\SecureConfig.dll</HintPath>
</Reference>
```

> On an air-gapped network, push `dist\nuget\*.nupkg` to your internal NuGet feed (or a local folder feed).

### 4) Deploy to a server

1. Copy `SecureConfigEditor.exe` to the server → run it → **Manage key... → Generate new key → Save to key file**
2. **New config variable...** `MYAPP_CONFIG` → **Import (JSON)...** your plaintext JSON → **Save**
3. Set the runtime environment on the IIS app pool, then recycle it

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
& $appcmd set config -section:system.applicationHost/applicationPools "/+[name='MyApp'].environmentVariables.[name='MYAPP_ENV',value='prod']" /commit:apphost
& $appcmd recycle apppool /apppool.name:MyApp
```

The app on the production server now reads `prod` values. From now on, changing a value is just **edit → Save** in the editor.

---

## 🧪 Samples

All samples read the same [`samples/sample-config.json`](samples/sample-config.json) (fake values only) after you import it into `SAMPLE_CONFIG`. See [samples/README.md](samples/README.md) for step-by-step setup.

| Sample | Shows |
|---|---|
| [`ConsoleSample`](samples/ConsoleSample) (.NET 8+) | Startup check, `Get` / `Require`, type conversion, name constants, **live reload demo (`--watch`)** |
| [`AspNetCoreSample`](samples/AspNetCoreSample) (.NET 8+) | DI registration, typed `AppSecrets` wrapper, **503 fail-fast middleware**, `/health` endpoint that never exposes values |
| [`NetFramework48Sample`](samples/NetFramework48Sample) (.NET Framework 4.8) | Static `AppConfig` (Debug = local, Release = app pool variable), **`ConfigGuard` (503) for ASP.NET 4.8**, `Global.asax` wiring |

---

## 💻 Code examples

### ASP.NET 4.8 / .NET Framework

```csharp
using SecureConfig;

public static class AppConfig
{
#if DEBUG
    public static readonly SecureAppConfig Config = new SecureAppConfig("MYAPP_CONFIG", environment: "local");
#else
    // Release: IIS app pool env var MYAPP_ENV (dev|stg|prod) is required; without it the app refuses to start
    public static readonly SecureAppConfig Config = new SecureAppConfig("MYAPP_CONFIG", environmentVariable: "MYAPP_ENV");
#endif

    public const string DB_MAIN = "DB_MAIN";
    public static readonly string[] RequiredNames = { DB_MAIN };

    public static string Require(string name) { return Config.Require(name); }
}

// Read values every time you need them, so edits in the editor apply immediately
using (var conn = new SqlConnection(AppConfig.Require(AppConfig.DB_MAIN))) { /* ... */ }
```

### ASP.NET Core / .NET 8+

```csharp
using SecureConfig;

var builder = WebApplication.CreateBuilder(args);
var cfg = new SecureAppConfig("MYAPP_CONFIG", environment: builder.Environment.EnvironmentName);
builder.Services.AddSingleton(cfg);

var app = builder.Build();

// Return 503 until required settings are readable (fix them and it opens without a restart)
var ready = false;
app.Use(async (ctx, next) =>
{
    if (!ready)
    {
        if (cfg.CheckRequired(new[] { "DB_MAIN" }, out var error)) ready = true;
        else { app.Logger.LogError("Config error: {Error}", error); ctx.Response.StatusCode = 503; return; }
    }
    await next();
});

app.MapGet("/", (SecureAppConfig c) => c.Get("OTP_MODE", "off"));
app.Run();
```

On a developer PC, set `"ASPNETCORE_ENVIRONMENT": "Local"` in `launchSettings.json` to read the `local` environment.

### Console / Windows service

```csharp
var cfg = new SecureAppConfig("MYJOB_CONFIG", environmentVariable: "MYJOB_ENV", fallbackEnvironment: "local");
string conn = cfg.Require("DB_MAIN");
```

### Main API

| Member | Description |
|---|---|
| `Get(name, defaultValue = null)` | Value for the current environment, or the default |
| `Require(name)` | Required value. Throws with the **exact cause** (no config / no key / no environment / no variable) |
| `CheckRequired(names, out error)` | Checks all required values at once — for fail-fast (503) |
| `Environment` / `EnvironmentSource` | Current runtime environment and what decided it (for logs) |
| `IsEnvironment("prod")` | Compare the current environment (aliases allowed) |
| `Source` / `Error` / `State` | Where the config came from, latest error, detailed state (for health checks) |

> Error messages contain variable **names** only — never **values**. They are safe to log as-is.

📘 **Server setup, key permissions, backup & recovery, troubleshooting: see the [Developer Guide](docs/DEVELOPER_GUIDE.md).**

---

## 🔐 Security design

| Item | Details |
|---|---|
| Algorithm | **AES-256-GCM** (authenticated encryption — tampered values are rejected) |
| Format | `enc:v1:` + Base64(nonce 12 \| ciphertext \| tag 16) with a fixed associated-data tag |
| Nonce | Fresh random nonce on every save (the same value never produces the same ciphertext) |
| Master key | 32 random bytes per server. ACL: Administrators & SYSTEM full control, IIS_IUSRS read, inheritance removed |
| Separation | Ciphertext (env var) and key (file) live in **different places** — leaking one is not enough |
| Implementation | Windows CNG (bcrypt.dll) / .NET `AesGcm` — **no home-made cryptography** |
| Verify before save | The editor decrypts the result through the real read path and compares before saving |

**Protects against:** leaks via source code, Git, config files, backups, screenshots, and AI tools; tampering with ciphertext.
**Does not protect against:** an attacker with administrator rights on the server, or code execution inside your app process (the app must know the plaintext).

> 🙏 **Your plaintext JSON is the most important secret.** Keep it only in a password manager or similar.
> This repository's `.gitignore` blocks `*.json`, `*.key`, and `backup/` by default to prevent accidents.

To report a vulnerability, see [SECURITY.md](SECURITY.md).

---

## 🏗️ Build & test

```bat
build.bat                               :: dist\ : DLLs (2 targets), NuGet package, editor exe
build.bat ..\MyApp\lib\SecureConfig     :: + copy the DLL into that folder
tests\run-tests.bat                     :: full self-test
```

Requirements: .NET SDK 10 (`global.json`) and Windows PowerShell 5.1 (for the .NET Framework 4.8 checks).

| Stage | What it verifies |
|---|---|
| ① .NET 10 (net8.0 DLL) | JSON parse/write, encryption round trip, wrong key & tampering rejected, import merge/replace, live reload, broken value → last good → auto-recovery, environment resolution & aliases, master key names |
| ② Key file ACL | Inheritance removed, only allowed accounts can read |
| ③ .NET Framework 4.8 (netstandard2.0 DLL) | CNG decryption, live reload, `SecureAppConfig` |

GitHub Actions runs the full suite, builds the samples, and builds `dist` on every push.

---

## 📁 Project layout

```
SecureConfig/
├─ src/
│  ├─ SecureConfig/                read-only library (netstandard2.0 + net8.0)
│  │  ├─ SecureAppConfig.cs        ★ app entry point (environment resolution + reading values)
│  │  ├─ SecureConfigReader.cs     change detection · decryption · last-known-good
│  │  ├─ MasterKey.cs / SecretFormat.cs
│  │  ├─ ConfigDocument.cs …       settings model
│  │  └─ Internal/                 AES-GCM (CNG / .NET), JSON parser, env var helper
│  └─ SecureConfig.Editor/         WinForms editor (.NET 10, single exe)
│     ├─ Core/                     encryption · storage · key file ACL · import planning
│     └─ UI/                       main window (partial classes) · dialogs
├─ samples/                        ConsoleSample · AspNetCoreSample · NetFramework48Sample · sample-config.json
├─ tests/                          self-tests (.NET 10 / key file ACL / .NET Framework 4.8)
└─ docs/DEVELOPER_GUIDE.md         detailed guide
```

---

## ❓ FAQ

**Q. I changed a value but my app doesn't see it.**
Your app probably read it once into a `static readonly` field. **Call `cfg.Get()` / `cfg.Require()` whenever you need the value** — each call costs a few microseconds.

**Q. What about multiple servers (load balancing)?**
Import the same plaintext JSON on each server and save. **Each server can have its own key** — each encrypts with its own.

**Q. What if I lose the master key?**
With the plaintext JSON you can create a new key, import, and save again. That's why backing up the plaintext JSON matters most.

**Q. How do I rotate the key?**
Editor **Manage key... → Generate new key → Save to key file**. All config variables on that server are **re-encrypted automatically** (previous values are backed up).

**Q. Several apps share one server.**
Give each app its own config variable (`ORDER_CONFIG`, `BILLING_CONFIG`). Note the master key is shared per server, so apps are not **cryptographically isolated** from each other.

**Q. Is there a size limit?**
Because of the Windows environment variable limit, up to 32,766 characters after encryption (roughly 24 KB of plaintext JSON). That's hundreds of secrets.

**Q. Why not use a JSON library?**
The DLL goes into .NET Framework 4.x apps, so it keeps **zero dependencies to avoid version conflicts ("DLL hell")**. A minimal JSON parser is built in.

---

## 🗺️ Roadmap

- [ ] `Microsoft.Extensions.Configuration` provider (`builder.Configuration.AddSecureConfig(...)`)
- [ ] Command-line tool for scripted deployments (`SecureConfigEditor.exe /import ...`)
- [ ] Localized editor UI

Ideas and bug reports are welcome in [Issues](https://github.com/jsoft0918/SecureConfig/issues). Pull requests are welcome too!

---

## 💖 Support the project

This is **my first open-source project**. 🎉

I built it for my own work and I'm sharing it in the hope that it helps others with the same problem.
It's **MIT licensed** — free to use in commercial and company projects.

If it helped you remove passwords from your source code, or saved you a late-night redeploy 🙂

- ⭐ **Star the repository** — it helps more than you'd think
- 💖 **Sponsor** — once GitHub Sponsors is enabled, a "Sponsor" button will appear at the top of this repository
- 📣 **Share it** with other developers — especially .NET teams in government, finance, and manufacturing working on closed networks
- 🏢 **Tell us how you use it** by opening an [issue](https://github.com/jsoft0918/SecureConfig/issues) — we would love to feature adopters here

---

<div align="center">

**MIT License** © 2026 jsoft · [LICENSE](LICENSE)

Secrets on the server, clean code in the repo. 🔐

</div>
