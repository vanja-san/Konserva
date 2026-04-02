using System.ComponentModel.DataAnnotations;
using Konserva.Utilities;

namespace Konserva.Models;

/// <summary>
/// Настройки сервера
/// </summary>
public class ServerSettings
{
    private int _ramMin = Constants.DefaultRamMinMb;
    private int _ramMax = Constants.DefaultRamMaxMb;

    /// <summary>
    /// Минимум RAM (MB). Минимум: 256, Максимум: RamMax
    /// </summary>
    [Range(Constants.MinRamMb, int.MaxValue)]
    public int RamMin
    {
        get => _ramMin;
        set
        {
            _ramMin = Math.Clamp(value, Constants.MinRamMb, RamMax);
        }
    }

    /// <summary>
    /// Максимум RAM (MB). Минимум: RamMin, Максимум: 65536
    /// </summary>
    [Range(1, Constants.MaxRamMb)]
    public int RamMax
    {
        get => _ramMax;
        set
        {
            _ramMax = Math.Clamp(value, RamMin, Constants.MaxRamMb);
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
    [Range(0, Constants.MaxAutoRestartDelaySec)]
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
    public bool Validate() => RamMin >= Constants.MinRamMb && RamMax >= RamMin && RamMax <= Constants.MaxRamMb &&
                               CpuCores >= 1 && CpuCores <= 128 &&
                               AutoRestartDelay >= 0 && AutoRestartDelay <= Constants.MaxAutoRestartDelaySec;
}
