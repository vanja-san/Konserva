namespace Konserva.Models;

/// <summary>
/// Результат применения обновления мода.
/// </summary>
public class ModUpdateApplyResult
{
    public bool Success { get; private init; }

    /// <summary>Путь к резервной копии старого файла (если создавалась).</summary>
    public string? BackupPath { get; private init; }

    /// <summary>Имя нового файла после обновления.</summary>
    public string? NewFileName { get; private init; }

    /// <summary>Текст ошибки при неудаче.</summary>
    public string? Error { get; private init; }

    public static ModUpdateApplyResult Ok(string? backupPath = null, string? newFileName = null) =>
        new() { Success = true, BackupPath = backupPath, NewFileName = newFileName };

    public static ModUpdateApplyResult Fail(string error) =>
        new() { Success = false, Error = error };
}
