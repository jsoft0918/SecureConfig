# Samples

Three small, copy-paste-ready projects that show how an app uses `SecureConfig.dll`.
They all read the same settings: [`sample-config.json`](sample-config.json) (**fake values only**) stored in the config variable **`SAMPLE_CONFIG`**.

| Project | Runtime | What it shows |
|---|---|---|
| [`ConsoleSample`](ConsoleSample) | .NET 8 / 9 / 10 | Startup check, `Get` / `Require`, type conversion, name constants, **live reload (`--watch`)** |
| [`AspNetCoreSample`](AspNetCoreSample) | .NET 8 / 9 / 10 | DI, typed `AppSecrets` wrapper, **503 fail-fast middleware**, `/health` that never exposes values |
| [`NetFramework48Sample`](NetFramework48Sample) | .NET Framework 4.8 | Static `AppConfig` (Debug = `local`, Release = app pool variable), **`ConfigGuard` for ASP.NET 4.8**, `Global.asax` wiring |

The samples use a `ProjectReference` to the library so they build straight from a clone.
In your own project use the NuGet package or a direct DLL reference — each `.csproj` shows how in a comment.

---

## 1. Setup (once)

This is exactly how you would set up a real server.

1. Build or download `SecureConfigEditor.exe` (`build.bat` → `dist\editor\`) and run it (it asks for administrator rights).
2. **Manage key...** → **Generate new key** → check **"Also grant read access to the current Windows user"** → **Save to key file**
   (the extra read permission lets Visual Studio / `dotnet run` read the key under your own account).
3. **New config variable...** → `SAMPLE_CONFIG`
4. **Import (JSON)...** → select `samples/sample-config.json` → review the preview → **Apply to editor**
5. **Save** (Ctrl+S)

You now have four environments — `local`, `dev`, `stg`, `prod` — encrypted in the system environment variable `SAMPLE_CONFIG`.

> Already have a master key on this PC (for your own app)? Skip step 2 — one key per machine is shared by all config variables.

---

## 2. Run

```powershell
# Console — reads "local" by default
dotnet run --project samples/ConsoleSample

# Console — pick another environment
$env:SAMPLE_ENV = "prod"; dotnet run --project samples/ConsoleSample; Remove-Item Env:SAMPLE_ENV

# Console — live reload demo: change OTP_MODE in the editor, Save, watch the output change
dotnet run --project samples/ConsoleSample -- --watch

# ASP.NET Core — http://localhost:5080 and http://localhost:5080/health
dotnet run --project samples/AspNetCoreSample
dotnet run --project samples/AspNetCoreSample --launch-profile "Staging (stg values)"

# .NET Framework 4.8 — Debug build always reads "local"
dotnet run --project samples/NetFramework48Sample
```

### Things to try

- **Break it on purpose:** in the editor, delete the `PAYMENT_API_KEY` row from `local` and save → the ASP.NET Core sample answers **503** and `/health` explains why — without ever printing a secret value. Add it back and save: the app opens **without a restart**.
- **Wrong environment name:** `$env:SAMPLE_ENV = "prdo"` → the console sample refuses to start instead of silently using the wrong settings.
- **Live reload:** with `--watch` running, change `OTP_MODE` in the editor and save.
- **Undo a mistake:** **Restore from backup...** in the editor brings back the value from before your last save.

---

## 3. Make it yours

1. Copy the pattern that matches your app (`AppSecrets.cs` for ASP.NET Core, `AppConfig.cs` + `ConfigGuard.cs` for ASP.NET 4.8).
2. Rename `SAMPLE_CONFIG` / `SAMPLE_ENV` to your app's names (e.g. `MYAPP_CONFIG` / `MYAPP_ENV`).
3. Write your own plaintext JSON with the same structure as `sample-config.json`, import it in the editor, save.
4. **Never commit your real JSON.** Keep it in a password manager; this repository's `.gitignore` blocks `*.json` by default.

When you're done trying the samples, you can remove `SAMPLE_CONFIG` with:

```powershell
# elevated PowerShell
[Environment]::SetEnvironmentVariable("SAMPLE_CONFIG", $null, "Machine")
```
