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

# Classic PAT exposes scopes in response headers; fine-grained tokens do not.
try {
    $scopeResponse = Invoke-WebRequest `
        -Uri "https://api.github.com/user" `
        -Headers @{
            Authorization = "Bearer $token"
            "User-Agent"  = "provix-winget"
        } `
        -Method Get
    $scopes = $scopeResponse.Headers['X-OAuth-Scopes']
    if ($scopes) {
        Write-Host "Token scopes: $scopes"
        if ($scopes -notmatch 'public_repo|\brepo\b') {
            throw "WINGET_TOKEN must be a classic PAT with public_repo scope."
        }
    }
    else {
        Write-Host "Token scopes header missing (likely fine-grained token)."
        Write-Host "winget submission requires a classic PAT with public_repo scope."
    }
}
catch {
    if ($_.Exception.Message -match 'public_repo') { throw }
}

Write-Host "Checking push access to fork $ForkOwner/winget-pkgs..."
try {
    Invoke-RestMethod `
        -Uri "https://api.github.com/repos/$ForkOwner/winget-pkgs" `
        -Headers @{
            Authorization = "Bearer $token"
            "User-Agent"  = "provix-winget"
        } | Out-Null
}
catch {
    throw "Cannot access fork $ForkOwner/winget-pkgs with WINGET_TOKEN. Use a classic PAT with public_repo scope."
}

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

Write-Host "Pushing branch to fork..."
git push --force fork "${branch}:${branch}"
if ($LASTEXITCODE -ne 0) {
    throw @"
Failed to push branch to https://github.com/$ForkOwner/winget-pkgs.

Your WINGET_TOKEN cannot write to the fork. Fix:
1. Delete current WINGET_TOKEN secret
2. Create a classic PAT: https://github.com/settings/tokens
3. Enable ONLY the public_repo checkbox
4. Save as repository secret WINGET_TOKEN
5. Re-run Publish to WinGet workflow
"@
}

Write-Host "Creating pull request..."
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

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($prUrl)) {
    throw "Failed to create pull request. Ensure WINGET_TOKEN is a classic PAT with public_repo scope."
}

Write-Host "WinGet PR created: $prUrl"
