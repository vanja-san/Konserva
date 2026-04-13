using System.Reflection;
using System.Windows.Markup;

namespace Konserva.Utilities;

/// <summary>
/// Markup extension для получения версии приложения из AssemblyInformationalVersion.
/// Использование: {utils:AppVersion} или {utils:AppVersion Prefix="v"}
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class AppVersionExtension : MarkupExtension
{
    /// <summary>
    /// Префикс перед версией (например "v")
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "0.0.0";

        // Убираем суффикс сборки если есть (например "1.5.3+build.123" → "1.5.3")
        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
            version = version[..plusIndex];

        return Prefix + version;
    }
}
