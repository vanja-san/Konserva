using System.ComponentModel.DataAnnotations;

namespace Konserva.Models;

/// <summary>
/// Настройки сервера
/// </summary>
public class ServerSettings
{
    private int _ramMin = 1024;
    private int _ramMax = 4096;

    /// <summary>
    /// Минимум RAM (MB). Минимум: 256, Максимум: RamMax
    /// </summary>
    [Range(256, int.MaxValue)]
    public int RamMin
    {
        get => _ramMin;
        set
        {
            _ramMin = Math.Clamp(value, 256, RamMax);
        }
    }

    /// <summary>
    /// Максимум RAM (MB). Минимум: RamMin, Максимум: 65536
    /// </summary>
    [Range(1, 65536)]
    public int RamMax
    {
        get => _ramMax;
        set
        {
            _ramMax = Math.Clamp(value, RamMin, 65536);
        }
    }

    /// <summary>
    /// Количество ядер CPU. Минимум: 1, Максимум: количество доступных
    /// </summary>
    [Range(1, 128)]
    public int CpuCores
    {
        get => field;
        set => field = Math.Clamp(value, 1, Environment.ProcessorCount);
    } = Environment.ProcessorCount;

    /// <summary>
    /// ID выбранной Java версии (если null, используется Java по умолчанию)
    /// </summary>
    public string? JavaId { get; set; }

    /// <summary>
    /// Автоматический выбор версии Java на основе версии Minecraft
    /// </summary>
    public bool JavaAutoSelect { get; set; } = true;

    /// <summary>
    /// Аргументы запуска Java
    /// </summary>
    public List<string> JavaArgs { get; set; } = [];

    /// <summary>
    /// Автоматический рестарт при остановке
    /// </summary>
    public bool AutoRestart { get; set; }

    /// <summary>
    /// Задержка перед автоматическим рестартом (сек). Минимум: 0, Максимум: 3600
    /// </summary>
    [Range(0, 3600)]
    public int AutoRestartDelay { get; set; } = 5;

    /// <summary>
    /// Копирование настроек
    /// </summary>
    public ServerSettings Clone() => new()
    {
        RamMin = RamMin,
        RamMax = RamMax,
        CpuCores = CpuCores,
        JavaId = JavaId,
        JavaAutoSelect = JavaAutoSelect,
        JavaArgs = [.. JavaArgs],
        AutoRestart = AutoRestart,
        AutoRestartDelay = AutoRestartDelay
    };

    /// <summary>
    /// Валидация настроек
    /// </summary>
    public bool Validate() => RamMin >= 256 && RamMax >= RamMin && RamMax <= 65536 &&
                               CpuCores >= 1 && CpuCores <= 128 &&
                               AutoRestartDelay >= 0 && AutoRestartDelay <= 3600;
}
