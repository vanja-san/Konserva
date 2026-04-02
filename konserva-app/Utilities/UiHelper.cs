using Konserva.Localization;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace Konserva.Utilities;

/// <summary>
/// Утилита для отображения MessageBox
/// </summary>
public static class UiHelper
{
    /// <summary>
    /// Показ информационного сообщения
    /// </summary>
    public static async Task<Wpf.Ui.Controls.MessageBoxResult> ShowInfo(string message, string title = "Информация")
    {
        var msg = new MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = LocalizationManager.Get("MsgBtn_OK") ?? "OK",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Info24),
            ShowTitle = true
        };
        return await msg.ShowDialogAsync();
    }

    /// <summary>
    /// Показ предупреждения
    /// </summary>
    public static async Task<Wpf.Ui.Controls.MessageBoxResult> ShowWarning(string message, string title = "Предупреждение")
    {
        var msg = new MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = LocalizationManager.Get("MsgBtn_OK") ?? "OK",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Warning24),
            ShowTitle = true
        };
        return await msg.ShowDialogAsync();
    }

    /// <summary>
    /// Показ ошибки
    /// </summary>
    public static async Task<Wpf.Ui.Controls.MessageBoxResult> ShowError(string message, string title = "Ошибка")
    {
        var msg = new MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = LocalizationManager.Get("MsgBtn_OK") ?? "OK",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.DismissCircle24),
            ShowTitle = true
        };
        return await msg.ShowDialogAsync();
    }

    /// <summary>
    /// Показ подтверждения
    /// </summary>
    public static async Task<Wpf.Ui.Controls.MessageBoxResult> ShowConfirm(string message, string title = "Подтверждение")
    {
        var msg = new MessageBox
        {
            Title = title,
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
            ShowTitle = true,
            Padding = new Thickness(16)
        };
        return await msg.ShowDialogAsync();
    }

    /// <summary>
    /// Показ подтверждения удаления сервера
    /// </summary>
    public static async Task<Wpf.Ui.Controls.MessageBoxResult> ShowDeleteServerConfirm(string serverName, string serverPath)
    {
        var msg = new MessageBox
        {
            Title = "🗑️ " + (LocalizationManager.Get("MsgDel_Title") ?? "Delete Server"),
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
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new Border
                    {
                        Padding = new Thickness(16,0,16,0),
                        Child = new StackPanel
                        {
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = LocalizationManager.Get("MsgDel_WillBeDeleted") ?? "The following will be deleted:",
                                    FontWeight = FontWeights.SemiBold,
                                    Opacity = 0.5,
                                    Margin = new Thickness(0, 0, 0, 4)
                                },
                                new TextBlock
                                {
                                    Text = "• " + (LocalizationManager.Get("MsgDel_ServerFiles") ?? "All server files"),
                                    Margin = new Thickness(0, 0, 0, 2)
                                },
                                new TextBlock
                                {
                                    Text = "• " + (LocalizationManager.Get("MsgDel_ConfigFiles") ?? "Configuration files"),
                                    Margin = new Thickness(0, 0, 0, 2)
                                },
                                new TextBlock
                                {
                                    Text = "• " + (LocalizationManager.Get("MsgDel_WorldSaves") ?? "World and saves"),
                                    Margin = new Thickness(0, 0, 0, 2)
                                },
                                new TextBlock
                                {
                                    Text = "• " + (LocalizationManager.Get("MsgDel_LogsBackups") ?? "Logs and backups"),
                                    Margin = new Thickness(0, 0, 0, 8)
                                },
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    Margin = new Thickness(0, 0, 0, 8),
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
                                }
                            }
                        }
                    }
                }
            },
            PrimaryButtonText = LocalizationManager.Get("MsgBtn_Delete") ?? "Delete",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Delete24),
            CloseButtonText = LocalizationManager.Get("MsgBtn_Cancel") ?? "Cancel",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.Dismiss24),
            ShowTitle = true,
            Padding = new Thickness(16)
        };
        return await msg.ShowDialogAsync();
    }
}