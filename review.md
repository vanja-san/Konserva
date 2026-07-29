# Code Review: konserva-app

**Stack**: WPF, .NET10, WPF-UI (lepo.co), CommunityToolkit.Mvvm, Microsoft.Extensions.DI,
Microsoft.Extensions.Http + Resilience, Polly, AvalonEdit, SharpOpenNat

**Target**: `net10.0-windows`, nullable enabled, TreatWarningsAsErrors, AnalysisLevel=latest

**Build**: 0 warnings, 0 errors — clean

**Tests**: 343/343 passing — xUnit v3, Moq, FluentAssertions, coverlet

---

## Architecture: 8/10

- Clean MVVM with constructor-injected DI via CommunityToolkit.Mvvm + Microsoft.Extensions.DI
- Frame-based navigation (4 pages: ServersPage, CreateServerPage, ServerDetailPage, SettingsPage)
- Single-instance via Mutex+NamedPipe with `bringtofront` IPC — production-quality
- Partial class decomposition: McServerInstaller (8 parts), McServerProcess (3 parts)

### Concern: Service locator anti-pattern

`Ioc.Default.GetService<MainWindow>()` is used in `MainWindow.xaml.cs:478` (static navigation command)
and `App.xaml.cs:129` (main window resolution). Elsewhere, constructor injection is used consistently.
This creates an inconsistent DI approach. Prefer injecting MainWindow or using a navigation service.

### Concern: Tight coupling to WPF from service layer

`McServerManager.cs:211-214, 241-244, 276-279, 286-289` accesses
`System.Windows.Application.Current?.Dispatcher` directly from a service class. Services should not
reference WPF.Dispatcher — use an abstraction (e.g., `IDispatcher` or event-based notification).

---

## DI & Service Layer: 7/10

- 4 named HttpClient registrations with distinct resilience policies — excellent
- `AddStandardResilienceHandler` with exponential backoff + jitter on all HTTP clients
- `IPortForwardingService` optional (nullable) in McServerManager and ServerDetailViewModel — clean

### Issues

1. **Dual container references**: `Ioc.Default.ConfigureServices()` + private `_serviceProvider` field
   in `App.xaml.cs:92-93` — confusing. Pick one.
2. **McServerManager stores reference to `System.Windows.Application.Current.Dispatcher`** in a
   non-UI-layer service. Introduce `IDispatcher` abstraction for testability and layering.

---

## Threading & Async: 7/10

- `ReaderWriterLockSlim` for server list, `ConcurrentDictionary` for processes — good
- `PeriodicTimer` in status bar loop — correct
- `SafeFireAndForget` used throughout — proper fire-and-forget

### Critical: Sync Dispatcher.Invoke — deadlock risk

**`McServerManager.cs:241-244`** uses `Dispatcher.Invoke` (synchronous blocking) inside
`StartServerCoreAsync`. If this code path is hit from the UI thread, it will deadlock the
application. Should be `Dispatcher.InvokeAsync` or `Dispatcher.BeginInvoke`.

```csharp
// ❌ Deadlock risk
System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
{
    OnServersChanged?.Invoke();
});

// ✅ Safe
System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
{
    OnServersChanged?.Invoke();
});
```

### Fragile: Task.Delay(500) heuristic

**`McServerManager.cs:226`** waits 500ms to "check if error happened" asynchronously.
This is a race condition window — the process could fail after the delay. Consider using
the `OnStatusChanged` callback exclusively instead of polling with a timer.

---

## Code Quality by Layer

### Models (9/10)

Clean data objects inheriting `ObservableObject`, proper cloning, validation, JsonIgnore for
transient runtime state (`Status`, `LastErrorMessage`). `ServerProperties.cs` (524 lines) is
the longest — the `SetProperty` switch with ~80 cases is appropriate for the domain (Minecraft
server.properties format).

### ViewModels (7/10)

- `ServersViewModel` (231 lines) — lean, clean, good use of CommunityToolkit source generators
- `SettingsViewModel` (214 lines) — minimal, focused
- `ServerDetailViewModel` (694 lines) — moderate complexity, mod/plugin management logic
  belongs in a dedicated service
- `CreateServerViewModel` (545 lines) — heaviest, `SaveSettings` has 7 positional params.
  Extract a command/options object.
- `CreateServerViewModel.ValidateServerPath` has path traversal protection, permission check,
  system-folder guard — thorough

### Services (8/10)

- Well-factored with clear interfaces
- `McServerProcess` partials: `Core.cs` (479 lines), `Logs.cs` (250 lines), `Java.cs` (426 lines) — good separation
- `ServerStorageService` uses `FileBasedStore<T>` with caching + dirty-file detection — solid
- `McServerManager` handles UPnP lifecycle, zombie process killing, CPU tracking — comprehensive

### Converters (9/10)

11 converters, each single-responsibility. Clean, testable, registered in XAML.

### Localization (9/10)

- `LocalizationManager` is static but thread-safe via `Lock` + `ConcurrentDictionary`
- `LocExtension` markup extension + `LocalizationResource` INotifyPropertyChanged trigger —
  live language switch without restart
- JSON i18n file export for user editing
- English + Russian with English fallback for missing keys

### Tests (8/10)

343 tests across all layers (models, services, converters, utilities, localization).
Good use of FluentAssertions, Moq for service mocking. Covers edge cases well.

---

## Refactoring Priorities

### High
1. **`McServerManager.cs:241`** — sync `Dispatcher.Invoke` → `Dispatcher.InvokeAsync`
2. **`McServerManager.cs:226`** — replace `Task.Delay(500)` with callback-based error detection
3. **`MainWindow.xaml.cs:478`** — remove `Ioc.Default.GetService<MainWindow>()` service locator

### Medium
4. **`CreateServerViewModel.SaveSettings(7 params)`** — extract parameter object
5. **`McServerManager` Dispatcher dependency** — extract `IDispatcher` interface
6. **`App.xaml.cs:92-93`** — keep single DI container reference

### Low
7. **`ServerProperties.cs` (524 lines)** — consider source-generated parsing for the switch
8. **`IUpdateService` / `IUpdateChecker` / `IAppUpdater`** — consider merging thin layers
9. **`CreateServerViewModel.cs`** — extract mod-loader version filtering logic into a service
10. **`ServerDetailViewModel.cs`** — extract file-system operations (mod/plugin toggling,
    rename, delete) into a dedicated service

---

## Security

- `PathValidator.ContainsTraversalSequences` + `IsPathSafe` checks before all directory
  delete operations — good
- RCON password and management server secret stored in-memory in plaintext — acceptable
  for a local desktop application, worth documenting as a known limitation
- Path validation in `CreateServerViewModel.ValidateServerPath` checks: invalid chars,
  max length (260), system folders, permissions — thorough

---

## Summary

Production-quality WPF application with mature DI, proper async patterns, and comprehensive
test coverage. The codebase is well-structured, compiles cleanly at the strictest analysis
level, and has strong domain-model separation.

**3 high-priority items** (1 deadlock risk, 1 race condition, 1 service locator to eliminate)
**3 medium-priority items** (parameter explosion, WPF coupling, confusing dual-reference)
**4 low-priority items** (extract services, thin layers, code organization)
