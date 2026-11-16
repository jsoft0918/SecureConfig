# Checks that the read-only DLL (netstandard2.0) works on .NET Framework 4.8.
# Must be run with Windows PowerShell 5.1 (= .NET Framework 4.x): powershell.exe -File net48-check.ps1 <fixture-folder> <dll-path>
param([string]$FixtureDir, [string]$DllPath)
$ErrorActionPreference = 'Stop'
$fail = 0
function Check($name, $ok) { if ($ok) { "PASS $name" } else { "FAIL $name"; $script:fail++ } }

"runtime: CLR " + [Environment]::Version
Add-Type -Path $DllPath
$env:SECURECONFIG_MASTER_KEY = (Get-Content "$FixtureDir\key.txt" -Raw).Trim()
$env:NET48_TEST_CONFIG = (Get-Content "$FixtureDir\enc1.txt" -Raw).Trim()

$r = New-Object SecureConfig.SecureConfigReader 'NET48_TEST_CONFIG'
$r.ErrorRetryInterval = [TimeSpan]::FromMilliseconds(100)

Check "decrypt+parse on net48" ($r.Get('stg', 'MYAPP_DB_OMS') -eq 'stg-oms')
Check "get with default" ($r.Get('prod', 'NOPE', 'dflt') -eq 'dflt')
Check "rate limit 100" ($r.Get('stg', 'MYAPP_RATE_LIMIT_PER_MIN') -eq '100')

$env:NET48_TEST_CONFIG = (Get-Content "$FixtureDir\enc2.txt" -Raw).Trim()
Check "live change -> 250" ($r.Get('stg', 'MYAPP_RATE_LIMIT_PER_MIN') -eq '250')

$env:NET48_TEST_CONFIG = (Get-Content "$FixtureDir\enc_bad.txt" -Raw).Trim()
Check "broken value keeps last good" ($r.Get('stg', 'MYAPP_RATE_LIMIT_PER_MIN') -eq '250')
Check "error reported" ($r.GetState().Error -ne $null)
"     error: " + $r.GetState().Error

try { $null = $r.Require('prod', 'MYAPP_DB_WMS'); Check "require throws when missing" $false }
catch { Check "require throws when missing" $true; "     msg: " + $_.Exception.InnerException.Message }

$shared1 = [SecureConfig.SecureConfigReader]::For('NET48_TEST_CONFIG')
$shared2 = [SecureConfig.SecureConfigReader]::For('NET48_TEST_CONFIG')
Check "For() returns shared instance" ([object]::ReferenceEquals($shared1, $shared2))

try { $null = [SecureConfig.SecureConfigReader]::For('bad name'); Check "invalid variable name rejected" $false }
catch { Check "invalid variable name rejected" $true }

# SecureAppConfig (app entry point)
$env:NET48_TEST_CONFIG = (Get-Content "$FixtureDir\enc1.txt" -Raw).Trim()
$app = New-Object SecureConfig.SecureAppConfig 'NET48_TEST_CONFIG', 'NET48_TEST_ENV', 'local', $null, $null, 'build config (Debug)'
Check "app: debug fallback local" ($app.Environment -eq 'local' -and $app.EnvironmentSource -eq 'build config (Debug)')
$env:NET48_TEST_ENV = 'staging'
$app2 = New-Object SecureConfig.SecureAppConfig 'NET48_TEST_CONFIG', 'NET48_TEST_ENV', $null, $null, $null, $null
Check "app: env var staging -> stg" ($app2.Environment -eq 'stg')
Check "app: get via environment" ($app2.Get('MYAPP_DB_OMS') -eq 'stg-oms')
$err = $null
Check "app: check required" ($app2.CheckRequired([string[]]@('MYAPP_DB_OMS','MYAPP_DB_WMS'), [ref]$err))
$env:NET48_TEST_ENV = $null
$rel = New-Object SecureConfig.SecureAppConfig 'NET48_TEST_CONFIG', 'NET48_TEST_ENV', $null, $null, $null, $null
Check "app: release without env -> not ready" (-not $rel.CheckRequired([string[]]@('MYAPP_DB_OMS'), [ref]$err))
"     msg: $err"

if ($fail -eq 0) { "ALL PASS (net48)"; exit 0 } else { "$fail FAILED (net48)"; exit 1 }
