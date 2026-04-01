# Konserva Build Guide

## Быстрая сборка

### Windows (PowerShell/CMD)

```bash
# Сборка publish-версии (self-contained, ~72 MB)
build-publish.bat publish

# Сборка Release-версии (требует .NET)
build-publish.bat release

# Сборка portable-версии (~50 MB, требует .NET runtime)
build-publish.bat portable

# Очистка
build-publish.bat clean
```

## Типы сборок

| Тип | Размер | .NET Required | Описание |
|-----|--------|---------------|----------|
| **Self-contained** | ~72 MB | ❌ Нет | Всё включено, готово к запуску |
| **Portable** | ~50 MB | ✅ Да | Требуется .NET 10 Runtime |
| **Release** | - | ✅ Да | Для разработки, требует .NET SDK |

## GitHub Actions

Для ручной сборки через GitHub Actions:

1. Перейдите на вкладку **Actions**
2. Выберите workflow **"Build Release"**
3. Нажмите **"Run workflow"**
4. Выберите параметры:
   - `version` - номер версии (например, `1.0.1`)
   - `build_self_contained` - собрать self-contained версию
   - `build_portable` - собрать portable версию
5. Нажмите **"Run workflow"**
6. Скачайте артефакты из раздела **"Artifacts"**

## Требования

### Для сборки
- .NET 10 SDK
- Windows x64

### Для запуска (self-contained)
- Windows x64
- Ничего больше не требуется

### Для запуска (portable)
- Windows x64
- .NET 10 Runtime ([скачать](https://dotnet.microsoft.com/download/dotnet/10.0))

## Структура выходных файлов

```
publish/
├── Release/           # Self-contained версия
│   ├── Konserva.exe   # ~72 MB
│   └── README.md
└── Portable/          # Portable версия
    ├── Konserva.exe   # ~50 MB
    └── README.md
```

## Настройки компиляции

### Release (.csproj)
```xml
<Optimize>true</Optimize>
<DebugType>none</DebugType>
```

### Publish (командная строка)
```bash
-p:PublishSingleFile=true
-p:EnableCompressionInSingleFile=true
-p:PublishReadyToRun=true
```

## Устранение проблем

### Ошибка "dotnet not found"
Установите .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0

### Ошибка компиляции
```bash
# Очистите и пересоберите
build-publish.bat clean
build-publish.bat publish
```

### Большой размер файла
Используйте portable-версию (~50 MB вместо ~72 MB)
