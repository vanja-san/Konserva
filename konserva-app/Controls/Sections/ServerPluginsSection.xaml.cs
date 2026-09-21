using Konserva.Models;
using Konserva.Utilities;
using Konserva.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;

namespace Konserva.Controls.Sections;

/// <summary>
/// Секция списка плагинов: включение/отключение, удаление.
/// </summary>
public partial class ServerPluginsSection : System.Windows.Controls.UserControl
{
    private ServerDetailViewModel _viewModel = null!;
    private Server? _server;

    public ServerPluginsSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Инициализация секции: привязка ViewModel и сервера, загрузка списка.
    /// </summary>
    public void Initialize(ServerDetailViewModel viewModel, Server? server)
    {
        _viewModel = viewModel;
        _server = server;
    }

    /// <summary>
    /// Перезагружает список плагинов (вызывается при каждом открытии раздела).
    /// </summary>
    public void Load() => LoadPlugins();

    private void LoadPlugins()
    {
        if (_server == null) return;
        _viewModel.LoadPlugins();
        PluginsList.ItemsSource = _viewModel.Plugins;
        ItemUiHelper.ReloadItemsPanel(PluginsList, PluginsCountBadge, _viewModel.Plugins.Count, () => ItemUiHelper.UpdateToggleBtn(ToggleAllPluginsBtn, _viewModel.CheckAllPluginsDisabled, "ServerDetail_Plugins_ToggleAll_Enable", "ServerDetail_Plugins_ToggleAll_Disable"));
    }

    private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e) =>
        ItemUiHelper.OpenItemFolder(_server!, "plugins", "ServerDetail_PluginsFolderNotFound", "ServerPluginsSection");

    private void PluginMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not PluginItem plugin) return;
        ItemUiHelper.ShowItemMoreMenu(btn, plugin, "ServerDetail_Plugins_Enable", "ServerDetail_Plugins_Disable", "ServerDetail_Plugins_Delete", TogglePlugin_Click, DeletePlugin_Click);
    }

    private void PluginCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not CardControl card || card.DataContext is not PluginItem plugin) return;
        ItemUiHelper.BuildItemContextMenu(card, plugin, "ServerDetail_Plugins_Enable", "ServerDetail_Plugins_Disable", "ServerDetail_Plugins_Delete", TogglePlugin_Click, DeletePlugin_Click);
    }

    private async void TogglePlugin_Click(object sender, RoutedEventArgs e) =>
        await ItemUiHelper.ToggleItemAsync<PluginItem>(sender, _viewModel.TogglePluginAsync, LoadPlugins);

    private async void ToggleAllPlugins_Click(object sender, RoutedEventArgs e) { _viewModel.ToggleAllPlugins(); LoadPlugins(); }

    private async void DeletePlugin_Click(object sender, RoutedEventArgs e) =>
        await ItemUiHelper.DeleteItemAsync<PluginItem>(sender, _viewModel.DeletePluginAsync, LoadPlugins, "ServerDetail_DeletePluginConfirm", "ServerDetail_DeletePluginTitle");
}