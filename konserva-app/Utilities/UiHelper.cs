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
/// Утилита для отображения ContentDialog и UI-операций
/// </summary>
public static class UiHelper
{
    /// <summary>
    /// Открыть папку в проводнике
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
    /// Получает ContentDialogService из MainWindow
    /// </summary>
    private static IContentDialogService? GetDialogService()
    {
        return Ioc.Default.GetService<MainWindow>()?.ContentDialogService;
    }

    /// <summary>
    /// Показ информационного сообщения
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
    /// Показ предупреждения
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
    /// Показ ошибки
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
    /// Показ подтверждения
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

    /// <summary>
    /// Показ подтверждения удаления сервера
    /// </summary>
    public static async Task<ContentDialogResult> ShowDeleteServerConfirm(string serverName)
    {
        var service = GetDialogService();
        if (service == null) return ContentDialogResult.None;

        var tcs = new TaskCompletionSource<ContentDialogResult>();

        var deleteButton = new Button
        {
            Content = LocalizationManager.Get("MsgBtn_Delete") ?? "Delete",
            Icon = new SymbolIcon(SymbolRegular.Delete24),
            Appearance = ControlAppearance.Danger,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var cancelButton = new Button
        {
            Content = LocalizationManager.Get("MsgBtn_Cancel") ?? "Cancel",
            MinWidth = 100,
            Appearance = ControlAppearance.Secondary
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("MsgDel_Title") ?? "Delete Server",
            Content = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = string.Format(
                            LocalizationManager.Get("MsgDel_Confirm") ?? "Are you sure you want to delete server \"{0}\"?",
                            serverName),
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new SymbolIcon(SymbolRegular.Warning24)
                            {
                                FontSize = 20,
                                Margin = new Thickness(0, 0, 8, 0)
                            },
                            new TextBlock
                            {
                                Text = LocalizationManager.Get("MsgDel_Irreversible") ?? "This action is irreversible!",
                                FontWeight = FontWeights.Bold
                            }
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children =
                        {
                            deleteButton,
                            cancelButton
                        }
                    }
                }
            },
            // Не используем встроенные кнопки ContentDialog — они создают стандартные Button,
            // чьи стили WPF-UI пытаются привязать PressedForeground и выдают binding errors.
            // Вместо этого используем ui:Button напрямую в Content.
            PrimaryButtonText = "",
            CloseButtonText = ""
        };

        deleteButton.Click += (_, _) =>
        {
            tcs.TrySetResult(ContentDialogResult.Primary);
            dialog.Hide();
        };

        cancelButton.Click += (_, _) =>
        {
            tcs.TrySetResult(ContentDialogResult.None);
            dialog.Hide();
        };

        // Показываем диалог (без кнопок, они внутри Content)
        _ = service.ShowAsync(dialog, CancellationToken.None);

        return await tcs.Task;
    }
}
