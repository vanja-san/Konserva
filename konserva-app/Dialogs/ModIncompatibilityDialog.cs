using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Localization;
using Konserva.Utilities;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Konserva.Dialogs;

/// <summary>
/// Показывает окно «Кажется, чего-то не хватает…» —
/// структурированный список недостающих зависимостей и подробности
/// проблем несовместимости модов.
/// Содержимое задано в XAML (<see cref="ModIncompatibilityView"/>).
/// </summary>
public static class ModIncompatibilityDialog
{
    /// <summary>
    /// Показывает диалог о несовместимости модов с локализованным содержимым
    /// </summary>
    public static async Task<ContentDialogResult> ShowAsync(ModIncompatibilityInfo info)
    {
        var service = GetDialogService();
        if (service == null) return ContentDialogResult.None;

        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("ModsIncompat_Title"),
            Content = new ModIncompatibilityView
            {
                DataContext = ModIncompatibilityViewBuilder.From(info)
            },
            CloseButtonText = LocalizationManager.Get("MsgBtn_OK") ?? "OK",
            DefaultButton = ContentDialogButton.Close
        };

        return await service.ShowAsync(dialog, CancellationToken.None);
    }

    /// <summary>
    /// Получает ContentDialogService из MainWindow
    /// </summary>
    private static IContentDialogService? GetDialogService()
    {
        return Ioc.Default.GetService<MainWindow>()?.ContentDialogService;
    }
}