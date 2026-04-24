# Konserva Build Guide

## Quick Build

### Windows (PowerShell/CMD)

```bash
# Self-contained build (~60 MB, single file, no .NET required)
build-publish-full.bat

# Deps build (~10 MB, requires .NET 10 Runtime)
build-publish-deps.bat
```

## Build Types

| Type | Size | .NET Required | Description |
|------|------|---------------|-------------|
| **Self-contained (Full)** | ~60 MB | ❌ No | Everything included, ready to run |
| **Deps** | ~10 MB | ✅ Yes | Requires .NET 10 Runtime |
| **Release** | - | ✅ Yes | For development, requires .NET SDK |

## Build Commands

### Standard Build
```bash
dotnet build --configuration Release
```

### Self-contained Publish (Full)
```bash
dotnet publish konserva-app\konserva-app.csproj ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish\Full
```

### Deps Publish
```bash
dotnet publish konserva-app\konserva-app.csproj ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -o publish\Deps
```

## GitHub Actions

CI/CD pipeline is configured in `.github/workflows/build-release.yml`.

### Manual Workflow Dispatch

1. Go to **Actions** tab in repository
2. Select **"Build Release"** workflow
3. Click **"Run workflow"**
4. Specify parameters:
   - **Version number**: Version tag (e.g., `1.2.0`)
   - **Create GitHub Release**: Create release (optional)
   - **Generate changelog**: Auto-generate changelog from commits (optional)
5. After build completes, download artifacts:
   - `Konserva-vX.X.X-Full.zip` — self-contained version (~60 MB)
   - `Konserva-vX.X.X-Deps.zip` — Deps version (~10 MB)

**Note**: If "Create GitHub Release" is enabled, a GitHub release will be created with changelog describing changes since the last version.

### Build Variants Comparison

| Parameter | Full | Deps |
|-----------|------|------|
| Size | ~60 MB | ~10 MB |
| Files | 1 | 1 |
| .NET Runtime | Built-in | Required |
| Recommended for | End users | Development/Testing |

**Full** — полностью автономная сборка. Включает .NET Runtime, все зависимости и библиотеки. Работает на любом ПК с Windows x64 без дополнительной установки. Идеально для обычных пользователей.

**Deps** — облегчённая сборка. Требует установленный .NET 10 Runtime. Занимает в 6 раз меньше места. Подходит для разработчиков и тех, у кого уже стоит .NET.

## Requirements

### For Build
- .NET 10 SDK
- Windows x64

### For Running (Self-contained)
- Windows x64
- Nothing else required

### For Running (Deps)
- Windows x64
- .NET 10 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/10.0))

## Output File Structure

```
publish/
├── Full/                # Self-contained build
│   ├── Konserva.exe     # ~60 MB
│   └── README.md
└── Deps/                # Deps build
    ├── Konserva.exe     # ~10 MB
    └── README.md
```

## Project Structure

```
konserva-app/
├── konserva-app/                    # Main application
│   ├── Models/                      # Data models
│   ├── Services/                    # Business logic services
│   ├── Views/                       # WPF views (XAML)
│   ├── ViewModels/                  # View models (if used)
│   ├── Converters/                  # Value converters
│   ├── Controls/                    # Custom controls
│   └── App.xaml / MainWindow.xaml   # Application entry
├── konserva-app.Tests/              # Test project (xUnit)
│   ├── Unit/                        # Unit tests
│   ├── Integration/                 # Integration tests
│   └── E2E/                         # End-to-end tests
├── .github/
│   └── workflows/                   # CI/CD workflows
├── publish/                         # Build output
├── build-publish-full.bat       # Full build script
├── build-publish-deps.bat       # Deps build script
├── BUILD.md                         # This file
├── README.md                        # User documentation (EN)
└── README.ru.md                     # User documentation (RU)
```

## Build Settings

### Release Configuration (.csproj)
```xml
<Optimize>true</Optimize>
<DebugType>none</DebugType>
```

### Publish Settings
```bash
-p:PublishSingleFile=true
-p:EnableCompressionInSingleFile=true
-p:PublishReadyToRun=true
-p:IncludeNativeLibrariesForSelfExtract=true
```

## Testing

### Run All Tests
```bash
dotnet test --configuration Release
```

### Run Specific Test Category
```bash
# Unit tests only
dotnet test --filter "Category=Unit"

# Integration tests only
dotnet test --filter "Category=Integration"

# E2E tests only
dotnet test --filter "Category=E2E"
```

### Test Output
Test results are saved to `TestResults/` directory.

## Troubleshooting

### Error "dotnet not found"
Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0

### Build errors
```bash
# Clean and rebuild
dotnet clean
dotnet build --configuration Release
```

### Large file size
Use Deps version (~10 MB instead of ~60 MB)

### Russian text encoding issues
Ensure all .cs and .xaml files are saved with UTF-8 with BOM encoding.

### WPF UI errors
Make sure WPF UI 4.2.0 package is restored:
```bash
dotnet restore
```

## Code Quality

### Fix Encoding (Russian Text)
All source files must be UTF-8 with BOM. Use PowerShell to fix:
```powershell
Get-ChildItem -Recurse -Include *.cs,*.xaml | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -Encoding UTF8
    [System.IO.File]::WriteAllText($_.FullName, $content, [System.Text.UTF8Encoding]::new($true))
}
```

### Check Build Warnings
```bash
dotnet build --configuration Release -v n
```

## Version Management

Update version in:
1. `konserva-app/konserva-app.csproj` — `<Version>` property
2. `konserva-app/AssemblyInfo.cs` — version attributes
3. README.md / README.ru.md — version badge

## Publishing to GitHub Releases

1. Create a new tag:
   ```bash
   git tag v1.2.0
   git push origin v1.2.0
   ```

2. Run GitHub Actions workflow with "Create GitHub Release" enabled

3. Artifacts will be automatically uploaded to the release

## Data Directory

Application data location:
```
%AppData%\Konserva\
├── config.json          # Application settings
├── servers.json         # Server list
├── Servers/             # Server installations
└── Logs/                # Application logs
```

## API Endpoints

Used by application for version resolution:

| Service | Endpoint |
|---------|----------|
| NeoForge (Primary) | `https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml` |
| NeoForge (Mirror) | `https://maven.creeperhost.net/neoforged/neoforge/maven-metadata.xml` |
| Fabric | `https://meta.fabricmc.net/v2/versions/loader/{mcVersion}` |
| Quilt | `https://meta.quiltmc.org/v3/versions/loader/{mcVersion}` |
| Paper | `https://api.papermc.io/v2/projects/paper` |
| Purpur | `https://api.purpurmc.org/v2/purpur` |
| Mojang Manifest | `https://launchermeta.mojang.com/mc/game/version_manifest.json` (GZip!) |

## Key Technical Decisions

1. **UTF-8 with BOM** — Required for Russian text in WPF
2. **Single File Publish** — Simplifies distribution
3. **Self-contained by default** — No .NET installation required for users
4. **WPF UI 4.2.0** — Modern Fluent Design controls
5. **.NET 10** — Latest LTS framework
6. **xUnit Testing** — 77 tests (Unit, Integration, E2E)

---

**Last Updated**: 2026-04-07
**Application Version**: 1.6.0
