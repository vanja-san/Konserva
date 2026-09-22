using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using Konserva.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;

namespace Konserva.Controls.Sections;

/// <summary>
/// Секция списка модов: проверка и применение обновлений, включение/отключение, удаление.
/// </summary>
public partial class ServerModsSection : System.Windows.Controls.UserControl, IDisposable
{
    private ServerDetailViewModel _viewModel = null!;
    private Server? _server;
    private bool _disposed;

    public ServerModsSection()
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
    /// Перезагружает список модов (вызывается при каждом открытии раздела).
    /// </summary>
    public void Load() => LoadMods();

    private async void LoadMods()
    {
        if (_server == null) return;
        _viewModel.LoadMods();
        await _viewModel.ResolveModTitlesAsync();
        ModsList.ItemsSource = _viewModel.Mods;
        ItemUiHelper.ReloadItemsPanel(ModsList, ModsCountBadge, _viewModel.Mods.Count, () => ItemUiHelper.UpdateToggleBtn(ToggleAllModsBtn, _viewModel.CheckAllModsDisabled, "ServerDetail_Mods_ToggleAll_Enable", "ServerDetail_Mods_ToggleAll_Disable"));
        await _viewModel.CheckModUpdatesAsync();
        UpdateModUpdateUI();
    }

    private async void CheckModUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckModUpdatesBtn.Visibility = Visibility.Collapsed;
        CheckModUpdatesProgress.Visibility = Visibility.Visible;

        await _viewModel.CheckModUpdatesAsync();

        CheckModUpdatesProgress.Visibility = Visibility.Collapsed;

        if (!_viewModel.HasModUpdates)
        {
            CheckModUpdatesCheck.Visibility = Visibility.Visible;
            await Task.Delay(1200);
            CheckModUpdatesCheck.Visibility = Visibility.Collapsed;
        }

        CheckModUpdatesBtn.Visibility = Visibility.Visible;
        UpdateModUpdateUI();
    }

    private async void UpdateAllMods_Click(object sender, RoutedEventArgs e)
    {
        UpdateAllModsBtn.Visibility = Visibility.Collapsed;
        UpdateAllModsProgress.Visibility = Visibility.Visible;

        await _viewModel.ApplyAllModsUpdatesAsync();

        await ReloadModsList();

        UpdateAllModsProgress.Visibility = Visibility.Collapsed;
        UpdateAllModsCheck.Visibility = Visibility.Visible;

        await Task.Delay(1200);

        UpdateAllModsCheck.Visibility = Visibility.Collapsed;
        UpdateModUpdateUI();
    }

    private async void UpdateMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is ModItem mod)
        {
            mod.IsUpdating = true;

            var result = await _viewModel.ApplyModUpdateAsync(mod);
            if (result?.Success != true)
            {
                mod.IsUpdating = false;
                UpdateModUpdateUI();
                return;
            }

            await ReloadModsList();

            var newFileName = NormalizeModFileName(result.NewFileName);
            var updatedMod = _viewModel.Mods.FirstOrDefault(m =>
                string.Equals(m.FileName, newFileName, StringComparison.OrdinalIgnoreCase));

            if (updatedMod != null)
            {
                updatedMod.UpdateSucceeded = true;
                updatedMod.UpdateAvailable = false;
                await Task.Delay(3000);
                updatedMod.UpdateSucceeded = false;
            }

            UpdateModUpdateUI();
        }
    }

    private static string NormalizeModFileName(string? fileName)
    {
        if (fileName?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true)
            return fileName[..^".disabled".Length];
        return fileName ?? string.Empty;
    }

    private async Task ReloadModsList()
    {
        if (_server == null) return;
        _viewModel.LoadMods();
        await _viewModel.ResolveModTitlesAsync();
        ModsList.ItemsSource = _viewModel.Mods;
        ItemUiHelper.ReloadItemsPanel(ModsList, ModsCountBadge, _viewModel.Mods.Count,
            () => ItemUiHelper.UpdateToggleBtn(ToggleAllModsBtn, _viewModel.CheckAllModsDisabled,
                "ServerDetail_Mods_ToggleAll_Enable", "ServerDetail_Mods_ToggleAll_Disable"));
    }

    private void UpdateModUpdateUI()
    {
        var hasUpdates = _viewModel.HasModUpdates;
        ModsUpdatesBadge.Value = _viewModel.ModUpdatesCount.ToString();
        ModsUpdatesBadge.Visibility = hasUpdates ? Visibility.Visible : Visibility.Collapsed;
        CheckModUpdatesBtn.IsEnabled = !_viewModel.IsCheckingModUpdates && !_viewModel.IsUpdatingMods;
        UpdateAllModsContainer.Visibility = hasUpdates ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e) =>
        ItemUiHelper.OpenItemFolder(_server!, "mods", "ServerDetail_ModsFolderNotFound", "ServerModsSection");

    private void ModMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton btn || btn.Tag is not ModItem mod) return;
        ItemUiHelper.ShowItemMoreMenu(btn, mod, "ServerDetail_Mods_Enable", "ServerDetail_Mods_Disable", "ServerDetail_Mods_Delete", ToggleMod_Click, DeleteMod_Click);
    }

    private void ModCard_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not CardControl card || card.DataContext is not ModItem mod) return;
        ItemUiHelper.BuildItemContextMenu(card, mod, "ServerDetail_Mods_Enable", "ServerDetail_Mods_Disable", "ServerDetail_Mods_Delete", ToggleMod_Click, DeleteMod_Click);
    }

    private async void ToggleMod_Click(object sender, RoutedEventArgs e) =>
        await ItemUiHelper.ToggleItemAsync<ModItem>(sender, _viewModel.ToggleModAsync, LoadMods);

    private async void ToggleAllMods_Click(object sender, RoutedEventArgs e) { _viewModel.ToggleAllMods(); LoadMods(); }

    private async void DeleteMod_Click(object sender, RoutedEventArgs e) =>
        await ItemUiHelper.DeleteItemAsync<ModItem>(sender, _viewModel.DeleteModAsync, LoadMods, "ServerDetail_DeleteModConfirm", "ServerDetail_DeleteModTitle");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}