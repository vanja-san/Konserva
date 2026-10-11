using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace Konserva.Utilities;

/// <summary>
/// Utility for showing ContentDialogs and performing UI operations
/// </summary>
public static class UiHelper
{
    /// <summary>
    /// Open a folder in Explorer
    /// </summary>
    public static void OpenFolder(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to open folder: {ex.Message}", "UiHelper");
            _ = ShowWarning(LocalizationManager.Get("UiHelper_OpenFolderError") ?? "Не удалось открыть папку");
        }
    }

    /// <summary>
    /// Gets the ContentDialogService from MainWindow
    /// </summary>
    private static IContentDialogService? GetDialogService()
    {
        return Ioc.Default.GetService<MainWindow>()?.ContentDialogService;
    }

    /// <summary>
    /// Shows an informational message
    /// </summary>
    public static async Task<ContentDialogResult> ShowInfo(string message, string title = "")
    {
        var service = GetDialogService();
        if (service == null) return ContentDialogResult.None;

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(title) ? LocalizationManager.Get("MsgTitle_Info") : title,
            Content = message,
            CloseButtonText = LocalizationManager.Get("MsgBtn_OK") ?? "OK",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.Info24),
            DefaultButton = ContentDialogButton.Close
        };

        return await service.ShowAsync(dialog, CancellationToken.None);
    }

    /// <summary>
    /// Shows a warning
    /// </summary>
    public static async Task<ContentDialogResult> ShowWarning(string message, string title = "")
    {
        var service = GetDialogService();
        if (service == null) return ContentDialogResult.None;

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(title) ? LocalizationManager.Get("MsgTitle_Warning") : title,
            Content = message,
            CloseButtonText = LocalizationManager.Get("MsgBtn_OK") ?? "OK",
            DefaultButton = ContentDialogButton.Close
        };

        return await service.ShowAsync(dialog, CancellationToken.None);
    }

    /// <summary>
    /// Shows an error
    /// </summary>
    public static async Task<ContentDialogResult> ShowError(string message, string title = "")
    {
        var service = GetDialogService();
        if (service == null) return ContentDialogResult.None;

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(title) ? LocalizationManager.Get("MsgTitle_Error") : title,
            Content = message,
            CloseButtonText = LocalizationManager.Get("MsgBtn_OK") ?? "OK",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.DismissCircle24),
            DefaultButton = ContentDialogButton.Close
        };

        return await service.ShowAsync(dialog, CancellationToken.None);
    }

    /// <summary>
    /// Shows a confirmation prompt
    /// </summary>
    public static async Task<ContentDialogResult> ShowConfirm(string message, string title = "")
    {
        var service = GetDialogService();
        if (service == null) return ContentDialogResult.None;

        var dialog = new ContentDialog
        {
            Title = string.IsNullOrEmpty(title) ? LocalizationManager.Get("MsgTitle_Confirm") : title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(0),
                FontSize = 14
            },
            PrimaryButtonText = LocalizationManager.Get("MsgBtn_Yes") ?? "Yes",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Checkmark24),
            CloseButtonText = LocalizationManager.Get("MsgBtn_No") ?? "No",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.Dismiss24),
            DefaultButton = ContentDialogButton.Primary
        };

        return await service.ShowAsync(dialog, CancellationToken.None);
    }

}
