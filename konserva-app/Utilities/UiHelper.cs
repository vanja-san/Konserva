using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            PrimaryButtonText = "OK",
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
            PrimaryButtonText = "OK",
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
            PrimaryButtonText = "OK",
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
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 14
            },
            PrimaryButtonText = "Да",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Checkmark24),
            CloseButtonText = "Нет",
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
            Title = "Удаление сервера",
            Content = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 12),
                        Children =
                        {
                            new SymbolIcon(SymbolRegular.Delete24)
                            {
                                FontSize = 20,
                                Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
                            },
                            new TextBlock
                            {
                                Text = $"Вы уверены, что хотите удалить сервер \"{serverName}\"?",
                                FontSize = 14,
                                FontWeight = FontWeights.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(8, 0, 0, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    },
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(255, 243, 205)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(12),
                        Child = new StackPanel
                        {
                            Children =
                            {
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    Margin = new Thickness(0, 0, 0, 8),
                                    Children =
                                    {
                                        new TextBlock
                                        {
                                            Text = "⚠️",
                                            FontSize = 16,
                                            Margin = new Thickness(0, 0, 4, 0)
                                        },
                                        new TextBlock
                                        {
                                            Text = "Это действие необратимо!",
                                            FontWeight = FontWeights.Bold,
                                            Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5))
                                        }
                                    }
                                },
                                new TextBlock
                                {
                                    Text = "Будут удалены:",
                                    FontWeight = FontWeights.SemiBold,
                                    Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5)),
                                    Margin = new Thickness(0, 0, 0, 4)
                                },
                                new TextBlock
                                {
                                    Text = "• Все файлы сервера",
                                    Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5)),
                                    Margin = new Thickness(0, 0, 0, 2)
                                },
                                new TextBlock
                                {
                                    Text = $"  {serverPath}",
                                    FontStyle = FontStyles.Italic,
                                    Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5)),
                                    Margin = new Thickness(8, 0, 0, 4)
                                },
                                new TextBlock
                                {
                                    Text = "• Конфигурационные файлы",
                                    Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5)),
                                    Margin = new Thickness(0, 0, 0, 2)
                                },
                                new TextBlock
                                {
                                    Text = "• Мир и сохранения",
                                    Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5)),
                                    Margin = new Thickness(0, 0, 0, 2)
                                },
                                new TextBlock
                                {
                                    Text = "• Логи и бэкапы",
                                    Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5)),
                                    Margin = new Thickness(0, 0, 0, 8)
                                },
                                new TextBlock
                                {
                                    Text = "Продолжить удаление невозможно!",
                                    FontWeight = FontWeights.Bold,
                                    Foreground = new SolidColorBrush(Color.FromRgb(102, 77, 5))
                                }
                            }
                        }
                    }
                }
            },
            PrimaryButtonText = "Удалить",
            PrimaryButtonIcon = new SymbolIcon(SymbolRegular.Delete24),
            CloseButtonText = "Отмена",
            CloseButtonIcon = new SymbolIcon(SymbolRegular.Dismiss24),
            ShowTitle = true,
            Padding = new Thickness(16)
        };
        return await msg.ShowDialogAsync();
    }
}