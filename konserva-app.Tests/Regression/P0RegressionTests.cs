using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Moq;
using System.IO;
using System.Reflection;

namespace Konserva.Tests.Regression;

/// <summary>
/// Regression tests for the P0 defects found in review:
/// orphaned java processes, non-atomic persistence, cross-thread events,
/// WQL injection and unlocalized format strings.
/// </summary>
[Collection("Sequential")]
[Trait("Category", "Unit")]
public class P0RegressionTests
{
    private static Server CreateServer(string name = "Test", string path = @"C:\Servers\Test")
        => new()
        {
            Name = name,
            Path = path,
            McVersion = "1.21.1",
            ModLoader = new ModLoader { Type = ModLoaderType.Vanilla },
            Settings = new ServerSettings()
        };

    // ─── P0-1: a disposed wrapper must never start a process ───

    [Fact]
    public async Task StartAsync_AfterDispose_DoesNotThrow()
    {
        var installer = Mock.Of<IServerInstaller>();
        using var process = new McServerProcess(CreateServer(), null, installer);

        process.Dispose();

        // Would previously launch a real java.exe that nothing tracked.
        await process.Invoking(p => p.StartAsync()).Should().NotThrowAsync();
        process.Status.Should().Be(ServerStatus.Stopped);
    }

    [Fact]
    public void Dispose_AfterDispose_DoesNotThrow()
    {
        var installer = Mock.Of<IServerInstaller>();
        var process = new McServerProcess(CreateServer(), null, installer);

        process.Dispose();

        process.Invoking(p => p.Dispose()).Should().NotThrow();
    }

    // ─── P0-2: persistence must survive a corrupted file ───

    [Fact]
    public void FileBasedStore_RecoversFromCorruptedFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"konserva_p0_store_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "data.json");
            var store = new FileBasedStore<List<Server>>(file);

            store.Save([CreateServer("Alpha")]);
            store.Save([CreateServer("Alpha"), CreateServer("Beta")]);

            // Simulate a truncated write (crash / full disk).
            File.WriteAllText(file, "{ \"truncated\": [");

            var loaded = new FileBasedStore<List<Server>>(file).Load();

            // Returning null here would silently empty the whole server list.
            loaded.Should().NotBeNull();
            loaded.Should().HaveCountGreaterThan(0);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void FileBasedStore_Save_DoesNotLeaveTempFileBehind()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"konserva_p0_tmp_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "data.json");
            var store = new FileBasedStore<List<Server>>(file);

            store.Save([CreateServer()]);

            File.Exists(file).Should().BeTrue();
            File.Exists(file + ".tmp").Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    // ─── P0-4: the server path must never be interpolated into WQL ───

    [Theory]
    [InlineData(@"C:\Servers\x' OR CommandLine LIKE '%")]
    [InlineData(@"C:\Servers\100%_server")]
    [InlineData(@"C:\Servers\a'b'c")]
    public void FindJavaPidsForPath_HandlesHostilePathNames(string hostilePath)
    {
        // A path used to be embedded in a WQL LIKE literal, where an apostrophe ended
        // the string and turned the predicate into a match-all. The query is now fixed
        // and the path is matched in managed code, so no input can break out.
        var method = typeof(McServerManager).GetMethod(
            "CommandLineReferencesPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("the managed-code matcher must exist");
        var matcher = method!;

        var matches = (bool)matcher.Invoke(null, [@"-Dfoo=C:\\Servers\\Other", hostilePath])!;
        matches.Should().BeFalse("an unrelated command line must not match a hostile path name");

        var selfMatch = (bool)matcher.Invoke(null, [$"-jar \"{hostilePath}\\server.jar\"", hostilePath])!;
        selfMatch.Should().BeTrue("the owning command line must still be matched");
    }

    // ─── P0-8: a missing translation must not throw ───

    [Fact]
    public void Localization_Get_UnknownKeyWithArgs_DoesNotThrow()
    {
        var action = () => LocalizationManager.Get("This_Key_Does_Not_Exist_12345", "arg1", 42);

        // string.Format over the raw key (which has no placeholder) used to throw
        // FormatException out of ~24 call sites, several of them inside catch blocks.
        action.Should().NotThrow();
    }

    [Fact]
    public void Localization_Get_UnknownKeyWithoutArgs_ReturnsKey()
    {
        LocalizationManager.Get("This_Key_Does_Not_Exist_12345")
            .Should().Be("This_Key_Does_Not_Exist_12345");
    }

    // ─── Helpers ───

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException)
        {
            // Windows may hold a handle briefly; test cleanup is best-effort.
        }
    }
}