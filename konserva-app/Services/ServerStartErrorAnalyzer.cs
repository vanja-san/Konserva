using Konserva.Models;
using Konserva.Utilities;

namespace Konserva.Services;

/// <summary>
/// Kind of user-facing feedback a failed server start requires.
/// </summary>
public enum ServerStartErrorKind
{
    /// <summary>
    /// Nothing to show: the failure was already reported to the user.
    /// </summary>
    None,

    /// <summary>
    /// Mod dependency problem: show the localized mod incompatibility dialog.
    /// </summary>
    ModIncompatibility,

    /// <summary>
    /// Java compatibility problem: show a snackbar listing the installed runtimes.
    /// </summary>
    JavaIncompatibility,

    /// <summary>
    /// Anything else: show the raw error text.
    /// </summary>
    Generic
}

/// <summary>
/// UI-independent description of a failed server start.
/// Produced by <see cref="ServerStartErrorAnalyzer"/>, consumed by the UI layer.
/// </summary>
public sealed record ServerStartErrorAnalysis
{
    public ServerStartErrorKind Kind { get; init; }

    public string ErrorMessage { get; init; } = string.Empty;

    public ModIncompatibilityInfo? ModInfo { get; init; }

    public int RequiredJavaVersion { get; init; }

    public int FoundJavaVersion { get; init; }

    public IReadOnlyList<JavaInstallation> InstalledJava { get; init; } = Array.Empty<JavaInstallation>();
}

/// <summary>
/// Classifies a server start error. Pure logic: no window, no dispatcher and no
/// service locator, so the classification rules are unit-testable on their own.
/// </summary>
public static class ServerStartErrorAnalyzer
{
    /// <summary>
    /// Determines how a failed server start should be reported to the user.
    /// </summary>
    /// <param name="server">The server that failed to start.</param>
    /// <param name="errorMessage">Raw error text captured from the server process.</param>
    /// <param name="configService">Used to list installed Java runtimes; optional.</param>
    public static ServerStartErrorAnalysis Analyze(
        Server server,
        string errorMessage,
        IConfigService? configService = null)
    {
        // Mod incompatibility (Fabric "Incompatible mods found!" and similar) —
        // show a clear localized dialog instead of the raw console lines.
        var modInfo = ModIncompatibilityParser.TryParse(errorMessage);
        if (modInfo != null)
        {
            return new ServerStartErrorAnalysis
            {
                Kind = ServerStartErrorKind.ModIncompatibility,
                ErrorMessage = errorMessage,
                ModInfo = modInfo
            };
        }

        var requiredVersion = JavaVersionParser.ParseRequiredJavaVersion(errorMessage);
        var foundVersion = JavaVersionParser.ParseFoundJavaVersion(errorMessage);

        // Treat it as a Java compatibility error if the required version could be extracted,
        // the message contains an explicit incompatibility pattern,
        // or it is a Java crash without an explicit version mention (e.g. InvocationTargetException)
        bool isJavaVersionError = requiredVersion > 0 ||
                                  errorMessage.Contains("Требуется Java", StringComparison.OrdinalIgnoreCase) ||
                                  errorMessage.Contains("class file version", StringComparison.OrdinalIgnoreCase) ||
                                  errorMessage.Contains("Unsupported class file major version", StringComparison.OrdinalIgnoreCase) ||
                                  IsJavaCrashError(errorMessage);

        if (!isJavaVersionError)
        {
            return new ServerStartErrorAnalysis
            {
                Kind = ServerStartErrorKind.Generic,
                ErrorMessage = errorMessage
            };
        }

        // If the version could not be extracted from the message — derive the required one from the Minecraft version
        if (requiredVersion <= 0)
        {
            requiredVersion = JavaVersionParser.GetRequiredJavaVersion(
                server.McVersion,
                GetLaunchType(server.ModLoader.Type));
        }

        LogForgeManifestEntryHint(errorMessage);

        // Broken Java 8 (>= 8u400): the snackbar was already shown by GetJavaPathForServer,
        // so a second generic error would only be noise.
        if (errorMessage.Contains("8u400", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info("[Analyze] Broken Java 8 (>= 8u400) snackbar already shown, skipping generic error.", "ServerStartErrorAnalyzer");
            return new ServerStartErrorAnalysis
            {
                Kind = ServerStartErrorKind.None,
                ErrorMessage = errorMessage,
                RequiredJavaVersion = requiredVersion,
                FoundJavaVersion = foundVersion
            };
        }

        var installedJava = configService?.GetConfig().JavaInstallations
                                .Where(j => j.Exists)
                                .ToList()
                            ?? new List<JavaInstallation>();

        return new ServerStartErrorAnalysis
        {
            Kind = ServerStartErrorKind.JavaIncompatibility,
            ErrorMessage = errorMessage,
            RequiredJavaVersion = requiredVersion,
            FoundJavaVersion = foundVersion,
            InstalledJava = installedJava
        };
    }

    /// <summary>
    /// Determines whether the error message is a Java crash, even if it has no
    /// explicit mention of the required version (e.g. <see cref="System.TypeInitializationException"/>,
    /// <see cref="System.Reflection.TargetInvocationException"/>).
    /// Used only as a fallback for <see cref="Analyze"/> — if the
    /// version could not be extracted from the text but it is clearly a Java crash,
    /// we treat it as a Java compatibility error so the user gets an informative
    /// snackbar with the list of Java installations.
    /// </summary>
    private static bool IsJavaCrashError(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        // Patterns that definitely indicate an internal Java/JVM error,
        // rather than problems with the classpath, jar files or arguments.
        // "Could not find or load main class", "Unable to access jarfile" and similar
        // are NOT included — they can be caused by many things (not only by the Java version),
        // and for those the raw error text must be shown instead of the incompatibility snackbar.
        var javaCrashPatterns = new[]
        {
            "InvocationTargetException",
            "Exception in thread",
            "at java.base/",
            "java.lang.reflect",
            "java.lang.UnsupportedClassVersionError",
        };

        // Forge modlauncher: NoSuchMethodError on ManifestEntryVerifier —
        // recent Java 8 builds (≥ 8u400) removed the constructor, Zulu 8u302b08 or older is needed
        if (message.Contains("NoSuchMethodError", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("ManifestEntryVerifier", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return javaCrashPatterns.Any(p => message.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Converts ModLoaderType into ServerLaunchType to compute the required Java version.
    /// </summary>
    private static ServerLaunchType GetLaunchType(ModLoaderType modLoaderType) => modLoaderType switch
    {
        ModLoaderType.Fabric => ServerLaunchType.Fabric,
        ModLoaderType.Quilt => ServerLaunchType.Quilt,
        ModLoaderType.Forge => ServerLaunchType.Forge,
        ModLoaderType.NeoForge => ServerLaunchType.NeoForge,
        _ => ServerLaunchType.Standard, // Vanilla, Paper
    };

    /// <summary>
    /// Logs the Forge-specific hint about the ManifestEntryVerifier constructor,
    /// which recent Java 8 builds removed.
    /// </summary>
    private static void LogForgeManifestEntryHint(string errorMessage)
    {
        // Forge modloader: NoSuchMethodError on ManifestEntryVerifier —
        // recent Java 8 builds (≥ 8u400) removed the ManifestEntryVerifier(Manifest) constructor.
        // An older build is needed: Zulu JRE 8u302b08 or similar.
        if (errorMessage.Contains("NoSuchMethodError", StringComparison.OrdinalIgnoreCase) &&
            errorMessage.Contains("ManifestEntryVerifier", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning("═══════════════════════════════════════════════════════════════", "ServerStartErrorAnalyzer");
            Logger.Warning("Forge modloader requires ManifestEntryVerifier(Manifest) constructor", "ServerStartErrorAnalyzer");
            Logger.Warning("which was removed in Java 8 builds >= 8u400 (Temurin 8u492, Zulu 8u502, etc.)", "ServerStartErrorAnalyzer");
            Logger.Warning("Install Zulu JRE 8u302b08 or older Java 8 build.", "ServerStartErrorAnalyzer");
            Logger.Warning("═══════════════════════════════════════════════════════════════", "ServerStartErrorAnalyzer");
        }
    }
}