param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestDir,

    [Parameter(Mandatory = $true)]
    [string]$PackageVersion,

    [string]$ForkOwner = "ryduxbsbdmc-eng",
    [string]$UpstreamRepo = "microsoft/winget-pkgs"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$token = $env:WINGET_CREATE_GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "WINGET_CREATE_GITHUB_TOKEN is not set."
}

if (-not (Test-Path $ManifestDir)) {
    throw "Manifest directory not found: $ManifestDir"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($ManifestDir)) {
    $ManifestDir = Join-Path $repoRoot $ManifestDir
}

$env:GH_TOKEN = $token
$branch = "Provix.Provix-$PackageVersion"
$targetRel = "manifests/p/Provix/Provix/$PackageVersion"
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "winget-pkgs-pr"
}
else {
    $workDir = Join-Path $env:RUNNER_TEMP "winget-pkgs-pr"
}

if (Test-Path $workDir) {
    Remove-Item -Recurse -Force $workDir
}

Write-Host "Validating GitHub token..."
$user = Invoke-RestMethod `
    -Uri "https://api.github.com/user" `
    -Headers @{
        Authorization = "Bearer $token"
        "User-Agent"  = "provix-winget"
    }
Write-Host "Authenticated as $($user.login)"

$existingPr = gh pr list `
    --repo $UpstreamRepo `
    --head "${ForkOwner}:${branch}" `
    --state open `
    --json url `
    --jq '.[0].url'
if ($existingPr) {
    Write-Host "Open PR already exists: $existingPr"
    return
}

Write-Host "Cloning $UpstreamRepo (sparse)..."
git clone `
    --filter=blob:none `
    --sparse `
    --depth 1 `
    --branch master `
    "https://x-access-token:${token}@github.com/$UpstreamRepo.git" `
    $workDir
Set-Location $workDir

git sparse-checkout set "manifests/p/Provix/Provix"
git checkout master

git remote add fork "https://x-access-token:${token}@github.com/$ForkOwner/winget-pkgs.git"
git checkout -b $branch

New-Item -ItemType Directory -Force -Path $targetRel | Out-Null
Get-ChildItem -Path $ManifestDir -Filter "*.yaml" | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $targetRel $_.Name) -Force
}

git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git add $targetRel

if (git diff --cached --quiet) {
    throw "No manifest changes to commit."
}

git commit -m "Add Provix.Provix version $PackageVersion"
git push --force fork "${branch}:${branch}"

$prUrl = gh pr create `
    --repo $UpstreamRepo `
    --head "${ForkOwner}:${branch}" `
    --base master `
    --title "Provix.Provix $PackageVersion" `
    --body @"
Automated manifest submission for Provix $PackageVersion.

- PackageIdentifier: Provix.Provix
- Installer: Inno Setup (per-user)
- Release: https://github.com/ryduxbsbdmc-eng/provix/releases/tag/v$PackageVersion
"@

Write-Host "WinGet PR created: $prUrl"
