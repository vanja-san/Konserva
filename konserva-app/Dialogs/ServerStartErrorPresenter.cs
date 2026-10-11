using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;

namespace Konserva.Dialogs;

/// <summary>
/// UI-layer counterpart of <see cref="ServerStartErrorAnalyzer"/>: turns the
/// classification of a failed server start into the matching snackbar or dialog.
/// Keeping it here keeps <see cref="ServerStartErrorAnalyzer"/> free of any UI
/// dependency, so the classification rules stay unit-testable.
/// </summary>
public static class ServerStartErrorPresenter
{
    /// <summary>
    /// Classifies <paramref name="errorMessage"/> and shows the matching UI feedback.
    /// </summary>
    public static void Show(Server server, string errorMessage)
    {
        var analysis = ServerStartErrorAnalyzer.Analyze(server, errorMessage, GetConfigService());

        switch (analysis.Kind)
        {
            case ServerStartErrorKind.ModIncompatibility when analysis.ModInfo is not null:
                _ = ModIncompatibilityDialog.ShowAsync(analysis.ModInfo);
                break;

            case ServerStartErrorKind.JavaIncompatibility:
                ShowJavaErrorSnackbar(server, analysis);
                break;

            case ServerStartErrorKind.Generic:
                // Not a Java error (or an unknown format) — show the raw text.
                _ = Utilities.UiHelper.ShowError(analysis.ErrorMessage);
                break;

            case ServerStartErrorKind.None:
            default:
                // Nothing to show: the failure was already reported to the user.
                break;
        }
    }

    private static void ShowJavaErrorSnackbar(Server server, ServerStartErrorAnalysis analysis)
    {
        var mainWindow = Ioc.Default.GetService<MainWindow>();
        mainWindow?.Dispatcher.Invoke(() =>
        {
            mainWindow?.ShowJavaErrorSnackbar(
                server,
                analysis.ErrorMessage,
                analysis.RequiredJavaVersion,
                analysis.FoundJavaVersion,
                analysis.InstalledJava?.ToList());
        });
    }

    private static IConfigService? GetConfigService()
    {
        try
        {
            return Ioc.Default.GetService<IConfigService>();
        }
        catch (Exception ex)
        {
            Logger.Warning($"[ServerStartErrorPresenter] IConfigService unavailable: {ex.Message}", "ServerStartErrorPresenter");
            return null;
        }
    }
}