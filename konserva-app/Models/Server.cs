using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Konserva.Utilities;

namespace Konserva.Models;

/// <summary>
/// Статус сервера
/// </summary>
public enum ServerStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error
}

/// <summary>
/// Модель сервера Minecraft
/// </summary>
public class Server : ObservableObject
{
    private static int _idCounter;
    private string _name = string.Empty;
    private int _port = 25565;
    private bool _errorDialogShown; // Флаг: показан ли диалог ошибки
    private ServerStatus _status = ServerStatus.Stopped;
    private string _installStatus = string.Empty;

    /// <summary>
    /// Уникальный идентификатор сервера
    /// </summary>
    public string Id { get; init; } = GenerateShortId();

    /// <summary>
    /// Название сервера (1-100 символов)
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name
    {
        get => _name;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (SetProperty(ref _name, trimmed.Length > 100 ? trimmed[..100] : trimmed))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    /// <summary>
    /// Путь к папке сервера
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Версия Minecraft
    /// </summary>
    public string McVersion { get; set; } = string.Empty;

    /// <summary>
    /// Модлоадер (Vanilla, Forge, NeoForge, Fabric, Quilt)
    /// </summary>
    public ModLoader ModLoader { get; set; } = new();

    /// <summary>
    /// Настройки сервера
    /// </summary>
    public ServerSettings Settings { get; set; } = new();

    /// <summary>
    /// Последнее время игры
    /// </summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>
    /// Дата создания сервера
    /// </summary>
    public DateTime CreatedAt { get; init; } = SystemTime.Now;

    /// <summary>
    /// Порт сервера (1-65535)
    /// </summary>
    [Range(1, 65535)]
    public int Port
    {
        get => _port;
        set => _port = Math.Clamp(value, 1, 65535);
    }

    /// <summary>
    /// Автозапуск сервера
    /// </summary>
    public bool AutoStart { get; set; }

    /// <summary>
    /// Номер сборки сервера (для Paper)
    /// </summary>
    public int? ServerBuild { get; set; }

    // Временные данные (не сохраняются, JsonIgnore)
    [JsonIgnore]
    public ServerStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    [JsonIgnore]
    public string InstallStatus
    {
        get => _installStatus;
        set => SetProperty(ref _installStatus, value ?? string.Empty);
    }

    private static string GenerateShortId() =>
        $"{SystemTime.Now:yyyyMMdd}-{Interlocked.Increment(ref _idCounter):X4}";

    /// <summary>
    /// Инициализация счётчика ID при загрузке существующих серверов
    /// </summary>
    /// <param name="existingServers">Список загруженных серверов</param>
    public static void InitializeIdCounter(IEnumerable<Server> existingServers)
    {
        // Находим максимальный числовой ID из существующих серверов
        var maxId = existingServers
            .Select(s => s.Id)
            .Where(id => id != null)
            .Select(id =>
            {
                // Формат ID: yyyyMMdd-XXXX (где XXXX - шестнадцатеричное число)
                var parts = id.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var numericId))
                {
                    return numericId;
                }
                return 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        // Устанавливаем счётчик на максимальное значение, чтобы новые ID были уникальными
        Interlocked.Exchange(ref _idCounter, maxId);
    }

    /// <summary>
    /// Проверка: сервер запущен
    /// </summary>
    public bool IsRunning => Status is ServerStatus.Running or ServerStatus.Starting;

    /// <summary>
    /// Краткое описание сервера
    /// </summary>
    public string Description => $"{McVersion} • {ModLoader.Type}{(ServerBuild.HasValue ? $" (build {ServerBuild})" : "")}";

    /// <summary>
    /// Флаг: показан ли диалог ошибки запуска
    /// </summary>
    public bool ErrorDialogShown
    {
        get => _errorDialogShown;
        set => _errorDialogShown = value;
    }

    /// <summary>
    /// Сбросить флаг ошибки
    /// </summary>
    public void ResetErrorDialog() => _errorDialogShown = false;

    /// <summary>
    /// Клонирование сервера
    /// </summary>
    public Server Clone() => new()
    {
        Name = Name,
        Path = Path,
        McVersion = McVersion,
        ModLoader = ModLoader.Clone(),  // Создаём копию ModLoader
        Settings = Settings.Clone(),
        LastPlayed = LastPlayed,
        Port = Port,
        AutoStart = AutoStart,
        ServerBuild = ServerBuild
    };

    /// <summary>
    /// Валидация сервера
    /// </summary>
    public bool Validate() => !string.IsNullOrWhiteSpace(Name) &&
                               Port >= 1 && Port <= 65535 &&
                               Settings.Validate();
}
