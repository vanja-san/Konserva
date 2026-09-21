using System.IO;
using System.Security.Cryptography;

namespace Konserva.Utilities;

/// <summary>
/// Хеширование файлов для сверки с репозиторием (Modrinth использует SHA-512).
/// </summary>
public static class FileHash
{
    /// <summary>
    /// Возвращает SHA-512 файла в нижнем регистре hex (формат Modrinth).
    /// </summary>
    public static async Task<string> Sha512Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        var hash = await SHA512.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
