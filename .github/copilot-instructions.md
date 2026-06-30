# Konserva — инструкции для Copilot

## 📋 О проекте
Konserva — WPF (.NET 10) менеджер Minecraft-серверов. FluentWindow (WPF-UI), MVVM.

## 🏗️ Архитектура

### Технологии
- **.NET 10.0-windows** (WinExe), Nullable=enable, ImplicitUsings=enable
- **WPF-UI 4.3.0** (lepoco) — FluentWindow, CardControl, CardExpander, Button (Appearance=Primary/Success/Caution/Danger), SymbolIcon, ThemeResource, NotifyIcon, ContentDialogHost, TitleBar, NavigationView
- **SharpOpenNat 4.0.17** — UPnP/NAT-PMP
- **DI**: Microsoft.Extensions.DependencyInjection (все сервисы singleton)
- **HTTP**: HttpClientFactory + Polly retry (exponential + jitter)
- **Локализация**: собственная (словари `Localization/EnglishStrings.cs`, `Localization/RussianStrings.cs`)
- **Тесты**: xUnit v3 (343+), Moq 4.20.72, FluentAssertions 8.10.0, Coverlet 10.0.1
- **Single-instance**: Mutex + named pipe IPC

### Структура
```
konserva-app/
├── App.xaml.cs              — точка входа, DI, single-instance, pipe-сервер
├── MainWindow.xaml.cs       — главное окно: трей, навигация, обновления (~700 строк)
├── Pages/                   — страницы: ServersPage, ServerDetailPage, SettingsPage, CreateServerPage
├── Services/                — бизнес-логика (сервисы + интерфейсы)
├── Models/                  — модели данных (Server, AppConfig, ModItem и др.)
├── ViewModels/              — ViewModel (пока только ServersViewModel)
├── Controls/                — кастомные контролы (UpdateNotification, ServerPropertiesEditor)
├── Converters/              — конвертеры WPF
├── Localization/            — словари локализации
├── Utilities/               — ObservableObject, RelayCommand, Logger, FileBasedStore и др.
└── Dialogs/                 — (пока пусто)
```

### DI-контейнер (App.xaml.cs)
Все сервисы регистрируются как singleton:
```csharp
services.AddSingleton<IConfigService, ConfigService>();
services.AddSingleton<IServerManager, McServerManager>();
services.AddSingleton<IServerStorageService, ServerStorageService>();
services.AddSingleton<IMcVersionsApi, McVersionsApi>();
services.AddSingleton<IJavaManagementService, JavaManagementService>();
services.AddSingleton<IPortForwardingService, PortForwardingService>();
services.AddSingleton<MainWindow>();
// HttpClient'ы: UpdateChecker, AppUpdater, McServerInstaller, PortForwardingService
```

### Service Locator (App.xaml.cs)
```csharp
App.ConfigService       // IConfigService
App.ServerManager       // IServerManager
App.MainWindow          // MainWindow
App.ServiceProvider     // IServiceProvider?
```

## 🚀 Команды

### Сборка
```powershell
dotnet build konserva-app/konserva-app.csproj --nologo
```

### Тесты
```powershell
dotnet test konserva-app.Tests/konserva-app.Tests.csproj --nologo
```

### Публикация
```powershell
# Full (self-contained, ~60 МБ)
dotnet publish konserva-app/konserva-app.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/Full

# Deps (framework-dependent, ~9 МБ)
dotnet publish konserva-app/konserva-app.csproj -c Release -r win-x64 --self-contained false -o publish/Deps
```

## 📐 Соглашения

### Код
- **Nullable** включён на уровне проекта — используйте `?` для nullable-типов
- **ImplicitUsings** включены
- `TreatWarningsAsErrors=true` — без предупреждений
- Проверяйте аргументы через `ArgumentNullException.ThrowIfNull()` или `?? throw`
- `async void` только для event-handlers (Button_Click и т.п.)
- CancellationToken пробрасывайте везде
- Logger используйте вместо Console.WriteLine/ Debug.WriteLine

### Именование
- **Классы**: PascalCase
- **Поля**: `_camelCase` (private)
- **Свойства**: PascalCase
- **Локальные переменные**: camelCase
- **Интерфейсы**: `I`-префикс
- **Async-методы**: суффикс `Async`

### WPF-UI
- Используйте `SymbolIcon` с корректными иконками: Wifi120, CheckmarkCircle24, MoreHorizontal20 и т.д.
- Иконки в WPF-UI имеют суффикс размера (20, 24, 48, 120)
- Для кнопок используйте `Appearance` вместо переопределения стилей
- `CardExpander` для раскрывающихся панелей
- `ContentDialogService` для диалогов
- `SnackbarService` для всплывающих уведомлений

## 🔄 Update Check Flow
1. `App.xaml.cs` → `InitializeServicesAsync()` → `UpdateChecker.Initialize(httpClient)`
2. `MainWindow_Loaded` → `InitializeMainWindow()` → `StartUpdateCheckLoop()`
3. `UpdateCheckLoopAsync()` → сначала всегда `CheckForUpdatesAsync()`, затем цикл по режиму
4. `CheckForUpdatesAsync()` → `UpdateChecker.CheckAsync()` → GitHub Releases API
5. Если `config.CheckUpdates == false` (On Launch) — ждёт 1 мин и проверяет, не изменился ли режим
6. Если `config.CheckUpdates == true` (Scheduled) — `PeriodicTimer` с интервалом из конфига

## 🧪 Тестирование
- Используйте `[Theory]` + `[InlineData]` для параметризованных тестов
- Moq для моков: `new Mock<T>()`
- FluentAssertions: `.Should().Be()`, `.Should().NotBeNull()` и т.д.
- Для тестов с конфигом используйте `TestConfigFixture` (ICollectionFixture)
- Тесты сервисов — unit-тесты с Moq
- CancellationToken в тестах: `TestContext.Current.CancellationToken`
