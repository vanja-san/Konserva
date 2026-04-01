# Konserva - Тесты

## 🧪 Запуск тестов

```bash
# Запустить все тесты
dotnet test

# Запустить с подробным выводом
dotnet test --verbosity normal

# Запустить с покрытием кода
dotnet test --collect:"XPlat Code Coverage"

# Запустить конкретный тест по имени
dotnet test --filter "FullyQualifiedName~McServerInstallerTests"

# Запустить тесты из определённого файла
dotnet test --filter "FullyQualifiedName~ServerTests"

# Запустить в режиме watch (автоматический перезапуск при изменениях)
dotnet watch test

# Запустить только E2E тесты
dotnet test --filter "FullyQualifiedName~E2E"
```

## 📁 Структура тестов

```
konserva-app.Tests/
├── Services/
│   ├── McServerInstallerTests.cs    # Тесты установщика серверов
│   ├── McServerProcessTests.cs      # Тесты процесса сервера
│   └── ConfigServiceTests.cs        # Тесты конфигурации
├── Models/
│   └── ServerTests.cs               # Тесты моделей
├── Utilities/
│   └── JavaVersionParserTests.cs    # Тесты утилит
├── E2E/
│   ├── ServerLifecycleE2ETests.cs   # E2E: Жизненный цикл сервера
│   └── SettingsE2ETests.cs          # E2E: Настройки приложения
├── Fixtures/
│   └── TestConfigFixture.cs         # Общие ресурсы для тестов
└── konserva-app.Tests.csproj
```

## 📊 Типы тестов

| Тип | Количество | Описание |
|-----|------------|----------|
| **Unit** | ~40 | Тестирование отдельных методов/классов |
| **Integration** | ~16 | Взаимодействие между сервисами |
| **E2E** | ~15 | Полные сценарии работы приложения |
| **ВСЕГО** | **~71** | |

## 📝 Написание тестов

### Простой тест
```csharp
[Fact]
public void MyTest_ReturnsTrue()
{
    // Arrange
    var service = new MyService();
    
    // Act
    var result = service.DoSomething();
    
    // Assert
    result.Should().BeTrue();
}
```

### Параметризованный тест
```csharp
[Theory]
[InlineData("input1", "expected1")]
[InlineData("input2", "expected2")]
public void MyTest_VariousInputs_ReturnsExpected(
    string input, 
    string expected)
{
    var result = MyService.Process(input);
    result.Should().Be(expected);
}
```

### WPF тест (STA поток)
```csharp
[StaFact]
public void WpfComponent_Initialization_Works()
{
    var window = new MainWindow();
    window.Should().NotBeNull();
}
```

### Тест с фикстурой
```csharp
public class MyTests : IClassFixture<TestConfigFixture>
{
    private readonly TestConfigFixture _fixture;
    
    public MyTests(TestConfigFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public void Test_WithFixture()
    {
        var configPath = _fixture.ConfigPath;
        // Используем configPath в тесте
    }
}
```

## 🔧 Покрытие кода

После запуска тестов с покрытием:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

Отчёт будет в:
```
konserva-app.Tests/TestResults/**/coverage.cobertura.xml
```

Для просмотра отчёта в формате HTML:
```bash
# Установите tool (если ещё не установлен)
dotnet tool install --global dotnet-reportgenerator-globaltool

# Сгенерируйте HTML отчёт
reportgenerator -reports:"konserva-app.Tests/TestResults/**/coverage.cobertura.xml" 
                -targetdir:"TestResults/CoverageReport" 
                -reporttypes:Html
```

## 🚀 CI/CD Интеграция

Тесты автоматически запускаются в GitHub Actions при каждом commit.

Файл workflow: `.github/workflows/build.yml`

```yaml
- name: Run Tests
  run: dotnet test --no-restore --verbosity normal
```

## 📊 Статистика

| Тип тестов | Количество |
|------------|------------|
| Unit тесты | 20+ |
| Интеграционные | 0 |
| E2E | 0 |

## 🎯 Планы

- [ ] Добавить тесты для McServerProcess
- [ ] Добавить тесты для ConfigService
- [ ] Добавить тесты для ServerStorageService
- [ ] Добавить интеграционные тесты API
- [ ] Добавить E2E тесты создания сервера
