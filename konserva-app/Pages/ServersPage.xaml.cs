using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;

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

        // Проверяем, является ли ошибка Java-совместимостью
        bool isJavaError = errorMessage.Contains("Java", StringComparison.OrdinalIgnoreCase) ||
                          errorMessage.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                          errorMessage.Contains("Требуется Java", StringComparison.OrdinalIgnoreCase);

        Logger.Info($"[ServersPage.OnServerStartError] isJavaError={isJavaError}", "ServersPage");

        if (isJavaError)
        {
            // Парсим информацию о версии Java из сообщения об ошибке
            var requiredVersion = JavaVersionParser.ParseRequiredJavaVersion(errorMessage);
            var foundVersion = JavaVersionParser.ParseFoundJavaVersion(errorMessage);

            // Получаем все установленные Java
            var allJava = App.ConfigService?.GetConfig().JavaInstallations.Where(j => j.Exists).ToList();

            Logger.Info($"[ServersPage.OnServerStartError] Calling ShowJavaErrorSnackbar: required={requiredVersion}, found={foundVersion}", "ServersPage");

            // Вызываем на UI потоке MainWindow
            MainWindow.Instance?.Dispatcher.Invoke(() =>
            {
                MainWindow.Instance?.ShowJavaErrorSnackbar(server, errorMessage, requiredVersion, foundVersion, allJava);
            });
        }
        else
        {
            MainWindow.Instance?.Dispatcher.InvokeAsync(async () => await UiHelper.ShowError(errorMessage));
        }
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
        if (FilterType.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _filterType = tag;
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
                await UiHelper.ShowError(LocalizationManager.Get("ServersPage_Error_AppNotInitialized"));
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
        if (sender is not WpfButton btn || btn.Tag is not Server server)
            return;

        if (_busyServers.Contains(server.Id))
            return;

        _busyServers.Add(server.Id);
        try
        {
            if (server.IsRunning)
            {
                MainWindow.ServerManager.StopServer(server.Id);
            }
            else
            {
                Logger.Info($"Starting server: {server.Name}", "ServersPage");
                server.ErrorDialogShown = false;
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
        if (e.OriginalSource is WpfButton)
            return;

        if (sender is CardAction { Tag: Server server } cardAction)
        {
            MainWindow.Instance?.NavigateToServer(server.Id);
        }
    }

    private void ServerCard_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space)
        {
            e.Handled = true;
            ServerCard_Click(sender, new RoutedEventArgs());
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not Server server)
            return;

        UiHelper.OpenFolder(server.Path);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not Server server)
            return;

        var result = await UiHelper.ShowDeleteServerConfirm(server.Name);

        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            await MainWindow.ServerManager.DeleteServerAsync(server.Id);
            RefreshList();
        }
        catch (Exception ex)
        {
            Logger.Error($"Delete server error: {ex.Message}", ex, "ServersPage");
            await UiHelper.ShowError($"Ошибка при удалении сервера: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        MainWindow.ServerManager.OnServersChanged -= OnServersChanged;
        MainWindow.ServerManager.OnServerStartError -= OnServerStartError;
        _disposed = true;
    }
}
