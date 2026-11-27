param(
    [switch]$SkipChecks,
    [switch]$SkipWebExport
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$BuildDir = Join-Path $Root "build"

$LocalDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
$DotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (Test-Path $LocalDotnet) {
    $Dotnet = $LocalDotnet
} elseif ($DotnetCmd) {
    $Dotnet = $DotnetCmd.Source
} else {
    throw "dotnet CLI not found on PATH or at $LocalDotnet"
}

New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null

$ExpoDir = Join-Path $Root "CerberusExpo"
$WebUiDir = Join-Path $Root "CerberusService\WebUI"
if (-not $SkipWebExport -and (Test-Path $ExpoDir)) {
    $NpmCmd = Get-Command npm -ErrorAction SilentlyContinue
    if ($NpmCmd) {
        Write-Host "Exporting Expo Web UI..."
        Push-Location $ExpoDir
        try {
            if (-not (Test-Path (Join-Path $ExpoDir "node_modules"))) {
                Write-Host "Installing CerberusExpo npm dependencies..."
                & $NpmCmd.Source ci
                if ($LASTEXITCODE -ne 0) {
                    throw "npm ci failed with exit code $LASTEXITCODE"
                }
            }
            $LocalExpo = Join-Path $ExpoDir "node_modules\.bin\expo.cmd"
            if (Test-Path $LocalExpo) {
                & $LocalExpo export --platform web
            } else {
                & $NpmCmd.Source exec --yes -- expo export --platform web
            }
            if ($LASTEXITCODE -ne 0) {
                throw "Expo web export failed with exit code $LASTEXITCODE"
            }
        } finally {
            Pop-Location
        }
        $DistDir = Join-Path $ExpoDir "dist"
        if (Test-Path $WebUiDir) { Remove-Item -Recurse -Force $WebUiDir }
        Copy-Item -Recurse -Force $DistDir $WebUiDir
    } else {
        Write-Host "npm not found; using existing CerberusService\WebUI bundle."
    }
}

$AgentProj = Join-Path $Root "CerberusService\CerberusService.csproj"
$AgentExe = Join-Path $BuildDir "CerberusService.exe"
Write-Host "Publishing $AgentExe ..."
& $Dotnet publish $AgentProj `
    -c Release `
    -r win-x64 `
    --no-self-contained `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -o $BuildDir `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "CerberusService build failed with exit code $LASTEXITCODE"
}

if ($SkipChecks) {
    Write-Host "Build succeeded (checks skipped)."
    return
}

$TestProj = Join-Path $Root "CerberusService.Tests\CerberusService.Tests.csproj"
$TestOut = Join-Path $BuildDir "tests"
Write-Host "Building RoomControllerTests ..."
& $Dotnet build $TestProj -c Release -o $TestOut --nologo
if ($LASTEXITCODE -ne 0) {
    throw "RoomControllerTests build failed with exit code $LASTEXITCODE"
}

$IntegrationTestProj = Join-Path $Root "CerberusService.Tests\ConsoleEnvironmentIntegrationTest.csproj"
$IntegrationOut = Join-Path $BuildDir "integration-tests"
Write-Host "Building ConsoleEnvironmentIntegrationTest ..."
& $Dotnet build $IntegrationTestProj -c Release -o $IntegrationOut --nologo
if ($LASTEXITCODE -ne 0) {
    throw "ConsoleEnvironmentIntegrationTest build failed with exit code $LASTEXITCODE"
}

Write-Host "Running tests..."
$TestExe = Join-Path $TestOut "RoomControllerTests.exe"
& $TestExe
if ($LASTEXITCODE -ne 0) {
    throw "Unit tests failed!"
}

Write-Host "Build and tests succeeded."
