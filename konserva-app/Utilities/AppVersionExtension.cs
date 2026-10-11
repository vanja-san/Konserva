using System.Reflection;
using System.Windows.Markup;

namespace Konserva.Utilities;

/// <summary>
/// Markup extension for obtaining the application version from AssemblyInformationalVersion.
/// Usage: {utils:AppVersion} or {utils:AppVersion Prefix="v"}
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class AppVersionExtension : MarkupExtension
{
    /// <summary>
    /// Prefix prepended to the version (for example "v")
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "0.0.0";

        // Strip the build metadata suffix if present (for example "1.5.3+build.123" → "1.5.3")
        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
            version = version[..plusIndex];

        return Prefix + version;
    }
}
