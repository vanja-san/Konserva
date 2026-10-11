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
konserva-app/                        # Main application
├── Models/                          # Data models
├── Services/                        # Business logic services
│   ├── McServerInstaller/           # Per-loader installers (partial class)
│   └── McServerProcess/             # Process lifecycle, logs, Java (partial class)
├── ViewModels/                      # MVVM view models
├── Pages/                           # Page views (code-behind)
├── Controls/                        # Custom controls
│   └── Sections/                    # Server detail sections
├── Dialogs/                         # Modal dialogs
├── Converters/                      # Value converters
├── Localization/                    # Localization manager and string tables
├── Utilities/                       # Helpers (logger, paths, hashing)
├── App.xaml / MainWindow.xaml       # Application entry
├── konserva-app.csproj
└── konserva-app.slnx
konserva-app.Tests/                  # Test project (xUnit v3)
├── Models/                          # Model tests
├── Services/                        # Service tests
├── Utilities/                       # Utility tests
├── Converters/                      # Converter tests
├── Localization/                    # Localization tests
└── Regression/                      # Regression tests for fixed defects
Directory.Build.props                # Settings shared by both projects
.github/
│   └── workflows/                   # CI/CD workflows
publish/                             # Build output
build-publish-full.bat               # Full build script
build-publish-deps.bat               # Deps build script
BUILD.md                             # This file
README.md                            # User documentation (EN)
README.ru.md                         # User documentation (RU)
```

## Build Settings

### Release Configuration (konserva-app/konserva-app.csproj)
```xml
<Optimize>true</Optimize>
<DebugType>embedded</DebugType>
<DebugSymbols>true</DebugSymbols>
```

`<DebugType>none</DebugType>` and `<DebugSymbols>false</DebugSymbols>` are passed on the
command line by the publish scripts to keep release symbols out of the distributed archive.

### Publish Settings (Full build)
```bash
-p:PublishSingleFile=true
-p:EnableCompressionInSingleFile=true
-p:IncludeNativeLibrariesForSelfExtract=true
-p:DebugType=none
-p:DebugSymbols=false
```

`PublishReadyToRun` is **not** used. The csproj sets it to `false` deliberately: ReadyToRun
precompilation and single-file compression work against each other, and enabling both
produced a considerably larger archive for no measurable startup gain in this WPF app.

### Shared build settings
`Directory.Build.props` holds the settings common to both projects: `Nullable`,
`ImplicitUsings`, `LangVersion`, `AnalysisLevel`, `EnforceCodeStyleInBuild` and
`TreatWarningsAsErrors`. Warnings are errors in the test project too.

## Testing

### Run All Tests
```bash
dotnet test --configuration Release
```

### Run Specific Test Category
Categories are declared as a `Category` xUnit trait on each test class, so
filters work from the command line and from the test explorer UI:

```bash
# Unit tests only — pure logic, no I/O
dotnet test --filter "Category=Unit"

# Integration tests only — filesystem, registry, HTTP
dotnet test --filter "Category=Integration"
```

There is no `E2E` category: no end-to-end tests exist yet. Add the trait
(`[Trait("Category", "E2E")]`) when the first one appears.

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
Save all .cs and .xaml files as UTF-8. A BOM is not required — the .NET SDK
decodes BOM-less files as UTF-8, which is why the project builds with a mix of
both. If a Russian string looks garbled, the file was most likely saved in a
legacy single-byte codepage.

### WPF-UI errors
Make sure the WPF-UI packages are restored:
```bash
dotnet restore
```

## Code Quality

### Fix Encoding (Russian Text)
All source files must be UTF-8. To normalise them (strips stray BOMs, rewrites
every file in UTF-8) — run this only when a file genuinely misbehaves, and review
the resulting diff, since it touches every source file:
```powershell
Get-ChildItem -Recurse -Include *.cs,*.xaml | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($_.FullName, $content, [System.Text.UTF8Encoding]::new($false))
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

Application data is stored **next to the executable** — Konserva is fully portable and
writes nothing to the registry or to `%AppData%`:

```
<folder containing Konserva.exe>\
├── config.json          # Application settings
├── Servers\             # Server installations
│   └── servers.json     # Server list (inside the Servers directory)
├── Logs\                # Application logs
├── i18n\                # Generated translation files
└── downloads\           # Installer and update downloads (temporary)
```

Because the data sits in the install directory, it must be **writable**. Installing to a
protected location such as `C:\Program Files` breaks configuration saving, logging and
server installation. Use a per-user directory instead.

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

1. **UTF-8 without BOM** — a BOM is not required; the SDK decodes BOM-less UTF-8 correctly. Keep every file in UTF-8 so the Russian strings survive.
2. **Single File Publish** — Simplifies distribution
3. **Self-contained by default** — No .NET installation required for users
4. **WPF-UI 4.3** — Modern Fluent Design controls
5. **.NET 10** — Latest LTS framework
6. **xUnit Testing** — tests tagged with a `Category` trait (`Unit` / `Integration`); no E2E suite yet. Run `dotnet test` for the current count.
7. **Writes next to the executable** — portable by design; the install directory must be writable
8. **Updates are HTTPS-only and are never redirected** — the expected SHA-256 travels in the same manifest as the URL, so a downgrade redirect would defeat the integrity check

---

**Last Updated**: 2026-10-11
