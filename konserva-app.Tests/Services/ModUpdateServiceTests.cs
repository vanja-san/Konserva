using Konserva.Services;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Konserva.Tests.Services;

public class ModUpdateServiceTests : IDisposable
{
    private const string RemoteHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _dir;
    private readonly Mock<IModRepositoryApi> _repo = new();
    private readonly Mock<IModFileDownloader> _downloader = new();

    public ModUpdateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"konserva_modupd_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* ignore cleanup errors */ }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsUpdate_WhenNewerVersionExists()
    {
        var path = WriteFile("example.jar", "old");
        var localHash = HashOf("old");
        SetupRepo(
            current: new() { [localHash] = Version("1.0.0", localHash, "2024-01-01T00:00:00Z") },
            latest: new() { [localHash] = Version("2.0.0", RemoteHash, "2024-06-01T00:00:00Z", fileName: "example-2.0.0.jar") });

        var result = await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Fabric, "1.21.1", ModUpdateChannel.Release);

        result.Should().ContainKey(path);
        var info = result[path];
        info.LatestVersion.Should().Be("2.0.0");
        info.CurrentVersion.Should().Be("1.0.0");
        info.LatestHash.Should().Be(RemoteHash);
        info.DownloadFileName.Should().Be("example-2.0.0.jar");
        info.DownloadUrl.Should().Be("https://cdn.modrinth.com/data/proj/versions/ver-2.0.0/example-2.0.0.jar");
        info.ProjectId.Should().Be("proj");
        info.VersionPageUrl.Should().Contain("modrinth.com/project/proj/version/ver-2.0.0");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NoUpdate_WhenHashUnchanged()
    {
        var path = WriteFile("same.jar", "same");
        var localHash = HashOf("same");
        SetupRepo(
            current: new() { [localHash] = Version("1.0.0", localHash, "2024-01-01T00:00:00Z") },
            latest: new() { [localHash] = Version("1.0.0", localHash, "2024-06-01T00:00:00Z") });

        var result = await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Fabric, "1.21.1", ModUpdateChannel.Release);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NoUpdate_WhenRemoteIsOlder()
    {
        var path = WriteFile("downgrade.jar", "local-new");
        var localHash = HashOf("local-new");
        SetupRepo(
            current: new() { [localHash] = Version("3.0.0", localHash, "2024-09-01T00:00:00Z") },
            latest: new() { [localHash] = Version("2.0.0", RemoteHash, "2024-06-01T00:00:00Z") });

        var result = await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Fabric, "1.21.1", ModUpdateChannel.Release);

        result.Should().BeEmpty("версия с меньшей датой — это даунгрейд");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NoUpdate_WhenRemoteIsSameDate()
    {
        var path = WriteFile("samedate.jar", "x");
        var localHash = HashOf("x");
        SetupRepo(
            current: new() { [localHash] = Version("1.0.0", localHash, "2024-06-01T00:00:00Z") },
            latest: new() { [localHash] = Version("1.0.1", RemoteHash, "2024-06-01T00:00:00Z") });

        var result = await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Fabric, "1.21.1", ModUpdateChannel.Release);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NoUpdate_WhenLatestMissing()
    {
        var path = WriteFile("unknown.jar", "unknown");
        SetupRepo(current: new(), latest: new());

        var result = await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Fabric, "1.21.1", ModUpdateChannel.Release);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NoUpdate_WhenLatestHasNoSha512()
    {
        var path = WriteFile("nohash.jar", "nohash");
        var localHash = HashOf("nohash");
        SetupRepo(
            current: new() { [localHash] = Version("1.0.0", localHash, "2024-01-01T00:00:00Z") },
            latest: new() { [localHash] = Version("2.0.0", sha512: null, "2024-06-01T00:00:00Z") });

        var result = await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Fabric, "1.21.1", ModUpdateChannel.Release);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ModLoaderType.Vanilla)]
    [InlineData(ModLoaderType.Paper)]
    public async Task CheckForUpdatesAsync_ReturnsEmpty_ForLoadersThatDoNotLoadMods(ModLoaderType loader)
    {
        var path = WriteFile("mod.jar", "mod");

        var result = await CreateService().CheckForUpdatesAsync(
            [path], loader, "1.21.1", ModUpdateChannel.Release);

        result.Should().BeEmpty();
        _repo.Verify(r => r.GetLatestVersionsAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsEmpty_WhenNoGameVersion()
    {
        var path = WriteFile("mod.jar", "mod");

        var result = await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Fabric, string.Empty, ModUpdateChannel.Release);

        result.Should().BeEmpty();
        _repo.Verify(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SkipsMissingFiles_WithoutCallingRepo()
    {
        var missing = Path.Combine(_dir, "ghost.jar");

        var result = await CreateService().CheckForUpdatesAsync(
            [missing], ModLoaderType.Fabric, "1.21.1", ModUpdateChannel.Release);

        result.Should().BeEmpty();
        _repo.Verify(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_PassesLoaderGameVersionAndChannel()
    {
        var path = WriteFile("quilt.jar", "q");
        IReadOnlyCollection<string>? loaders = null;
        string? gameVersion = null;
        IReadOnlyCollection<string>? versionTypes = null;

        _repo.Setup(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ModrinthVersion>());
        _repo.Setup(r => r.GetLatestVersionsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<string> _, IReadOnlyCollection<string> l, string g, IReadOnlyCollection<string> t, CancellationToken _) =>
            {
                loaders = l;
                gameVersion = g;
                versionTypes = t;
            })
            .ReturnsAsync(new Dictionary<string, ModrinthVersion>());

        await CreateService().CheckForUpdatesAsync(
            [path], ModLoaderType.Quilt, "1.20.1", ModUpdateChannel.Beta);

        loaders.Should().Equal("quilt", "fabric");
        gameVersion.Should().Be("1.20.1");
        versionTypes.Should().Equal("release", "beta");
    }

    // --- ResolveModMetadataAsync ---

    [Fact]
    public async Task ResolveModMetadataAsync_ReturnsTitleAndVersion_FromRepository()
    {
        var path = WriteFile("mod.jar", "mod");
        var localHash = HashOf("mod");
        var version = new ModrinthVersion
        {
            Id = "ver1",
            ProjectId = "proj1",
            VersionNumber = "1.5.0",
            Files = []
        };
        _repo.Setup(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ModrinthVersion> { [localHash] = version });
        _repo.Setup(r => r.GetProjectsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ModrinthProject>
            {
                ["proj1"] = new() { Id = "proj1", Slug = "my-mod", Title = "My Mod" }
            });

        var result = await CreateService().ResolveModMetadataAsync([path]);

        result.Should().ContainKey(path);
        result[path].Title.Should().Be("My Mod");
        result[path].Version.Should().Be("1.5.0");
    }

    [Fact]
    public async Task ResolveModMetadataAsync_SkipsUnknownFiles()
    {
        var path = WriteFile("unknown.jar", "unk");
        _repo.Setup(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ModrinthVersion>());

        var result = await CreateService().ResolveModMetadataAsync([path]);

        result.Should().BeEmpty();
        _repo.Verify(r => r.GetProjectsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveModMetadataAsync_UsesEmptyTitle_WhenProjectMissing()
    {
        var path = WriteFile("mod.jar", "mod");
        var localHash = HashOf("mod");
        var version = new ModrinthVersion
        {
            Id = "ver1",
            ProjectId = "proj1",
            VersionNumber = "1.5.0",
            Files = []
        };
        _repo.Setup(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ModrinthVersion> { [localHash] = version });
        _repo.Setup(r => r.GetProjectsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ModrinthProject>());

        var result = await CreateService().ResolveModMetadataAsync([path]);

        result[path].Title.Should().BeEmpty();
        result[path].Version.Should().Be("1.5.0");
    }

    [Fact]
    public async Task ResolveModMetadataAsync_SkipsMissingFiles_WithoutCallingRepo()
    {
        var missing = Path.Combine(_dir, "ghost.jar");

        var result = await CreateService().ResolveModMetadataAsync([missing]);

        result.Should().BeEmpty();
        _repo.Verify(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.GetProjectsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- ApplyUpdateAsync ---

    [Fact]
    public async Task ApplyUpdateAsync_ReplacesFile_AndCreatesBackup()
    {
        var target = WriteFile("mod.jar", "old");
        var backupDir = Path.Combine(_dir, "backups");

        SetupDownloaderToWrite("new");
        var result = await CreateService().ApplyUpdateAsync(MakeUpdate(target, HashOf("new"), "2.0.0"), backupDir);

        result.Success.Should().BeTrue();
        result.NewFileName.Should().Be("mod-2.0.0.jar");
        File.Exists(target).Should().BeFalse("старый файл должен быть заменён новым именем");
        var newPath = Path.Combine(_dir, "mod-2.0.0.jar");
        File.ReadAllText(newPath).Should().Be("new");
        Directory.Exists(backupDir).Should().BeTrue();
        result.BackupPath.Should().NotBeNull();
        File.ReadAllText(result.BackupPath!).Should().Be("old");
    }

    [Fact]
    public async Task ApplyUpdateAsync_Fails_OnHashMismatch_AndKeepsOriginal()
    {
        var target = WriteFile("mod.jar", "old");
        var backupDir = Path.Combine(_dir, "backups");

        SetupDownloaderToWrite("different");
        var result = await CreateService().ApplyUpdateAsync(MakeUpdate(target, "wrong", "2.0.0"), backupDir);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("SHA-512");
        File.ReadAllText(target).Should().Be("old");
        File.Exists(Path.Combine(_dir, "mod-2.0.0.jar")).Should().BeFalse();
        Directory.Exists(backupDir).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyUpdateAsync_UpdatesDisabledJar_WithNewName()
    {
        var target = WriteFile("mod.jar.disabled", "old");
        var backupDir = Path.Combine(_dir, "backups");

        SetupDownloaderToWrite("new");
        var result = await CreateService().ApplyUpdateAsync(MakeUpdate(target, HashOf("new"), "2.0.0"), backupDir);

        result.Success.Should().BeTrue();
        result.NewFileName.Should().Be("mod-2.0.0.jar.disabled");
        File.Exists(target).Should().BeFalse("старый .disabled файл должен быть удалён");
        var newPath = Path.Combine(_dir, "mod-2.0.0.jar.disabled");
        File.ReadAllText(newPath).Should().Be("new");
    }

    [Fact]
    public async Task ApplyUpdateAsync_Fails_WhenDownloadThrows()
    {
        var target = WriteFile("mod.jar", "old");

        _downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));

        var result = await CreateService().ApplyUpdateAsync(
            MakeUpdate(target, HashOf("new"), "2.0.0"), Path.Combine(_dir, "backup"));

        result.Success.Should().BeFalse();
        File.ReadAllText(target).Should().Be("old");
        File.Exists(Path.Combine(_dir, "mod-2.0.0.jar")).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyUpdateAsync_SkipsBackup_WhenTargetMissing()
    {
        var oldPath = Path.Combine(_dir, "new-mod.jar");

        SetupDownloaderToWrite("content");
        var result = await CreateService().ApplyUpdateAsync(
            MakeUpdate(oldPath, HashOf("content"), "1.0.0"), Path.Combine(_dir, "backup"));

        result.Success.Should().BeTrue();
        result.BackupPath.Should().BeNull();
        File.Exists(oldPath).Should().BeFalse();
        File.ReadAllText(Path.Combine(_dir, "mod-1.0.0.jar")).Should().Be("content");
    }

    [Fact]
    public async Task ApplyUpdateAsync_CleansTempFile_OnHashFailure()
    {
        var target = WriteFile("mod.jar", "old");
        var dir = Path.GetDirectoryName(target)!;

        SetupDownloaderToWrite("new");
        await CreateService().ApplyUpdateAsync(MakeUpdate(target, "bad", "2.0.0"), Path.Combine(_dir, "backup"));

        Directory.GetFiles(dir, "*.tmp").Should().BeEmpty("temp-файл должен быть удалён после ошибки");
    }

    private void SetupDownloaderToWrite(string content) =>
        _downloader
            .Setup(d => d.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string dest, CancellationToken _) => File.WriteAllText(dest, content))
            .Returns(Task.CompletedTask);

    private ModUpdateService CreateService() => new(_repo.Object, _downloader.Object);

    private static ModUpdateInfo MakeUpdate(string filePath, string sha512, string versionNumber) => new()
    {
        FilePath = filePath,
        CurrentHash = "dummy",
        VersionId = $"ver-{versionNumber}",
        ProjectId = "proj",
        LatestVersion = versionNumber,
        DownloadUrl = $"https://cdn.modrinth.com/data/proj/versions/ver-{versionNumber}/mod-{versionNumber}.jar",
        DownloadFileName = $"mod-{versionNumber}.jar",
        LatestHash = sha512
    };

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string HashOf(string content) =>
        Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(content)));

    private void SetupRepo(
        Dictionary<string, ModrinthVersion> current,
        Dictionary<string, ModrinthVersion> latest)
    {
        _repo.Setup(r => r.GetCurrentVersionsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _repo.Setup(r => r.GetLatestVersionsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);
    }

    private static ModrinthVersion Version(
        string versionNumber,
        string? sha512,
        string datePublished,
        string fileName = "file.jar",
        string projectId = "proj")
    {
        return new ModrinthVersion
        {
            Id = $"ver-{versionNumber}",
            ProjectId = projectId,
            VersionNumber = versionNumber,
            VersionType = "release",
            DatePublished = DateTimeOffset.Parse(datePublished, CultureInfo.InvariantCulture),
            Files = sha512 is null
                ? []
                :
                [
                    new ModrinthFile
                    {
                        Url = $"https://cdn.modrinth.com/data/proj/versions/ver-{versionNumber}/{fileName}",
                        FileName = fileName,
                        Primary = true,
                        Hashes = new ModrinthHashes { Sha512 = sha512 }
                    }
                ]
        };
    }
}
