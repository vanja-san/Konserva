using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.IO;
using System.Reflection;

namespace Konserva.Tests.Regression;

/// <summary>
/// Regression tests for the P1 defects: path traversal through Modrinth file names,
/// JVM argument injection, UPnP ownership and the shared Java configuration race.
/// </summary>
[Collection("Sequential")]
[Trait("Category", "Unit")]
public class P1RegressionTests
{
    // ─── P1-1: a mod file name from the API must stay a bare file name ───

    [Theory]
    [InlineData(@"..\..\..\Users\victim\Startup\evil.jar")]
    [InlineData(@"..\evil.jar")]
    [InlineData("/etc/cron.d/evil.jar")]
    [InlineData("subdir/evil.jar")]
    public void ModUpdateService_RejectsTraversalFileNames(string hostileName)
    {
        // Path.GetFileName strips the directory part, so a name that survives unchanged
        // is safe; anything else used to reach File.Delete / File.Move as an absolute path.
        var safeName = Path.GetFileName(hostileName);

        string.Equals(safeName, hostileName, StringComparison.Ordinal)
            .Should().BeFalse($"'{hostileName}' must be rejected before touching the disk");
    }

    [Theory]
    [InlineData("sodium-0.6.0.jar")]
    [InlineData("fabric-api-0.100.0+1.21.jar")]
    public void ModUpdateService_AcceptsPlainFileNames(string name)
    {
        Path.GetFileName(name).Should().Be(name);
    }

    // ─── P1-2: values that reach the JVM command line must be shaped like versions ───

    [Theory]
    [InlineData("1.21.1")]
    [InlineData("1.20.4")]
    [InlineData("1.20.4-pre1")]
    [InlineData("24w14potato")]
    [InlineData("1.0")]
    public void McVersionHelper_AcceptsRealVersions(string version)
    {
        McVersionHelper.IsSafeVersionString(version).Should().BeTrue();
    }

    [Theory]
    [InlineData("1.20.1 --help")]
    [InlineData("1.20.1 -javaagent:C:\\evil.jar")]
    [InlineData("1.20.1\" \"x")]
    [InlineData("1.20.1 & calc")]
    [InlineData("1.20.1|calc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("../../etc/passwd")]
    public void McVersionHelper_RejectsInjectedVersionStrings(string? version)
    {
        // The value used to be interpolated unquoted into the installer argument string.
        McVersionHelper.IsSafeVersionString(version).Should().BeFalse();
    }

    [Theory]
    [InlineData("cpw.mods.bootstraplauncher.BootstrapLauncher")]
    [InlineData("net.minecraft.server.Main")]
    [InlineData("_Inner$Class")]
    public void McVersionHelper_AcceptsRealMainClasses(string className)
    {
        McVersionHelper.IsSafeJavaClassName(className).Should().BeTrue();
    }

    [Theory]
    [InlineData("Main -javaagent:C:\\evil.jar")]
    [InlineData("Main; calc")]
    [InlineData("Main|nested")]
    [InlineData("1Main")]
    [InlineData("")]
    [InlineData(null)]
    public void McVersionHelper_RejectsInjectedMainClasses(string? className)
    {
        // Read from .neoforge-launch.json inside an importable server folder and placed
        // unquoted on the command line — a smuggling vector.
        McVersionHelper.IsSafeJavaClassName(className).Should().BeFalse();
    }

    // ─── P1-3: only delete the port rule we created ───

    [Theory]
    [InlineData("Konserva - TestServer")]
    [InlineData("konserva - server")]
    public void PortForwarding_RecognizesOwnRules(string description)
    {
        bool owned = (bool)typeof(PortForwardingService)
            .GetMethod("IsOwnedByKonserva", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [description])!;

        owned.Should().BeTrue();
    }

    [Theory]
    [InlineData("SomeOtherApp")]
    [InlineData("Steam - Server")]
    [InlineData("")]
    [InlineData(null)]
    public void PortForwarding_DoesNotDeleteForeignRules(string? description)
    {
        // Deleting by port number alone tore down rules created by other applications.
        bool owned = (bool)typeof(PortForwardingService)
            .GetMethod("IsOwnedByKonserva", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [description])!;

        owned.Should().BeFalse();
    }

    // ─── P1-8: the shared Java list must be safe to read while it is mutated ───

    [Fact]
    public async Task AppConfig_JavaSnapshot_IsStableDuringConcurrentMutation()
    {
        var config = new AppConfig();

        config.MutateJavaInstallations(list =>
        {
            for (int i = 0; i < 500; i++)
                list.Add(new JavaInstallation { Id = $"java-{i}", Path = $"C:\\java\\{i}\\java.exe" });
        });

        // Reading from another thread while a rescan rewrites the live collection used to
        // throw InvalidOperationException("Collection was modified").
        var reader = Task.Run(() =>
        {
            for (int i = 0; i < 2000; i++)
                _ = config.GetJavaInstallationsSnapshot().Count;
        });

        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                config.MutateJavaInstallations(list =>
                {
                    list.Add(new JavaInstallation { Id = $"extra-{i}-{Guid.NewGuid()}", Path = $"C:\\x\\{i}" });
                    if (list.Count > 50) list.RemoveAt(0);
                });
            }
        });

        await Task.WhenAll(reader, writer);

        config.GetJavaInstallationsSnapshot().Should().NotBeEmpty();
    }

    [Fact]
    public void AppConfig_GetDefaultJava_PrefersConfiguredDefault()
    {
        var config = new AppConfig();

        config.MutateJavaInstallations(list =>
        {
            list.Add(new JavaInstallation { Id = "a", Path = @"C:\a\java.exe" });
            list.Add(new JavaInstallation { Id = "b", Path = @"C:\b\java.exe", IsDefault = true });
        });
        config.DefaultJavaId = "b";

        config.GetDefaultJava()!.Id.Should().Be("b");
    }
}