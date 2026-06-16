# Marks provix-android as trusted in Android Studio (fixes Safe Mode).
# Close Android Studio completely before running this script.

$ErrorActionPreference = "Stop"

$projectPath = "C:\Users\rydux\OneDrive\Desktop\provix\provix-android"
$trustFile = Join-Path $env:APPDATA "Google\AndroidStudio2026.1.1\options\trusted-paths.xml"

$studio = Get-Process -Name "studio64" -ErrorAction SilentlyContinue
if ($studio) {
    Write-Host "Close Android Studio first, then run this script again." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $trustFile)) {
    Write-Host "Trusted paths file not found: $trustFile" -ForegroundColor Red
    exit 1
}

$content = @"
<application>
  <component name="Trusted.Paths">
    <option name="TRUSTED_PROJECT_PATHS">
      <map>
        <entry key="$projectPath" value="true" />
        <entry key="C:\Users\rydux\OneDrive\Desktop\provix" value="true" />
      </map>
    </option>
  </component>
  <component name="Trusted.Paths.Settings">
    <option name="TRUSTED_PATHS">
      <list>
        <option value="$projectPath" />
        <option value="C:\Users\rydux\OneDrive\Desktop\provix" />
      </list>
    </option>
  </component>
</application>
"@

[System.IO.File]::WriteAllText($trustFile, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated: $trustFile" -ForegroundColor Green
Write-Host ""
Write-Host "Now open Android Studio -> Open -> $projectPath"
