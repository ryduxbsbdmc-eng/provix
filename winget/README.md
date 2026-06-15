# WinGet packaging for Provix

## Requirements checklist

| Requirement | Status |
|-------------|--------|
| x64 Windows 10+ installer (Inno Setup) | `setup/Provix.iss` |
| Per-user install (`Scope: user`) | `PrivilegesRequired=lowest` |
| Silent install switches | `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` |
| Stable `ProductCode` | `8F4E2A91-6C3D-4B8E-9F1A-2D7E5B4C9013_is1` |
| Apps & Features metadata | `DisplayName`, `DisplayVersion`, `Publisher` |
| SHA256 of release asset | generated in CI / script |
| License in manifest | `PolyForm-Noncommercial-1.0.0` |
| Locale manifests (en-US, ru-RU) | `winget/template/` |
| Bundled assets in installer | exe + Locales + Themes/Packs + IconPacks |

## Build installer

```cmd
build-setup.cmd
```

Output: `installer\Provix-Setup-<version>.exe`

## Generate manifests locally

```powershell
.\scripts\New-WingetManifest.ps1 -PackageVersion 1.3.5 -ReleaseTag v1.3.5 -Validate
```

Manifests are written to `winget\Provix.Provix\<version>\`.

## Validate only

Download [winget-create](https://github.com/microsoft/winget-create/releases) and run:

```powershell
winget validate --manifest winget\Provix.Provix\1.3.5
```

## GitHub Actions

Workflow `.github/workflows/winget.yml` runs on each published release:

1. Downloads `Provix-Setup-*.exe` from the release
2. Generates manifests from `winget/template/`
3. Validates with `wingetcreate validate`
4. Submits PR to `microsoft/winget-pkgs` when `WINGET_TOKEN` secret is set

### WINGET_TOKEN setup

1. GitHub → Settings → Developer settings → Personal access tokens (**classic**)
2. Create token with **`public_repo`** scope (fine-grained tokens do not work for cross-repo PRs)
3. Repository → Settings → Secrets → Actions → `WINGET_TOKEN`
4. Do **not** use `GITHUB_TOKEN` — it cannot open PRs in `microsoft/winget-pkgs`
5. Do **not** use fine-grained tokens — wingetcreate requires classic PAT

### Troubleshooting submit failures

If CI fails with `Failed to connect to GitHub`:
- Recreate **classic** PAT with only `public_repo`
- Update repository secret `WINGET_TOKEN`
- Re-run workflow **Publish to WinGet**

Or submit manually:
```powershell
$env:WINGET_CREATE_GITHUB_TOKEN = "ghp_..."
wingetcreate submit winget\Provix.Provix\1.3.5 --prtitle "Provix.Provix 1.3.5"
```

## Manual submission

```powershell
wingetcreate submit winget\Provix.Provix\1.3.5 --token YOUR_PAT
```

## After WinGet merge

Users can install with:

```powershell
winget install Provix.Provix
```
