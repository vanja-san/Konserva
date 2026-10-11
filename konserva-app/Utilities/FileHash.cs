using System.IO;
using System.Security.Cryptography;

namespace Konserva.Utilities;

/// <summary>
/// File hashing for verification against the repository (Modrinth uses SHA-512).
/// </summary>
public static class FileHash
{
    /// <summary>
    /// Returns the SHA-512 hash of a file as a lowercase hex string (Modrinth format).
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
