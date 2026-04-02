using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Konserva.Pages;

/// <summary>
/// Страница списка серверов
/// </summary>
public partial class ServersPage : Page, IDisposable
{
    private string _searchText = string.Empty;
    private string _filterType = "All";
    private string _filterStatus = "All";
    private readonly HashSet<string> _busyServers = [];
    private bool _disposed;

    public ServersPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MainWindow.ServerManager.OnServersChanged += OnServersChanged;
        MainWindow.ServerManager.OnServerStartError += OnServerStartError;
        RefreshList();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MainWindow.ServerManager.OnServersChanged -= OnServersChanged;
        MainWindow.ServerManager.OnServerStartError -= OnServerStartError;
    }

    /// <summary>
    /// Обработчик ошибки запуска сервера
    /// </summary>
    private void OnServerStartError(Server server, string errorMessage)
    {
        Logger.Info($"[ServersPage.OnServerStartError] Error for server {server.Name}: {errorMessage}", "ServersPage");

        // Помечаем, что диалог показан (чтобы не показывать повторно на странице сервера)
        server.ErrorDialogShown = true;

        // Показываем ошибку пользователю в UI потоке
        this.Invoke(async () =>
        {
            bool isJavaError = errorMessage.Contains("Java", StringComparison.OrdinalIgnoreCase) ||
                              errorMessage.Contains("java", StringComparison.OrdinalIgnoreCase);

            if (isJavaError)
            {
                // Создаём кнопку с обработчиком
                var downloadButton = new System.Windows.Controls.Button
                {
                    Content = "📥 Скачать Java (adoptium.net)",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Padding = new Thickness(16, 8, 16, 8),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                downloadButton.Click += (s, e) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://adoptium.net/",
                        UseShellExecute = true
                    });
                };

                // Для Java ошибки показываем более подробное сообщение
                var dialog = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "⚠️ Ошибка Java",
                    Content = new StackPanel
                    {
                        Margin = new Thickness(0, 8, 0, 0),
                        Children =
                        {
                            new System.Windows.Controls.TextBlock
                            {
                                Text = errorMessage,
                                TextWrapping = System.Windows.TextWrapping.Wrap,
                                Margin = new Thickness(0, 0, 0, 16)
                            },
                            new Border
                            {
                                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 243, 205)),
                                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(12),
                                Child = new StackPanel
                                {
                                    Children =
                                    {
                                        new System.Windows.Controls.TextBlock
                                        {
                                            Text = "Требуется установить или обновить Java",
                                            FontWeight = System.Windows.FontWeights.Bold,
                                            Margin = new Thickness(0, 0, 0, 8)
                                        },
                                        new System.Windows.Controls.TextBlock
                                        {
                                            Text = "Скачайте последнюю версию Java с официального сайта:",
                                            Margin = new Thickness(0, 0, 0, 8)
                                        },
                                        downloadButton
                                    }
                                }
                            }
                        }
                    },
                    PrimaryButtonText = "OK",
                    PrimaryButtonIcon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Info24),
                    ShowTitle = true,
                    Padding = new Thickness(16)
                };

                await dialog.ShowDialogAsync();
            }
            else
            {
                await UiHelper.ShowError(errorMessage);
            }
        });
    }

    /// <summary>
    /// Обработка изменения списка серверов
    /// </summary>
    private void OnServersChanged()
    {
        // Снимаем блокировку для серверов, которые не запущены
        var servers = MainWindow.ServerManager.GetServers();
        foreach (var server in servers.Where(s => !s.IsRunning))
        {
            _busyServers.Remove(server.Id);
        }

        RefreshList();
    }

    private void RefreshList() => ApplyFilters();

    private void ApplyFilters()
    {
        if (ServersList == null || NoServersPanel == null)
            return;

        var servers = MainWindow.ServerManager.GetServers();

        var filtered = servers.Where(s =>
        {
            var matchSearch = string.IsNullOrEmpty(_searchText) ||
                              s.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);

            var matchType = _filterType == "All" ||
                            s.ModLoader.Type.ToString().Equals(_filterType, StringComparison.OrdinalIgnoreCase);

            var matchStatus = _filterStatus switch
            {
                "All" => true,
                "Running" => s.Status is ServerStatus.Running or ServerStatus.Starting,
                "Stopped" => s.Status is ServerStatus.Stopped or ServerStatus.Error,
                _ => true
            };

            return matchSearch && matchType && matchStatus;
        }).ToList();

        ServersList.ItemsSource = filtered;
        NoServersPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        SearchPlaceholder.Visibility = (string.IsNullOrEmpty(_searchText) && !SearchBox.IsFocused)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilters();
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e) =>
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e) =>
        SearchPlaceholder.Visibility = Visibility.Collapsed;

    private void FilterType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterType.SelectedItem is ComboBoxItem item && item.Content is string content)
        {
            _filterType = content == "Все типы" ? "All" : content;
            ApplyFilters();
        }
    }

    private void FilterStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterStatus.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _filterStatus = tag;
            ApplyFilters();
        }
    }

    private async void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("CreateServer_Click START", "ServersPage");

        try
        {
            if (MainWindow.Instance == null)
            {
                Logger.Error("MainWindow.Instance is null", null, "ServersPage");
                await UiHelper.ShowError("Ошибка: приложение не инициализировано");
                return;
            }

            Logger.Info("Opening CreateServerDialog", "ServersPage");

            var versionsApi = App.ServiceProvider?.GetService(typeof(IMcVersionsApi)) as IMcVersionsApi
                ?? new McVersionsApi();

            Logger.Info($"versionsApi created: {versionsApi != null}", "ServersPage");

            var dialog = new Dialogs.CreateServerDialog(App.ConfigService, versionsApi);

            Logger.Info("CreateServerDialog created", "ServersPage");

            dialog.Owner = MainWindow.Instance;

            Logger.Info("Dialog.Owner set", "ServersPage");

            if (dialog.ShowDialog() == true)
            {
                Logger.Info("CreateServerDialog completed with OK", "ServersPage");
                RefreshList();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CreateServer_Click error: {ex}", ex, "ServersPage");
            await UiHelper.ShowError($"Ошибка: {ex.Message}");
        }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info($"Play_Click triggered, sender={sender?.GetType().Name}", "ServersPage");

        // Кнопки в DataTemplate используют стандартный WPF Button, не WPF UI
        if (sender is not System.Windows.Controls.Button btn)
        {
            Logger.Error($"Play_Click: sender is not Button, type={sender?.GetType().FullName}", null, "ServersPage");
            return;
        }

        Logger.Info($"Play_Click: btn.Tag={btn.Tag?.GetType().FullName}", "ServersPage");

        if (btn.Tag is not Server server)
        {
            Logger.Error($"Play_Click: Tag is not Server", null, "ServersPage");
            return;
        }

        Logger.Info($"Play_Click: Server={server.Name}, IsRunning={server.IsRunning}", "ServersPage");

        if (_busyServers.Contains(server.Id))
        {
            Logger.Info($"Play_Click: Server {server.Name} is busy", "ServersPage");
            return;
        }

        _busyServers.Add(server.Id);
        try
        {
            if (server.IsRunning)
            {
                Logger.Info($"Play_Click: Stopping server {server.Name}", "ServersPage");
                MainWindow.ServerManager.StopServer(server.Id);
            }
            else
            {
                Logger.Info($"Play_Click: Starting server {server.Name}", "ServersPage");
                MainWindow.ServerManager.StartServer(server.Id);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Play_Click: Error managing server {server.Name}: {ex.Message}", ex, "ServersPage");
            await UiHelper.ShowError($"Не удалось выполнить операцию: {ex.Message}");
        }
        finally
        {
            _busyServers.Remove(server.Id);
        }

        RefreshList();
    }

    private void ServerCard_Click(object sender, RoutedEventArgs e)
    {
        // Игнорируем клик, если нажата кнопка (чтобы не было двойного срабатывания)
        if (e.OriginalSource is System.Windows.Controls.Button)
            return;

        if (sender is CardAction cardAction)
        {
            if (cardAction.Tag is Server server)
            {
                MainWindow.Instance?.NavigateToServer(server.Id);
            }
        }
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        // Кнопки в DataTemplate используют стандартный WPF Button, не WPF UI
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not Server server)
            return;

        try
        {
            if (Directory.Exists(server.Path))
            {
                Process.Start("explorer.exe", server.Path);
            }
        }
        catch
        {
            await UiHelper.ShowWarning("Не удалось открыть папку сервера");
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        // Кнопки в DataTemplate используют стандартный WPF Button, не WPF UI
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not Server server)
            return;

        var result = await UiHelper.ShowDeleteServerConfirm(server.Name, server.Path);

        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        try
        {
            await MainWindow.ServerManager.DeleteServerAsync(server.Id);
            RefreshList();
        }
        catch (Exception ex)
        {
            await UiHelper.ShowError($"Ошибка при удалении сервера: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        MainWindow.ServerManager.OnServersChanged -= OnServersChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
