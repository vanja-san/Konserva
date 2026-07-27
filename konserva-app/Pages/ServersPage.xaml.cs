using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
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

        _viewModel = Ioc.Default.GetService<ServersViewModel>()
            ?? new ServersViewModel(Ioc.Default.GetService<IServerManager>()!);

        // Подписываемся на события ViewModel для UI-действий
        _viewModel.NavigateToServerRequested += OnNavigateToServer;
        _viewModel.OpenFolderRequested += OnOpenFolder;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetService<IServerManager>()!.OnServerStartError += OnServerStartError;
        _viewModel.IsVisible = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetService<IServerManager>()!.OnServerStartError -= OnServerStartError;
        _viewModel.IsVisible = false;
    }

    private void OnServerStartError(Server server, string errorMessage)
    {
        Logger.Info($"[ServersPage.OnServerStartError] Error for server {server.Name}: {errorMessage}", "ServersPage");
        JavaManagementService.HandleServerStartError(server, errorMessage);
    }

    private void OnNavigateToServer(Server server)
    {
        Ioc.Default.GetService<MainWindow>()?.NavigateToServer(server.Id);
    }

    private void OnOpenFolder(Server server)
    {
        UiHelper.OpenFolder(server.Path);
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

    private void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        Logger.Info("Navigating to CreateServerPage", "ServersPage");
        Ioc.Default.GetService<MainWindow>()?.NavigateToCreateServer();
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
                    _viewModel.DeleteCommand.Execute(server);
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

    // ===== Sort & Filter UI (DropDownButton + ContextMenu) =====

    private void SortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        if (sender is System.Windows.Controls.MenuItem { Tag: string field })
        {
            // Uncheck all sort items, then check the clicked one
            var menu = SortButton.Flyout as System.Windows.Controls.ContextMenu;
            if (menu?.Items is not null)
            {
                foreach (var child in menu.Items.OfType<System.Windows.Controls.MenuItem>())
                    child.IsChecked = child.Tag as string == field;
            }

            _viewModel.SortField = field;

            // Toggle direction on re-click of same field
            if (_lastSortField == _viewModel.SortField)
                _viewModel.SortAscending = !_viewModel.SortAscending;
            _lastSortField = _viewModel.SortField;
        }
    }
    private string? _lastSortField;

    private void TypeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag })
        {
            _viewModel.FilterType = tag;
            // Update checked state
            var menu = FilterButton.Flyout as System.Windows.Controls.ContextMenu;
            if (menu?.Items is not null)
            {
                foreach (var child in menu.Items.OfType<System.Windows.Controls.MenuItem>())
                {
                    if (child.Tag is string childTag &&
                        (childTag == "All" || childTag == "Vanilla" || childTag == "Forge" ||
                         childTag == "NeoForge" || childTag == "Fabric" || childTag == "Paper"))
                    {
                        child.IsChecked = childTag == tag;
                    }
                }
            }
        }
    }

    private void StatusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag })
        {
            _viewModel.FilterStatus = tag;
            // Update checked state
            var menu = FilterButton.Flyout as System.Windows.Controls.ContextMenu;
            if (menu?.Items is not null)
            {
                foreach (var child in menu.Items.OfType<System.Windows.Controls.MenuItem>())
                {
                    if (child.Tag is string childTag &&
                        (childTag == "All" || childTag == "Running" || childTag == "Stopped"))
                    {
                        child.IsChecked = childTag == tag;
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        Ioc.Default.GetService<IServerManager>()!.OnServerStartError -= OnServerStartError;
    }
}
