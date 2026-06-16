# Provix for Android

Native Android port of Provix file manager (Kotlin + Jetpack Compose).

**Location:** this folder lives inside the main [Provix](../) repository and is intentionally isolated from the WPF/.NET Windows code. Shared assets only: `Locales/`, `Themes/Packs/`, `IconPacks/` (copied into `app/src/main/assets/`).

## Requirements

- Android Studio Ladybug or newer
- JDK 17 (bundled with Android Studio)
- Android SDK Platform **35** + Build-Tools + Platform-Tools

## First-time setup (Windows)

1. Install [Android Studio](https://developer.android.com/studio).
2. Open **SDK Manager** and install **Android 15 (API 35)** and **Android SDK Build-Tools**.
3. From the repo root, generate `local.properties`:

```powershell
cd provix
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Setup-AndroidSdk.ps1
```

Or manually copy `local.properties.example` to `local.properties` and set your SDK path.

## Build

```powershell
cd provix-android
.\gradlew.bat assembleDebug
```

Release AAB:

```bash
./gradlew bundleRelease
```

## Features

- Dual-pane file browser with tabs, history, bookmarks
- Copy / move / delete / rename / new folder
- Search by name and grep content
- File preview (text, images)
- Archives: zip, rar, 7z
- Git status (JGit)
- Folder compare
- AI automation (OpenRouter / Ollama / LM Studio)
- Built-in shell + Termux bridge
- Themes, locales (en-US, ru-RU, uk-UA)
- Support author tab

## Permissions

File manager apps require broad storage access on Android 11+. Provix requests `MANAGE_EXTERNAL_STORAGE` after launch; grant **All files access** in system settings.

## Version

Synced with Windows Provix **1.3.5** (`versionCode` 135).

## Play Store

See `app/src/main/assets/privacy_policy.txt` for store listing compliance.
