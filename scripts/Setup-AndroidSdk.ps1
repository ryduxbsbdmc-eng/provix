# Writes provix-android/local.properties with sdk.dir for Gradle.
# Run after installing Android Studio (SDK Manager -> Android 15 / API 35).

param(
    [string]$SdkPath = "",
    [string]$ProjectRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Join-Path (Split-Path -Parent $PSScriptRoot) "provix-android"
}

$localProps = Join-Path $ProjectRoot "local.properties"

function Find-AndroidSdk {
    param([string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $expanded = [Environment]::ExpandEnvironmentVariables($candidate)
        if (Test-Path (Join-Path $expanded "platform-tools")) {
            return (Resolve-Path $expanded).Path
        }
    }
    return $null
}

$candidates = @(
    $SdkPath,
    $env:ANDROID_HOME,
    $env:ANDROID_SDK_ROOT,
    "$env:LOCALAPPDATA\Android\Sdk",
    "$env:USERPROFILE\AppData\Local\Android\Sdk",
    "C:\Android\Sdk"
)

$sdk = Find-AndroidSdk -Candidates $candidates

if (-not $sdk) {
    Write-Host ""
    Write-Host "Android SDK not found." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Install Android Studio, then in SDK Manager install:"
    Write-Host "  - Android SDK Platform 35"
    Write-Host "  - Android SDK Build-Tools"
    Write-Host "  - Android SDK Platform-Tools"
    Write-Host ""
    Write-Host "Default SDK path after install:"
    Write-Host "  $env:LOCALAPPDATA\Android\Sdk"
    Write-Host ""
    Write-Host "Then run:"
    Write-Host "  .\scripts\Setup-AndroidSdk.ps1"
    Write-Host ""
    Write-Host "Or pass a custom path:"
    Write-Host "  .\scripts\Setup-AndroidSdk.ps1 -SdkPath 'D:\Android\Sdk'"
    exit 1
}

$escaped = $sdk -replace '\\', '/'
$content = "sdk.dir=$escaped`n"
[System.IO.File]::WriteAllText($localProps, $content, [System.Text.UTF8Encoding]::new($false))

Write-Host "Created $localProps"
Write-Host "sdk.dir=$escaped"
Write-Host ""
Write-Host "Next:"
Write-Host "  cd provix-android"
Write-Host "  .\gradlew.bat assembleDebug"
