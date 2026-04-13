using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace Konserva.Utilities;

/// <summary>
/// Утилиты для работы с потоками.
/// </summary>
public static class StreamUtilities
{
    /// <summary>
    /// Возвращает распакованный поток в зависимости от Content-Encoding.
    /// Поддерживает gzip и deflate. Если кодирование не указано — возвращает исходный поток.
    /// </summary>
    /// <param name="compressedStream">Исходный поток</param>
    /// <param name="contentEncoding">Заголовок Content-Encoding (коллекция или строка)</param>
    /// <param name="leaveOpen">Оставлять ли исходный поток открытым после dispose</param>
    public static Stream GetDecompressedStream(Stream compressedStream, IEnumerable<string>? contentEncoding, bool leaveOpen = true)
    {
        var encoding = contentEncoding?.FirstOrDefault()?.ToLowerInvariant();

        if (encoding == "gzip")
            return new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen);

        if (encoding == "deflate")
            return new DeflateStream(compressedStream, CompressionMode.Decompress, leaveOpen);

        return compressedStream;
    }

    /// <summary>
    /// Перегрузка для HttpContentHeaders.
    /// </summary>
    public static Stream GetDecompressedStream(Stream compressedStream, HttpContentHeaders? headers, bool leaveOpen = true)
    {
        var encoding = headers?.ContentEncoding?.FirstOrDefault()?.ToLowerInvariant();

        if (encoding == "gzip")
            return new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen);

        if (encoding == "deflate")
            return new DeflateStream(compressedStream, CompressionMode.Decompress, leaveOpen);

        return compressedStream;
    }
}
