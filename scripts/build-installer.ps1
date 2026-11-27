param(
    [switch]$SkipChecks
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build.ps1") -SkipChecks:$SkipChecks

$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
)

$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) {
        $Iscc = $cmd.Source
    }
}

if (-not $Iscc) {
    throw "Inno Setup 6 (ISCC.exe) not found. Install it with: winget install --id JRSoftware.InnoSetup -e"
}

$AppVersion = "1.0.0"
$AssemblyInfoPath = Join-Path $Root "CerberusService\AssemblyInfo.cs"
if (Test-Path $AssemblyInfoPath) {
    $assemblyInfo = Get-Content $AssemblyInfoPath -Raw
    if ($assemblyInfo -match 'AssemblyVersion\("(\d+\.\d+\.\d+)(?:\.\d+)?"\)') {
        $AppVersion = $Matches[1]
    }
}

$IssPath = Join-Path $Root "installer\CerberusService.iss"
Write-Host "Building installer v$AppVersion via $Iscc ..."
& $Iscc "/DMyAppVersion=$AppVersion" $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup build failed with exit code $LASTEXITCODE"
}

$SetupExe = Join-Path $Root "build\CerberusServiceSetup.exe"
Write-Host "Installer created: $SetupExe" -ForegroundColor Green
