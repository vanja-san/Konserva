namespace Konserva.Models;

/// <summary>
/// Result of applying a mod update.
/// </summary>
public class ModUpdateApplyResult
{
    public bool Success { get; private init; }

    /// <summary>Path to the backup of the old file (if one was created).</summary>
    public string? BackupPath { get; private init; }

    /// <summary>Name of the new file after the update.</summary>
    public string? NewFileName { get; private init; }

    /// <summary>Error text on failure.</summary>
    public string? Error { get; private init; }

    public static ModUpdateApplyResult Ok(string? backupPath = null, string? newFileName = null) =>
        new() { Success = true, BackupPath = backupPath, NewFileName = newFileName };

    public static ModUpdateApplyResult Fail(string error) =>
        new() { Success = false, Error = error };
}
