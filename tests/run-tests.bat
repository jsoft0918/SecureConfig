@echo off
rem ============================================================
rem  SecureConfig self test
rem    1) .NET 10 (library net8.0 build = .NET AesGcm path): JSON / encrypt-decrypt / import / reader / SecureAppConfig
rem    2) key file ACL
rem    3) .NET Framework 4.8 (library netstandard2.0 build = Windows CNG path) via Windows PowerShell 5.1
rem ============================================================
setlocal
cd /d "%~dp0.."
set OUT=%~dp0out
if not exist "%OUT%" mkdir "%OUT%"

rem Build the library for ALL targets first: the self test only pulls in the net8.0 build,
rem but step 3 needs the netstandard2.0 DLL.
dotnet build src\SecureConfig\SecureConfig.csproj -c Release -v q --nologo
if %errorlevel% neq 0 goto :failed
dotnet build tests\SecureConfig.SelfTest\SecureConfig.SelfTest.csproj -c Release -v q --nologo
if %errorlevel% neq 0 goto :failed
set EXE=tests\SecureConfig.SelfTest\bin\Release\net10.0-windows\SecureConfig.SelfTest.exe

echo ===== [1] .NET 10
"%EXE%" "%OUT%"
if %errorlevel% neq 0 goto :failed

echo ===== [2] key file ACL
"%EXE%" "%OUT%" acl
if %errorlevel% neq 0 goto :failed

echo ===== [3] .NET Framework 4.8
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0net48-check.ps1" "%OUT%" "%CD%\src\SecureConfig\bin\Release\netstandard2.0\SecureConfig.dll"
if %errorlevel% neq 0 goto :failed

echo.
echo ALL TESTS PASSED
exit /b 0

:failed
echo.
echo *** TESTS FAILED ***
exit /b 1
