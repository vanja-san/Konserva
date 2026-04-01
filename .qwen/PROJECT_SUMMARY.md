# Project Summary

## Overall Goal
Modernize and fix the Konserva WPF application (.NET 10) for managing Minecraft servers, including bug fixes, UI improvements, namespace standardization, code quality enhancements, and Russian text encoding fixes.

## Key Knowledge

### Technology Stack
- **Framework**: .NET 10.0 Windows (WPF)
- **Language**: C# 12-14 with nullable reference types
- **Namespace**: `Konserva` (changed from `konserva-app`)
- **UI Framework**: WPF UI 4.2.0 (TitleBar, FluentWindow, custom controls, Badge, InfoBar)
- **Navigation**: Standard WPF `Frame` control (not NavigationView)
- **Theme System**: WPF UI Appearance Manager (System/Dark/Light)
- **Build Commands**:
  - `dotnet build --configuration Release` — стандартная сборка
  - `build-publish.bat publish` — self-contained версия (~66 MB, один файл)
  - `build-publish-min.bat build` — минимальная версия (~1.6 MB, требует .NET)

### Architecture
- **DI Container**: Microsoft.Extensions.DependencyInjection
- **Services**: ConfigService, ServerStorageService, McServerManager, McVersionsApi, JavaManagementService
- **Data Storage**: `%AppData%\Konserva\` (config.json, servers.json, Servers/, Logs/)
- **Lock Mechanism**: C# 13 `Lock` type (вместо `object`)

### Critical Version Formats
- **NeoForge Maven Format**: `MAJOR.MINOR.PATCH[-beta/-alpha.x+snapshot-x]`
- **Minecraft to NeoForge Conversion**: `1.21.11` → `21.11`, `26.1` → `26.1` (new format without "1." prefix)
- **Test Version Markers**: `-beta`, `-alpha.`, `+snapshot`, `+pre`, `-pre.`, `-rc.`

### API Endpoints
- **NeoForge (Primary)**: `https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml`
- **NeoForge (Mirror)**: `https://maven.creeperhost.net/neoforged/neoforge/maven-metadata.xml`
- **Fabric**: `https://meta.fabricmc.net/v2/versions/loader/{mcVersion}`
- **Quilt**: `https://meta.quiltmc.org/v3/versions/loader/{mcVersion}`
- **Paper**: `https://api.papermc.io/v2/projects/paper`
- **Purpur**: `https://api.purpurmc.org/v2/purpur`
- **Mojang Manifest**: `https://launchermeta.mojang.com/mc/game/version_manifest.json` (returns GZip!)

### User Preferences
- Prefer Russian language for UI and comments
- Show full version numbers (not truncated)
- Filter test versions when checkbox is unchecked
- Minimal logging (only critical errors)
- Build logic in .bat files only, not in .csproj
- Single executable file without dependencies
- **UTF-8 with BOM encoding** for all source files (critical for Russian text)
- Custom navigation buttons in TitleBar (not NavigationView)
- Auto-save settings with visual confirmation

### Critical Technical Details
- **Mojang API returns GZip compressed responses** - must add Accept-Encoding headers and decompress
- **NeoForge version format differs from Minecraft** - 1.21.10 → 21.10 for promo keys
- **UI thread blocking causes freezes** - always use async/await for server operations
- **Version filtering** - NeoForge versions (26.1, 21.10) must be filtered from Vanilla/Forge/Fabric lists
- **NavigationView creates unwanted left margins** - use standard `Frame` for navigation instead
- **WPF UI FluentWindow requires SizeToContent="Manual"** to prevent auto-resize
- **Console encoding** - UTF-8 for Minecraft 1.20.5+ and newer versions (26.1+)
- **Theme switching** - Use `Wpf.Ui.Appearance.ApplicationThemeManager` with registry check for System theme
- **Settings auto-save** - Use `_isLoading` flag to prevent saving during initial load

## Recent Actions

### Fixed Issues (Previous Sessions)
1. **[FIXED] Russian text encoding (кракозябры)** - Manually fixed corrupted Russian text in 30+ files including McServerInstaller.cs (1900+ lines)
2. **[FIXED] Mojang API GZip decompression** - Added proper GZip/Deflate handling in `GetVanillaServerDownloadUrl()` method
3. **[FIXED] NeoForge URL formatting** - Fixed promoKey format to use NeoForge version format (21.10 instead of 1.21.10)
4. **[FIXED] UI freeze on server delete** - Changed `Delete_Click` handlers to use `async/await` instead of blocking `.GetAwaiter().GetResult()`
5. **[FIXED] Version filtering** - Added filter to exclude NeoForge versions from Vanilla/Forge/Fabric lists
6. **[FIXED] Quilt versions duplicating on first load** - Added cache protection
7. **[FIXED] Quilt test version filtering** - Added `IsQuiltSnapshot()` method
8. **[FIXED] Unsupported Minecraft versions for Quilt** - Added API validation
9. **[FIXED] NeoForge Maven API unavailable** - Added mirror support
10. **[FIXED] Removed tray icon functionality** - Removed `MinimizeToTray` and related UI
11. **[FIXED] Namespace inconsistency** - Changed to `namespace Konserva`

### Fixed Issues (This Session - 2026-03-31)

#### TitleBar & Window Improvements
1. **[FIXED] TitleBar title display** - Title was not showing when Header was set
   - Added `TextBlock` with title text inside `Header` StackPanel
   - Used `IsHitTestVisible="False"` to allow dragging window through title text
   - Title positioned before back button and app icon

2. **[FIXED] TitleBar icon quality** - Icon was pixelated
   - Increased size from 16x16 to 20x20
   - Added `RenderOptions.BitmapScalingMode="HighQuality"`
   - Used `Grid` with columns for proper alignment

3. **[FIXED] TitleBar text centering** - Text was not vertically centered
   - Replaced `StackPanel` with `Grid` for better alignment control
   - Added proper `Margin` and `VerticalAlignment="Center"`

#### Settings Page Improvements
4. **[FIXED] Settings auto-save implementation** - Removed manual save button
   - Removed "Save" button panel from bottom of SettingsPage
   - Added `AutoSaveSettings()` method with `_isLoading` and `_isUpdating` flags
   - Auto-save triggered on: Java selection, RAM changes, theme change, checkbox toggle
   - Protection against recursive saves and saving during initial load

5. **[FIXED] Settings save notification** - Visual feedback for auto-save
   - Added green `TextBlock` "✓ Настройки сохранены" on right side of header
   - Auto-hides after 2 seconds using `Task.Delay`
   - Only shows after actual user changes (not on initial load)

6. **[FIXED] Theme system implementation** - Full theme switching support
   - Added new Expander "Тема и внешний вид" with `Color20` icon
   - Three theme options: "Как в системе", "Тёмная", "Светлая"
   - `ApplyTheme()` method in MainWindow using `Wpf.Ui.Appearance.ApplicationThemeManager`
   - System theme detection via Windows Registry (`AppsUseLightTheme`)
   - Theme persisted in config.json and applied on startup

7. **[FIXED] Status bar theme support** - Fixed colors now adapt to theme
   - Background: `ApplicationBackgroundBrush` (was `#1A1A1A`)
   - Icons: `TextFillColorSecondaryBrush` (was `Gray`)
   - Text: `TextFillColorSecondaryBrush` (was `Gray`)
   - Version: `TextFillColorTertiaryBrush` (was `Gray`)
   - Running status: `SystemFillColorSuccessBrush` (was `Green`)

8. **[FIXED] Mod loader badges** - Replaced custom Border with ui:Badge
   - Vanilla: Info (blue)
   - Forge: Danger (red)
   - NeoForge: Success (green)
   - Fabric: Caution (yellow)
   - Quilt: Info (blue)
   - Paper: Danger (red)
   - Purpur: Info (blue)
   - Spigot: Translucent (gray, "В разработке")

### Testing Results
- ✅ Build successful (Release configuration)
- ✅ Title displays correctly in TitleBar
- ✅ Title text doesn't interfere with window dragging
- ✅ Icon displays at high quality (20x20, HighQuality scaling)
- ✅ Settings auto-save works without initial load trigger
- ✅ Save notification appears only after user changes
- ✅ Theme switching works: System ↔ Dark ↔ Light
- ✅ System theme correctly detects Windows registry setting
- ✅ Status bar colors adapt to theme changes
- ✅ Mod loader badges display with proper colors
- ✅ All previous tests still passing

## Current Plan

### Completed [DONE]
1. [DONE] Fix Russian text encoding in all source files (UTF-8 with BOM)
2. [DONE] Fix Mojang API GZip decompression
3. [DONE] Fix NeoForge URL formatting and promoKey conversion
4. [DONE] Fix UI freeze on server deletion (async/await)
5. [DONE] Fix version filtering (exclude NeoForge versions)
6. [DONE] Fix Quilt version duplication
7. [DONE] Fix Quilt test version filtering
8. [DONE] Add NeoForge Maven mirror support
9. [DONE] Remove tray icon functionality
10. [DONE] Remove "Start Minimized" setting
11. [DONE] Change namespace to `Konserva`
12. [DONE] Fix all build warnings
13. [DONE] WPF UI 4.2.0 TitleBar customization (back button + settings button)
14. [DONE] Replace NavigationView with Frame (remove left margin issues)
15. [DONE] Fix page margins for consistent spacing
16. [DONE] Fix WPF UI resource errors (replace non-existent resources)
17. [DONE] Replace Play/Stop TextBlock with SymbolIcon
18. [DONE] Add Minecraft 26.1 support (new version format)
19. [DONE] Fix mod loader version loading (remove duplicates)
20. [DONE] Fix CreateServerDialog window size (SizeToContent="Manual")
21. [DONE] Remove auto-generated server names
22. [DONE] Remove "latest"/"recommended" from loader versions
23. [DONE] Replace all MessageBox with WPF UI MessageBox
24. [DONE] Fix server status updates on error
25. [DONE] Fix unobserved task exceptions
26. [DONE] Fix cross-thread UI access (Dispatcher.Invoke)
27. [DONE] Fix console encoding (UTF-8)
28. [DONE] Simplify Java display name across UI
29. [DONE] Fix TitleBar title display and dragging
30. [DONE] Improve TitleBar icon quality
31. [DONE] Implement settings auto-save with notification
32. [DONE] Add full theme system (System/Dark/Light)
33. [DONE] Fix status bar theme adaptation
34. [DONE] Replace mod loader borders with ui:Badge
35. [DONE] Upgrade Mods/Plugins pages to Fluent Design (CardControl, Badge, improved typography)
36. [DONE] Refactor XAML to compact syntax (RowDefinitions/ColumnDefinitions)
37. [DONE] Complete project code review with recommendations
38. [DONE] Fix Server ID generation (Interlocked.Increment duplicate prevention)
39. [DONE] Remove unused files (FolderPickerDialog, MicaHelper, WindowCornerHelper, FsUtils, UiExtensions, Theme.xaml, AboutPage)
40. [DONE] Create test project with xUnit (77 tests: Unit, Integration, E2E)
41. [DONE] Setup CI/CD GitHub Actions workflow with auto-release
42. [DONE] Replace all Expander with CardExpander (14 controls)
43. [DONE] Replace server cards with CardAction
44. [DONE] Add VerticalAlignment="Center" to all TextBox controls
45. [DONE] Fix Settings page navigation (prevent duplicate opens)
46. [DONE] Improve MessageBox design (remove Close button, use Primary+Close)
47. [DONE] Update application version to 1.2.0

### Known Limitations [TODO]
1. **[TODO] NeoForge "latest" version resolution** - Promo API may return 404 for some versions
2. **[TODO] NeoForge installer URL format** - May need fallback for version resolution
3. **[TODO] Full E2E testing** - Test all mod loader types (Vanilla, Forge, NeoForge, Fabric, Quilt) with actual server creation

---

## Future Improvements (Code Review 2026-03-31)

### 🔴 Critical (Требуется выполнение)
1. **[TODO] Add unit tests** - No test coverage for services (McServerInstaller, McServerProcess, etc.)
2. **[TODO] Add integration tests** - No tests for API endpoints and file operations
3. **[TODO] Fix Server ID generation** - `Interlocked.Increment` may produce duplicate IDs after app restart
4. **[TODO] Handle DispatcherUnhandledException** - Currently doesn't close app, may cause instability

### 🟡 Important (Рекомендуется)
5. **[TODO] Split large files** - Refactor files >1000 lines:
   - `McServerInstaller.cs` (1915 lines) → Split by installer type (Vanilla, Forge, NeoForge, Fabric, etc.)
   - `McServerProcess.cs` (907 lines) → Extract log parsing methods
   - `CreateServerDialog.xaml.cs` (1145 lines) → Simplify logic
6. **[TODO] Extract common XAML styles** - Move duplicated button styles to `Theme.xaml`
7. **[TODO] Improve logging** - Add more debug logging (currently minimal, only critical errors)
8. **[TODO] Add structured logging** - Consider JSON format for log entries
9. **[TODO] Add XML documentation** - Complete missing XML comments (especially in converters)

### 🟢 Desirable (Желательно)
10. **[TODO] Add CI/CD pipeline** - Automated builds with unit tests
11. **[TODO] Add code analysis** - SonarQube, StyleCop integration
12. **[TODO] Create developer documentation** - Architecture overview, contribution guidelines
13. **[TODO] Add E2E tests** - Full workflow testing (create → start → stop → delete server)
14. **[TODO] Improve error handling** - More granular error messages for users
15. **[TODO] Add telemetry** - Optional anonymous usage statistics (with user consent)

### Testing Checklist
- [x] Select Vanilla → verify only MC versions (1.X.Y, 26.1) shown
- [x] Select NeoForge → verify stable versions shown when checkbox unchecked
- [x] Select Forge → verify Forge versions load correctly
- [x] Select Fabric → verify Fabric versions load correctly
- [x] Select Quilt → verify Quilt versions load correctly
- [x] Title displays in TitleBar and doesn't block dragging
- [x] Icon displays at high quality
- [x] Settings auto-save works (no save on initial load)
- [x] Save notification appears and auto-hides
- [x] Theme switching works (System/Dark/Light)
- [x] Status bar adapts to theme changes
- [ ] Create Vanilla server → verify download works
- [ ] Create NeoForge server → verify installer URL is correct
- [ ] Delete server → verify no UI freeze
- [ ] Verify Russian characters display correctly in console
- [ ] Verify back button works for all navigation paths
- [ ] Verify CreateServerDialog cannot be resized

### Build & Run
```bash
cd M:\User\Dev\.visualstudio\konserva-app

# Standard build
dotnet build --configuration Release

# Self-contained publish (~66 MB, single file)
build-publish.bat publish

# Portable publish (~1.6 MB, requires .NET)
build-publish-min.bat build

# Clean all builds
build-publish.bat clean
```

### Debug Logging
Key log messages (minimal, only critical):
- `NavButton_Click: Before/After Navigate, CanGoBack=...` — навигация к настройкам
- `BackButton_Click: CanGoBack=...` — нажатие кнопки "Назад"
- `ContentFrame_Navigated: CanGoBack=..., BackButton.Visibility=...` — обновление кнопки
- `Navigated to server {serverId}` — переход к деталям сервера
- `Dialog load error: {ex}` — критичная ошибка загрузки диалога
- `Failed to load {modLoaderType} versions: {ex}` — ошибка загрузки версий
- `Server install failed: {error}` — ошибка установки сервера
- `Server install completed: {name}` — успешная установка
- `Play_Click: Starting/Stopping server {name}` — запуск/остановка сервера
- `Play_Click: Error managing server {name}: {error}` — ошибка управления сервера
- `Applying theme: {theme}` — применение темы

---

## Summary Metadata
**Update time**: 2026-04-01T12:00:00+03:00
**Session duration**: ~48 hours (continued from previous session)
**Files modified**: 100+ (Russian text fixes + WPF UI navigation + TitleBar customization + MessageBox integration + Server status fixes + Theme system + Auto-save + Mods/Plugins Fluent Design + CardExpander + CardAction + Tests + CI/CD)
**Lines changed**: ~12000+
**Build status**: ✅ Successful (no warnings, no errors)
**Publish status**: ✅ Successful (62 MB self-contained, 3 MB portable)
**Encoding**: UTF-8 with BOM (critical for Russian text)
**Namespace**: `Konserva`
**UI Framework**: WPF UI 4.2.0 (TitleBar, FluentWindow, custom controls, MessageBox, Badge, ThemeManager, CardControl, CardAction, CardExpander)
**Navigation**: Standard WPF Frame (not NavigationView - causes margin issues)
**Console Encoding**: UTF-8 (for Minecraft 1.20.5+ and newer versions)
**Theme System**: WPF UI Appearance Manager with Windows Registry detection
**Test Framework**: xUnit 2.9.3 (77 tests: 46 Unit + 16 Integration + 15 E2E)
**CI/CD**: GitHub Actions with auto-release (Full + Deps builds)
**Version**: 1.2.0

### UI Layout
```
┌─────────────────────────────────────────────────────────┐
│ [←] [🏠] Konserva - MC Server Manager      [⚙]  [─][□][×] │
│  ↑   ↑                                    ↑             │
│  │   │                                    │             │
│  │   └─ App icon                          └─ Settings   │
│  └─ Back button (visible when CanGoBack)                │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  Page content with Margin="16" or Margin="20"          │
│  (equal spacing on all sides)                          │
│                                                         │
├─────────────────────────────────────────────────────────┤
│ 🖥️ 0  ✅ 0  💾 0 MB  🔧 Java не настроена   v1.0       │
│  ↑   ↑    ↑         ↑                    ↑              │
│  │   │    │         │                    └─ Version    │
│  │   │    │         └─ Java status (theme-aware)       │
│  │   │    └─ Memory (theme-aware)                      │
│  │   └─ Running (green, theme-aware)                   │
│  └─ Total servers (theme-aware)                        │
└─────────────────────────────────────────────────────────┘
```

### Build Variants
| Variant | Size | Files | .NET Required |
|---------|------|-------|---------------|
| Full (Self-contained) | ~66 MB | 1 | No |
| Portable | ~3 MB | 1 | Yes (.NET 10) |

### GitHub Actions
- Manual workflow dispatch
- Auto-generate changelog from commits
- Create GitHub release with artifacts
- Two build variants: Full and Portable

### Key Technical Decisions
1. **WPF UI MessageBox** - Use async `UiHelper` methods instead of standard MessageBox
2. **SizeToContent="Manual"** - Required for FluentWindow to prevent auto-resize
3. **Dispatcher.Invoke()** - Required for all UI updates from background threads
4. **UTF-8 Console Encoding** - Required for Minecraft 1.20.5+ and newer versions
5. **Version Filtering** - 2-part versions (26.1) = Minecraft, 3+ parts not starting with 1 = NeoForge
6. **Mod Loader Loading** - Use `_isChangingModLoader` and `_currentModLoader` flags to prevent race conditions
7. **Auto-save Settings** - Use `_isLoading` flag to prevent saving during initial page load
8. **Theme System** - Use `Wpf.Ui.Appearance.ApplicationThemeManager` with registry check for System theme
9. **TitleBar Dragging** - Use `IsHitTestVisible="False"` on title text to allow window dragging
10. **Status Bar Theming** - Use theme resources (`ApplicationBackgroundBrush`, `TextFillColorSecondaryBrush`) instead of fixed colors

### Configuration
**config.json** structure:
```json
{
  "Theme": "System",  // System, Dark, Light
  "Language": "ru",
  "JavaInstallations": [...],
  "DefaultJavaId": "...",
  "DefaultRamMin": 1024,
  "DefaultRamMax": 4096,
  "CheckUpdates": true,
  "ServersDirectory": "...",
  "ApiEndpoints": {...}
}
```
