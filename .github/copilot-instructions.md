# Konserva — Copilot Instructions

## 📋 About the Project
Konserva — WPF (.NET 10) Minecraft server manager. FluentWindow (WPF-UI), MVVM.

## 🏗️ Architecture

### Technology Stack
- **.NET 10.0-windows** (WinExe), Nullable=enable, ImplicitUsings=enable
- **WPF-UI 4.3.0** (lepoco) — FluentWindow, CardControl, CardExpander, Button (Appearance=Primary/Success/Caution/Danger), SymbolIcon, ThemeResource, NotifyIcon, ContentDialogHost, TitleBar, NavigationView
- **SharpOpenNat 4.0.17** — UPnP/NAT-PMP
- **DI**: Microsoft.Extensions.DependencyInjection (all services singleton)
- **HTTP**: HttpClientFactory + Polly retry (exponential + jitter)
- **Localization**: custom (dictionaries `Localization/EnglishStrings.cs`, `Localization/RussianStrings.cs`)
- **Tests**: xUnit v3 (343+), Moq 4.20.72, FluentAssertions 8.10.0, Coverlet 10.0.1
- **Single-instance**: Mutex + named pipe IPC

### Structure
```
konserva-app/
├── App.xaml.cs              — entry point, DI, single-instance, pipe server
├── MainWindow.xaml.cs       — main window: tray, navigation, updates (~700 lines)
├── Pages/                   — pages: ServersPage, ServerDetailPage, SettingsPage, CreateServerPage
├── Services/                — business logic (services + interfaces)
├── Models/                  — data models (Server, AppConfig, ModItem, etc.)
├── ViewModels/              — ViewModels (currently ServersViewModel only)
├── Controls/                — custom controls (UpdateNotification, ServerPropertiesEditor)
├── Converters/              — WPF converters
├── Localization/            — localization dictionaries
├── Utilities/               — ObservableObject, RelayCommand, Logger, FileBasedStore, etc.
└── Dialogs/                 — (empty for now)
```

### DI Container (App.xaml.cs)
All services registered as singleton:
```csharp
services.AddSingleton<IConfigService, ConfigService>();
services.AddSingleton<IServerManager, McServerManager>();
services.AddSingleton<IServerStorageService, ServerStorageService>();
services.AddSingleton<IMcVersionsApi, McVersionsApi>();
services.AddSingleton<IJavaManagementService, JavaManagementService>();
services.AddSingleton<IPortForwardingService, PortForwardingService>();
services.AddSingleton<MainWindow>();
// HttpClients: UpdateChecker, AppUpdater, McServerInstaller, PortForwardingService
```

### Service Locator (App.xaml.cs)
```csharp
App.ConfigService       // IConfigService
App.ServerManager       // IServerManager
App.MainWindow          // MainWindow
App.ServiceProvider     // IServiceProvider?
```

## 🚀 Commands

### Build
```powershell
dotnet build konserva-app/konserva-app.csproj --nologo
```

### Tests
```powershell
dotnet test konserva-app.Tests/konserva-app.Tests.csproj --nologo
```

### Publish
```powershell
# Full (self-contained, ~60 MB)
dotnet publish konserva-app/konserva-app.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/Full

# Deps (framework-dependent, ~9 MB)
dotnet publish konserva-app/konserva-app.csproj -c Release -r win-x64 --self-contained false -o publish/Deps
```

## 📐 Conventions

### Code
- **Nullable** enabled at project level — use `?` for nullable types
- **ImplicitUsings** enabled
- `TreatWarningsAsErrors=true` — no warnings allowed
- Validate arguments via `ArgumentNullException.ThrowIfNull()` or `?? throw`
- `async void` only for event handlers (Button_Click, etc.)
- Propagate `CancellationToken` everywhere
- Use `Logger` instead of Console.WriteLine / Debug.WriteLine

### Naming
- **Classes**: PascalCase
- **Fields**: `_camelCase` (private)
- **Properties**: PascalCase
- **Local variables**: camelCase
- **Interfaces**: `I` prefix
- **Async methods**: `Async` suffix

### Language
- All user-facing communication, descriptions, explanations, and review conclusions must be written **in Russian**
- Code, code comments, XML-doc summaries, variable/class/method names, identifiers, API references, tool names, and technical terms **must always be in English**
- Inline code comments (`// comment`) and XML-doc (`/// <summary>`) should be in English — the codebase is bilingual (RU/EN), but the code itself and its documentation in code are English

### WPF-UI
- Use `SymbolIcon` with correct icons: Wifi120, CheckmarkCircle24, MoreHorizontal20, etc.
- Icons in WPF-UI have a size suffix (20, 24, 48, 120)
- Use `Appearance` for buttons instead of overriding styles
- `CardExpander` for expandable panels
- `ContentDialogService` for dialogs
- `SnackbarService` for toast notifications

## 🔄 Update Check Flow
1. `App.xaml.cs` → `InitializeServicesAsync()` → `UpdateChecker.Initialize(httpClient)`
2. `MainWindow_Loaded` → `InitializeMainWindow()` → `StartUpdateCheckLoop()`
3. `UpdateCheckLoopAsync()` → `UpdateService.Start()` → `LoopAsync()`
4. `LoopAsync()` → immediately `FetchAndSaveAsync()` → `UpdateChecker.CheckAsync()`
5. `UpdateChecker.CheckAsync()` → reads **`version.json`** via `raw.githubusercontent.com` (CDN, no rate limit)
6. `version.json` lives in `.github/`: `latestVersion`, `downloads` (full/deps), `releaseNotes`, `changelogUrl`
7. If `config.CheckUpdates == false` (On Launch) — exits after first check
8. If `config.CheckUpdates == true` (Scheduled) — `PeriodicTimer` with configurable interval
9. In-memory throttle 15 min between real HTTP requests (protection against frequent restarts)

## 🧪 Testing
- Use `[Theory]` + `[InlineData]` for parameterized tests
- Moq for mocking: `new Mock<T>()`
- FluentAssertions: `.Should().Be()`, `.Should().NotBeNull()`, etc.
- For config-related tests use `TestConfigFixture` (ICollectionFixture)
- Service tests are unit tests with Moq
- CancellationToken in tests: `TestContext.Current.CancellationToken`

## 🤖 Code Review Agent (`.github/agents/code-review.agent.md`)

A specialized `@code-review` agent is set up for deep code analysis. Delegation rules:

### When to automatically delegate to `@code-review`
- **User explicitly asks for code review**: "review the code", "find issues", "code review", "audit"
- **PR/MR is pushed or discussed**: delegate review of changes to this agent
- **Architecture analysis**: questions about DI, MVVM, patterns, startup flow
- **Security review**: path traversal, shell injection, file handling
- **Performance optimization**: async patterns, memory, threading

### When NOT to delegate
- Simple language or syntax questions (answer directly)
- Fixing a single function or bug (do it yourself and show the result)
- Creating new files or features (if no existing code review is required)

### How to use
- In chat: `@code-review analyze MainWindow.xaml.cs for WPF-UI best practices`
- Via subagent: use `runSubagent` with agentName: `code-review` for automatic delegation

### Prompt templates for `@code-review`

**Full project review:**
```
@code-review Do a full project review across all dimensions (architecture, C# 13+, WPF-UI, perf, security, testing, localization)
```

**Single file review:**
```
@code-review Check {filePath} for {topics: arch-di | csharp13 | wpf-ui | perf | security | error-handling | testing | localization}
```

**PR review:**
```
@code-review Analyze the changes in this PR for regressions and code quality
```

## 🤖 Созданные агенты

В `.github/agents/` созданы специализированные агенты. Delegation rules:

| Агент | Назначение | Когда делегировать |
|-------|-----------|-------------------|
| `@code-review` | Полное ревью кода | Архитектура, DI, C# 13+, WPF-UI, perf, security, testing, localization |
| `@xaml-designer` | WPF-UI / XAML дизайн | Вёрстка, стили, DataTemplate, binding, SymbolIcon, конвертеры, анимации |
| `@test-runner` | Unit-тесты | Написание/фикс тестов, улучшение coverage, xUnit v3, Moq, FluentAssertions |
| `@localization-manager` | Локализация | Добавление/синхронизация ключей en/ru, аудит перевода |
| `@mc-api` | Minecraft API | Версии, загрузчики (Forge/Fabric/NeoForge/Quilt/Paper), установка серверов |
| `@build-publisher` | Сборка и CI | dotnet build/publish, GitHub Actions, publish profiles |
| `@packages-gardener` | NuGet-зависимости | Обновление пакетов, проверка совместимости, уязвимости |
| `@process-debugger` | Процессы и Java | Отладка запуска Java-серверов, парсинг логов, CPU/RAM |

### Общие правила делегирования
- Если запрос попадает в область одного из агентов — **делегируй через `runSubagent`**
- Если запрос комплексный (например, ревью + тесты) — делай сам, привлекая агентов по подзадачам
- Простые вопросы (синтаксис, односложный ответ) — отвечай без делегирования
