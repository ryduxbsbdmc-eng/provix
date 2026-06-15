param(
    [Parameter(Mandatory = $true)]
    [string]$PackageVersion,

    [string]$ReleaseTag = "",
    [string]$InstallerPath = "",
    [string]$InstallerUrl = "",
    [string]$OutputDir = "",
    [string]$Repository = "ryduxbsbdmc-eng/provix",
    [string]$TemplateDir = "",
    [switch]$Validate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($TemplateDir)) {
    $TemplateDir = Join-Path $repoRoot "winget\template"
}

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    $ReleaseTag = "v$PackageVersion"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "winget\Provix.Provix\$PackageVersion"
}

if (-not (Test-Path $TemplateDir)) {
    throw "Template directory not found: $TemplateDir"
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $localInstaller = Join-Path $repoRoot "installer\Provix-Setup-$PackageVersion.exe"
    if (Test-Path $localInstaller) {
        $InstallerPath = $localInstaller
    }
}

if ([string]::IsNullOrWhiteSpace($InstallerUrl)) {
    if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
        throw "Provide -InstallerPath or -InstallerUrl, or build installer\Provix-Setup-$PackageVersion.exe first."
    }

    $InstallerUrl = "https://github.com/$Repository/releases/download/$ReleaseTag/Provix-Setup-$PackageVersion.exe"
}

$sha256 = ""
if (-not [string]::IsNullOrWhiteSpace($InstallerPath)) {
    if (-not (Test-Path $InstallerPath)) {
        throw "Installer not found: $InstallerPath"
    }

    $sha256 = (Get-FileHash -Path $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
elseif ([string]::IsNullOrWhiteSpace($InstallerUrl)) {
    throw "Installer URL or path is required."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$replacements = @{
    "__PACKAGE_VERSION__"  = $PackageVersion
    "__RELEASE_TAG__"      = $ReleaseTag
    "__INSTALLER_URL__"    = $InstallerUrl
    "__INSTALLER_SHA256__" = $sha256
}

Get-ChildItem -Path $TemplateDir -Filter "*.yaml" | ForEach-Object {
    $content = Get-Content -Path $_.FullName -Raw -Encoding UTF8
    foreach ($pair in $replacements.GetEnumerator()) {
        if (-not [string]::IsNullOrWhiteSpace($pair.Value)) {
            $content = $content -replace [regex]::Escape($pair.Key), $pair.Value
        }
    }

    $target = Join-Path $OutputDir $_.Name
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($target, $content, $utf8NoBom)
}

Write-Host "WinGet manifests written to: $OutputDir"
if ($sha256) {
    Write-Host "Installer SHA256: $sha256"
}

if ($Validate) {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($winget) {
        & winget.exe validate --manifest $OutputDir --disable-interactivity
        if ($LASTEXITCODE -ne 0) {
            throw "winget validate failed with exit code $LASTEXITCODE"
        }

        Write-Host "Manifest validation succeeded."
        return
    }

    Write-Warning "winget CLI not found; skipping manifest validation."
}
