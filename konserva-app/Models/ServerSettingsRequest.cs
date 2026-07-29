namespace Konserva.Models;

public record ServerSettingsRequest(
    string? Name,
    string? RamMinStr,
    string? RamMaxStr,
    bool? AutoRestart,
    string? AutoRestartDelayStr,
    bool JavaAutoSelect,
    string? JavaId,
    string JvmArgs
);
