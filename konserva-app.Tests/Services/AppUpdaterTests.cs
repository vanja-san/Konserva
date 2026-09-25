using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Konserva.Services;
using Moq;
using Moq.Protected;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты проверки целостности авто-обновления (<see cref="AppUpdater"/>).
/// <para>
/// Обновление применяется к файлам установки, поэтому архив без корректного
/// SHA-256 в манифесте обязан отклоняться: иначе подменённый ассет с CDN
/// выполнялся бы на машине пользователя. Именно эти сценарии и проверяются —
/// без обращения к сети.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class AppUpdaterTests : IDisposable
{
    private static readonly string WorkDir =
        Path.Combine(AppContext.BaseDirectory, "downloads", "KonservaUpdate");

    private static byte[] CreateZipBytes()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Konserva.exe");
            using var stream = entry.Open();
            stream.Write("not-a-real-exe"u8);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// HttpClient, отдающий фиксированные байты (200 OK).
    /// </summary>
    private static HttpClient CreateClient(byte[] payload)
    {
        // Loose, а не Strict: HttpClient.Dispose() дёргает Dispose у обработчика,
        // а у Strict-мока нет на это настройки и тест падает на уборке.
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            });

        return new HttpClient(handler.Object);
    }

    private static string Sha256Of(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));

    [Fact]
    public async Task ApplyAsync_WithoutSha256_RefusesToInstall()
    {
        // Arrange
        using var httpClient = CreateClient(CreateZipBytes());
        var updater = new AppUpdater(httpClient);
        var info = new Konserva.Models.UpdateInfo
        {
            IsAvailable = true,
            NewVersion = "99.0.0",
            CurrentVersion = "1.0.0",
            DownloadUrl = "https://example.com/update.zip",
            AssetName = "update.zip",
            Sha256 = string.Empty
        };

        // Act
        var result = await updater.ApplyAsync(info);

        // Assert: запрос не должен был состояться, файлы — не появиться
        result.Should().BeFalse();
        Directory.Exists(Path.Combine(WorkDir, "extracted")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WithMalformedSha256_RefusesToInstall()
    {
        using var httpClient = CreateClient(CreateZipBytes());
        var updater = new AppUpdater(httpClient);
        var info = new Konserva.Models.UpdateInfo
        {
            NewVersion = "99.0.0",
            CurrentVersion = "1.0.0",
            DownloadUrl = "https://example.com/update.zip",
            AssetName = "update.zip",
            Sha256 = "not-a-hash"
        };

        var result = await updater.ApplyAsync(info);

        result.Should().BeFalse();
        Directory.Exists(Path.Combine(WorkDir, "extracted")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WithWrongSha256_RefusesToInstall()
    {
        // Arrange: корректный по формату, но неверный хэш
        using var httpClient = CreateClient(CreateZipBytes());
        var updater = new AppUpdater(httpClient);
        var info = new Konserva.Models.UpdateInfo
        {
            NewVersion = "99.0.0",
            CurrentVersion = "1.0.0",
            DownloadUrl = "https://example.com/update.zip",
            AssetName = "update.zip",
            Sha256 = new string('a', 64)
        };

        // Act
        var result = await updater.ApplyAsync(info);

        // Assert: распаковка не выполнена, скачанный архив удалён
        result.Should().BeFalse();
        Directory.Exists(Path.Combine(WorkDir, "extracted")).Should().BeFalse();
        File.Exists(Path.Combine(WorkDir, "update.zip")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WithSizeMismatch_RefusesToInstall()
    {
        var payload = CreateZipBytes();
        using var httpClient = CreateClient(payload);
        var updater = new AppUpdater(httpClient);
        var info = new Konserva.Models.UpdateInfo
        {
            NewVersion = "99.0.0",
            CurrentVersion = "1.0.0",
            DownloadUrl = "https://example.com/update.zip",
            AssetName = "update.zip",
            Sha256 = Sha256Of(payload),
            SizeBytes = payload.Length + 1
        };

        var result = await updater.ApplyAsync(info);

        result.Should().BeFalse();
        Directory.Exists(Path.Combine(WorkDir, "extracted")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_WithMatchingSha256_PassesVerification()
    {
        // Arrange: хэш и размер совпадают — проверка целостности обязана пройти,
        // и распаковка должна состояться (дальше падает уже запуск .bat,
        // поскольку в тесте нет WPF Application).
        var payload = CreateZipBytes();
        using var httpClient = CreateClient(payload);
        var updater = new AppUpdater(httpClient);
        var info = new Konserva.Models.UpdateInfo
        {
            NewVersion = "99.0.0",
            CurrentVersion = "1.0.0",
            DownloadUrl = "https://example.com/update.zip",
            AssetName = "update.zip",
            Sha256 = Sha256Of(payload),
            SizeBytes = payload.Length
        };

        // Act
        await updater.ApplyAsync(info);

        // Assert
        Directory.Exists(Path.Combine(WorkDir, "extracted"))
            .Should().BeTrue("проверка SHA-256 и размера должна была пройти и дойти до распаковки");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WorkDir))
                Directory.Delete(WorkDir, true);

            // Убираем за собой сгенерированные .bat-скрипты обновления
            var downloads = Path.Combine(AppContext.BaseDirectory, "downloads");
            if (Directory.Exists(downloads))
            {
                foreach (var bat in Directory.EnumerateFiles(downloads, "KonservaUpdate_*.bat"))
                {
                    try { File.Delete(bat); } catch { /* не критично */ }
                }
            }
        }
        catch
        {
            // Уборка не должна ронять тест
        }
    }
}
