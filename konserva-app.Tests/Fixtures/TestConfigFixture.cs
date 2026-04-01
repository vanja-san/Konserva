using System;
using System.IO;

namespace Konserva.Tests.Fixtures;

/// <summary>
/// Фикстура для тестов с конфигурацией
/// Используется для общих ресурсов между тестами
/// </summary>
public class TestConfigFixture : IDisposable
{
    public string TestDirectory { get; private set; }
    public string ConfigPath { get; private set; }
    
    public TestConfigFixture()
    {
        // Создаём временную дирекорию для тестов
        TestDirectory = Path.Combine(
            Path.GetTempPath(), 
            $"KonservaTests_{Guid.NewGuid()}");
        
        Directory.CreateDirectory(TestDirectory);
        ConfigPath = Path.Combine(TestDirectory, "config.json");
    }
    
    public void Dispose()
    {
        // Очищаем за собой
        try
        {
            if (Directory.Exists(TestDirectory))
            {
                Directory.Delete(TestDirectory, true);
            }
        }
        catch
        {
            // Игнорируем ошибки очистки
        }
    }
}
