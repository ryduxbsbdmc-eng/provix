# Builds Provix test installer and prepares a LAN folder for other PCs on the network.
param(
    [int]$HttpPort = 8765,
    [switch]$StartServer,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$networkDir = Join-Path $repoRoot 'network-test'
$issPath = Join-Path $repoRoot 'setup\Provix.iss'

if (-not (Test-Path $issPath)) {
    throw "Installer script not found: $issPath"
}

$versionLine = Get-Content $issPath | Where-Object { $_ -match '^#define MyAppVersion "' } | Select-Object -First 1
if ($versionLine -notmatch '"([^"]+)"') {
    throw 'Could not read MyAppVersion from setup\Provix.iss'
}

$version = $Matches[1]
$installerName = "Provix-Setup-$version.exe"
$installerPath = Join-Path $repoRoot "installer\$installerName"

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        Write-Host "Building test version $version..." -ForegroundColor Cyan
        & cmd /c build-setup.cmd
        if ($LASTEXITCODE -ne 0) { throw "build-setup.cmd failed with exit code $LASTEXITCODE" }
    }

    if (-not (Test-Path $installerPath)) {
        throw "Installer not found: $installerPath"
    }

    New-Item -ItemType Directory -Force -Path $networkDir | Out-Null
    Copy-Item -Path $installerPath -Destination (Join-Path $networkDir $installerName) -Force

    $versionInfo = @"
Provix $version (test)
Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
"@
    Set-Content -Path (Join-Path $networkDir 'VERSION.txt') -Value $versionInfo -Encoding UTF8

    $readme = @"
Provix — тестовая версия в локальной сети
=========================================

Файл установки: $installerName
Версия: $version

## Установка с другого ПК в сети

### Вариант 1 — общая папка Windows
1. На этом ПК откройте папку: $networkDir
2. ПКМ → Свойства → Доступ → Общий доступ
3. На другом ПК в проводнике: \\ИМЯ-ЭТОГО-ПК\ИмяОбщейПапки
4. Запустите $installerName

### Вариант 2 — мини-сервер загрузки (HTTP)
1. Запустите: scripts\Start-NetworkTestServer.cmd
2. На другом ПК в браузере: http://IP-ЭТОГО-ПК:$HttpPort/$installerName
   (IP смотрите командой ipconfig)

### Вариант 3 — USB / мессенджер
Скопируйте $installerName на флешку или отправьте файл напрямую.

---
Не публикуется на GitHub. Только для тестов в вашей сети.
"@
    Set-Content -Path (Join-Path $networkDir 'README.txt') -Value $readme -Encoding UTF8

    Write-Host ""
    Write-Host "Ready: $networkDir\$installerName" -ForegroundColor Green
    Write-Host "LAN HTTP (if started): http://$((Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.PrefixOrigin -ne 'WellKnown' } | Select-Object -First 1).IPAddress):$HttpPort/$installerName" -ForegroundColor Yellow

    if ($StartServer) {
        Set-Location $networkDir
        Write-Host "Starting HTTP server on port $HttpPort (Ctrl+C to stop)..." -ForegroundColor Cyan
        python -m http.server $HttpPort --bind 0.0.0.0
    }
}
finally {
    Pop-Location
}
