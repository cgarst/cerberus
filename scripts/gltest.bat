@echo off
setlocal

set "ROOT=%~dp0.."
set "BUILD_DIR=%ROOT%\build"
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo csc.exe not found at "%CSC%" >&2
    exit /b 1
)

if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"

taskkill /F /IM gltest.exe >nul 2>&1

echo Building "%BUILD_DIR%\gltest.exe" ...
"%CSC%" /nologo /target:winexe /out:"%BUILD_DIR%\gltest.exe" "%~dp0gltest.cs"
if errorlevel 1 exit /b %errorlevel%

echo Starting "%BUILD_DIR%\gltest.exe" ...
start "" "%BUILD_DIR%\gltest.exe"
