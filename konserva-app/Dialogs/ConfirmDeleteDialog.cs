using Konserva.Localization;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace Konserva.Dialogs;

/// <summary>
/// Универсальный диалог подтверждения удаления
/// </summary>
public static class ConfirmDeleteDialog
{
    /// <summary>
    /// Показывает диалог подтверждения удаления
    /// </summary>
    /// <param name="itemName">Имя или описание удаляемого объекта</param>
    /// <param name="title">Заголовок диалога (по умолчанию — "MsgDelete_Title")</param>
    /// <param name="messageFormat">Формат сообщения подтверждения (по умолчанию — "MsgDelete_Confirm")</param>
    /// <param name="showIrreversibleWarning">Показывать предупреждение о необратимости</param>
    /// <returns>ContentDialogResult.Primary если подтверждено, иначе None</returns>
    public static async Task<ContentDialogResult> ShowAsync(
        string itemName,
        string? title = null,
        string? messageFormat = null,
        bool showIrreversibleWarning = true)
    {
        var service = GetDialogService();
        if (service == null) return ContentDialogResult.None;

        var dialogContent = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = string.Format(
                        messageFormat ?? LocalizationManager.Get("MsgDelete_Confirm") ?? "Are you sure you want to delete \"{0}\"?",
                        itemName),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12)
                }
            }
        };

        if (showIrreversibleWarning)
        {
            dialogContent.Children.Add(new StackPanel
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
            });
        }

        var dialog = new ContentDialog
        {
            Title = title ?? LocalizationManager.Get("MsgDelete_Title") ?? "Confirm Deletion",
            Content = dialogContent,
            PrimaryButtonText = LocalizationManager.Get("MsgBtn_Delete") ?? "Delete",
            PrimaryButtonAppearance = ControlAppearance.Danger,
            CloseButtonText = LocalizationManager.Get("MsgBtn_Cancel") ?? "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await service.ShowAsync(dialog, CancellationToken.None);
    }

    /// <summary>
    /// Получает ContentDialogService из MainWindow
    /// </summary>
    private static IContentDialogService? GetDialogService()
    {
        return CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<MainWindow>()?.ContentDialogService;
    }
}
