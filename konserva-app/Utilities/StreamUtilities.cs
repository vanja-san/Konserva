using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace Konserva.Utilities;

/// <summary>
/// Helpers for working with streams.
/// </summary>
public static class StreamUtilities
{
    /// <summary>
    /// Returns a decompressed stream depending on the Content-Encoding.
    /// Supports gzip and deflate. If no encoding is specified — returns the original stream.
    /// </summary>
    /// <param name="compressedStream">The source stream</param>
    /// <param name="contentEncoding">The Content-Encoding header (collection or string)</param>
    /// <param name="leaveOpen">Whether to keep the source stream open after dispose</param>
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
    /// Overload for HttpContentHeaders.
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
