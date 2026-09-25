using System.IO;
using System.Security.Cryptography;

namespace Konserva.Tests.Utilities;

[Trait("Category", "Unit")]
public class FileHashTests : IDisposable
{
    private readonly string _dir;

    public FileHashTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"konserva_hash_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* ignore cleanup errors */ }
    }

    [Fact]
    public async Task Sha512Async_ReturnsKnownHash_ForAbc()
    {
        var path = Path.Combine(_dir, "abc.bin");
        await File.WriteAllTextAsync(path, "abc");

        var hash = await FileHash.Sha512Async(path);

        hash.Should().Be(
            "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a" +
            "2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f");
    }

    [Fact]
    public async Task Sha512Async_MatchesFramework_And_IsLowercaseHex()
    {
        var path = Path.Combine(_dir, "data.bin");
        var bytes = new byte[8193];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);

        var hash = await FileHash.Sha512Async(path);

        hash.Should().Be(Convert.ToHexStringLower(SHA512.HashData(bytes)));
        hash.Should().MatchRegex("^[0-9a-f]{128}$");
    }

    [Fact]
    public async Task Sha512Async_Throws_WhenFileMissing()
    {
        var path = Path.Combine(_dir, "nope.jar");

        var act = () => FileHash.Sha512Async(path);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
