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
$forkApi = "repos/$ForkOwner/winget-pkgs"
$upstreamApi = "repos/$UpstreamRepo"

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
    Write-Host "Updating manifest files on branch $branch..."
}
else {
    Write-Host "Reading fork master commit..."
    $baseSha = gh api "$forkApi/git/ref/heads/master" --jq .object.sha
    Write-Host "Fork base commit: $baseSha"

    Write-Host "Creating branch $branch on fork..."
    $branchRef = "heads/$branch"
    $null = gh api "$forkApi/git/ref/$branchRef" 2>$null
    if ($LASTEXITCODE -eq 0) {
        $refPayload = @{ sha = $baseSha; force = $true } | ConvertTo-Json -Compress
        $refPath = Join-Path $env:TEMP "winget-ref-$([Guid]::NewGuid().ToString('N')).json"
        try {
            [System.IO.File]::WriteAllText($refPath, $refPayload, [System.Text.UTF8Encoding]::new($false))
            gh api "$forkApi/git/refs/$branchRef" -X PATCH --input $refPath | Out-Null
        }
        finally {
            if (Test-Path $refPath) { Remove-Item -Force $refPath }
        }
    }
    else {
        gh api "$forkApi/git/refs" -f ref="refs/heads/$branch" -f sha=$baseSha | Out-Null
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create branch $branch on fork."
    }
}

$commitMessage = "Add Provix.Provix version $PackageVersion"
Get-ChildItem -Path $ManifestDir -Filter "*.yaml" | ForEach-Object {
    $relativePath = "$targetRel/$($_.Name)"
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    $encoded = [Convert]::ToBase64String($bytes)

    $payload = @{
        message = $commitMessage
        content = $encoded
        branch  = $branch
    }

    $existingFile = gh api "$forkApi/contents/$relativePath?ref=$branch" --jq .sha 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existingFile)) {
        $payload.sha = $existingFile
    }

    $payloadJson = $payload | ConvertTo-Json -Compress

    $payloadPath = Join-Path $env:TEMP "winget-content-$([Guid]::NewGuid().ToString('N')).json"
    try {
        [System.IO.File]::WriteAllText($payloadPath, $payloadJson, [System.Text.UTF8Encoding]::new($false))
        gh api "$forkApi/contents/$relativePath" -X PUT --input $payloadPath | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to upload $relativePath."
        }
        Write-Host "Uploaded $relativePath"
    }
    finally {
        if (Test-Path $payloadPath) { Remove-Item -Force $payloadPath }
    }
}

Write-Host "Creating pull request..."
if ($existingPr) {
    Write-Host "WinGet PR updated: $existingPr"
    return
}

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
    throw "Failed to create pull request."
}

Write-Host "WinGet PR created: $prUrl"
