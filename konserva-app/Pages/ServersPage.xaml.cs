using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Windows.Media.Animation;
using Konserva.ViewModels;
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
    private readonly ServersViewModel _viewModel;
    private bool _disposed;

    public ServersPage()
    {
        InitializeComponent();

        _viewModel = new ServersViewModel(App.ServerManager);

        // Подписываемся на события ViewModel для UI-действий
        _viewModel.NavigateToServerRequested += OnNavigateToServer;
        _viewModel.OpenFolderRequested += OnOpenFolder;
        _viewModel.ShowErrorRequested += OnShowError;

        DataContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.ServerManager.OnServerStartError += OnServerStartError;
        _viewModel.RefreshList();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ServerManager.OnServerStartError -= OnServerStartError;
    }

    private void OnServerStartError(Server server, string errorMessage)
    {
        Logger.Info($"[ServersPage.OnServerStartError] Error for server {server.Name}: {errorMessage}", "ServersPage");
        JavaManagementService.HandleServerStartError(server, errorMessage);
    }

    private void OnNavigateToServer(Server server)
    {
        App.MainWindow?.NavigateToServer(server.Id);
    }

    private void OnOpenFolder(Server server)
    {
        UiHelper.OpenFolder(server.Path);
    }

    private async void OnShowError(Server server, string errorMessage)
    {
        await UiHelper.ShowError(errorMessage);
    }

    // ========== Обработчики UI (поиск, фильтры, навигация) ==========

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.SearchText = SearchBox.Text;
        SearchPlaceholder.Visibility = (string.IsNullOrEmpty(SearchBox.Text) && !SearchBox.IsFocused)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e) =>
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e) =>
        SearchPlaceholder.Visibility = Visibility.Collapsed;

    private void FilterType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterType.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _viewModel.FilterType = tag;
        }
    }

    private void FilterStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterStatus.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _viewModel.FilterStatus = tag;
        }
    }

    private void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Navigating to CreateServerPage", "ServersPage");
        App.MainWindow?.NavigateToCreateServer();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: Server server })
        {
            if (_viewModel.PlayCommand.CanExecute(server))
                _viewModel.PlayCommand.Execute(server);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: Server server })
            _viewModel.OpenFolderCommand.Execute(server);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: Server server })
        {
            var result = await UiHelper.ShowDeleteServerConfirm(server.Name);
            if (result == ContentDialogResult.Primary)
            {
                if (_viewModel.DeleteCommand.CanExecute(server))
                    await ((AsyncRelayCommand<Server>)_viewModel.DeleteCommand).ExecuteAsync(server);
            }
        }
    }

    private void ServerCard_Click(object sender, RoutedEventArgs e)
    {
        // Игнорируем клик, если нажата кнопка (чтобы не было двойного срабатывания)
        if (e.OriginalSource is WpfButton)
            return;

        if (sender is CardAction { Tag: Server server } cardAction)
        {
            _viewModel.NavigateToServerCommand.Execute(server);
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

    // ===== Drag & Drop =====

    private Server? _dragSourceServer;
    private Point _dragStartPoint;

    private void ServerCard_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragSourceServer = GetServerFromElement(sender);
    }

    private void ServerCard_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragSourceServer == null)
            return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStartPoint.X) <= SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) <= SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(
            (DependencyObject)sender,
            new DataObject(DataFormats.Serializable, _dragSourceServer.Id),
            DragDropEffects.Move);

        _dragSourceServer = null;
    }

    private void ServersList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.Serializable))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void ServersList_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.Serializable))
            return;

        var serverId = e.Data.GetData(DataFormats.Serializable) as string;
        if (string.IsNullOrEmpty(serverId))
            return;

        // Определяем индекс, куда бросили
        var dropIndex = GetDropIndex(e.GetPosition(ServersList));
        if (dropIndex < 0)
            return;

        App.ServerManager.MoveServer(serverId, dropIndex);
        _dragSourceServer = null;
        e.Handled = true;
    }

    private Server? GetServerFromElement(object sender)
    {
        if (sender is ContentPresenter presenter)
            return presenter.Content as Server;
        return null;
    }

    private int GetDropIndex(Point dropPosition)
    {
        double accumulatedHeight = 0;
        for (int i = 0; i < ServersList.Items.Count; i++)
        {
            var container = ServersList.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
            if (container == null)
                continue;

            var itemHeight = container.ActualHeight;
            var itemCenter = accumulatedHeight + itemHeight / 2;

            if (dropPosition.Y < itemCenter)
                return i;

            accumulatedHeight += itemHeight;
        }

        return ServersList.Items.Count; // В конец списка
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        App.ServerManager.OnServerStartError -= OnServerStartError;
    }
}
