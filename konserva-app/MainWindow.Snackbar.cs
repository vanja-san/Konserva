using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.Windows;
using Wpf.Ui.Controls;

namespace Konserva;

public partial class MainWindow
{
    public void ShowJavaErrorSnackbar(Server server, string errorMessage, int requiredVersion, int foundVersion, List<JavaInstallation>? allJava = null)
    {
        var isServersPage = ContentFrame.Content is Pages.ServersPage;
        var isDetailPage = ContentFrame.Content is Pages.ServerDetailPage;

        Logger.Info($"[ShowJavaErrorSnackbar] server={server.Name}, required={requiredVersion}, found={foundVersion}, isServersPage={isServersPage}, isDetailPage={isDetailPage}", "MainWindow");

        // Специфичная ошибка: Java 8 >= 8u400 с Forge — NoSuchMethodError на ManifestEntryVerifier
        if (errorMessage.Contains("NoSuchMethodError", StringComparison.OrdinalIgnoreCase) &&
            errorMessage.Contains("ManifestEntryVerifier", StringComparison.OrdinalIgnoreCase))
        {
            ShowSnackbar(
                LocalizationManager.Get("Snackbar_Java8Broken_Title"),
                string.Format(LocalizationManager.Get("Snackbar_Java8Broken_Message"), server.McVersion),
                ControlAppearance.Danger, 12);
            return;
        }

        string javaVersionsText;
        if (allJava != null && allJava.Count > 0)
        {
            var versions = allJava
                .Where(j => j.Exists)
                .Select(j => j.MajorVersion > 0 ? j.MajorVersion.ToString() : j.Version)
                .Distinct()
                .OrderBy(v => int.TryParse(v, out var n) ? n : 999);
            javaVersionsText = string.Join(", ", versions);
        }
        else
        {
            javaVersionsText = foundVersion > 0 ? foundVersion.ToString() : "\u2014";
        }

        string title, message;
        if (isServersPage)
        {
            title = server.Name;
            message = allJava is { Count: 1 }
                ? LocalizationManager.Get("Snackbar_JavaIncompatible_Message", server.McVersion, requiredVersion, javaVersionsText)
                : LocalizationManager.Get("Snackbar_JavaIncompatible_Message_Plural", server.McVersion, requiredVersion, javaVersionsText);
        }
        else if (isDetailPage)
        {
            title = LocalizationManager.Get("Snackbar_JavaIncompatible_Title");
            message = allJava is { Count: 1 }
                ? LocalizationManager.Get("Snackbar_JavaIncompatible_Message", server.McVersion, requiredVersion, javaVersionsText)
                : LocalizationManager.Get("Snackbar_JavaIncompatible_Message_Plural", server.McVersion, requiredVersion, javaVersionsText);
        }
        else
        {
            title = LocalizationManager.Get("Snackbar_JavaIncompatible_Title");
            message = errorMessage;
        }

        ShowSnackbar(title, message, ControlAppearance.Danger, 10);
    }

    public void HideJavaErrorSnackbar()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (SnackbarPresenter != null)
            {
                _ = SnackbarPresenter.HideCurrent();
            }
        });
    }

    public void ShowSnackbar(string title, string message, ControlAppearance appearance = ControlAppearance.Info, int timeoutSeconds = 3)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (SnackbarPresenter == null)
            {
                Logger.Warning("[ShowSnackbar] SnackbarPresenter is null!", "MainWindow");
                return;
            }

            var symbol = appearance switch
            {
                ControlAppearance.Success => SymbolRegular.CheckmarkCircle20,
                ControlAppearance.Caution => SymbolRegular.Warning20,
                ControlAppearance.Danger => SymbolRegular.ErrorCircle20,
                _ => SymbolRegular.Info20
            };

            var snackbar = new Snackbar(SnackbarPresenter)
            {
                Title = title,
                Content = message,
                Icon = new SymbolIcon(symbol) { FontSize = 20 },
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                Appearance = appearance,
                Padding = new Thickness(12, 10, 12, 10),
                MinHeight = 44,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            SnackbarPresenter.AddToQue(snackbar);
        });
    }
}
