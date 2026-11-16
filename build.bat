@echo off
rem ============================================================
rem  SecureConfig build
rem    dist\lib\SecureConfig.dll            read-only DLL, netstandard2.0 (.NET Framework 4.6.2+)
rem    dist\lib\net8.0\SecureConfig.dll     read-only DLL, net8.0 (.NET 8 / 9 / 10)
rem    dist\nuget\SecureConfig.*.nupkg      NuGet package (both targets)
rem    dist\editor\SecureConfigEditor.exe   editor (portable single exe, no .NET install needed)
rem
rem  Usage: build.bat [copy-target-folder]
rem    e.g. build.bat ..\MyApp\lib\SecureConfig   (copies dist\lib\* files into that folder)
rem ============================================================
setlocal
cd /d "%~dp0"

rem clean build output, but keep dist\editor\backup (editor's automatic backups of saved settings)
for %%d in (dist\lib dist\nuget dist\tools) do if exist %%d rmdir /s /q %%d
if exist dist\editor del /q dist\editor\*.* 2>nul
mkdir dist\lib\net8.0 dist\nuget 2>nul
if not exist dist\editor mkdir dist\editor

set LIBOUT=src\SecureConfig\bin\Release

echo [1/2] read-only library (netstandard2.0 + net8.0) + NuGet package
dotnet pack src\SecureConfig\SecureConfig.csproj -c Release -v q --nologo -o dist\nuget
if %errorlevel% neq 0 goto :failed
for %%e in (dll pdb xml) do copy /y %LIBOUT%\netstandard2.0\SecureConfig.%%e dist\lib\ >nul
for %%e in (dll pdb xml) do copy /y %LIBOUT%\net8.0\SecureConfig.%%e dist\lib\net8.0\ >nul

echo [2/2] editor
dotnet publish src\SecureConfig.Editor\SecureConfig.Editor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -v q --nologo -o dist\editor
if %errorlevel% neq 0 goto :failed
del /q dist\editor\*.xml 2>nul

if not "%~1"=="" (
  if not exist "%~1" mkdir "%~1"
  copy /y dist\lib\*.* "%~1\" >nul
  echo copied library to %~1
)

echo.
echo Build OK
dir /s /b dist\lib dist\nuget dist\editor\*.exe
exit /b 0

:failed
echo.
echo *** BUILD FAILED ***
exit /b 1
