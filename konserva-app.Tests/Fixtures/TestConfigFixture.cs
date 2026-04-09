using System;
using System.IO;

namespace Konserva.Tests.Fixtures;

/// <summary>
/// Фикстура для тестов с конфигурацией.
/// Создаёт временную директорию и путь к config.json.
/// Используется через ICollectionFixture в CollectionDefinition.
/// </summary>
public class TestConfigFixture : IDisposable
{
    public string TestDirectory { get; }
    public string ConfigPath { get; }
    public string ServersPath { get; }

    public TestConfigFixture()
    {
        TestDirectory = Path.Combine(
            Path.GetTempPath(),
            $"KonservaTests_{Guid.NewGuid():N}");

        Directory.CreateDirectory(TestDirectory);
        ConfigPath = Path.Combine(TestDirectory, "config.json");
        ServersPath = Path.Combine(TestDirectory, "servers.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TestDirectory))
                Directory.Delete(TestDirectory, true);
        }
        catch
        {
            // Игнорируем ошибки очистки
        }
    }
}
