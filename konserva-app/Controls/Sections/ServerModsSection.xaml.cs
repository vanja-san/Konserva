using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using Konserva.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private CancellationTokenSource? _modUpdateStatusCts;
    private bool _modUpdateStatusBarOpen;
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
        await _viewModel.CheckModUpdatesAsync();
        UpdateModUpdateUI();
    }

    private async void UpdateAllMods_Click(object sender, RoutedEventArgs e)
    {
        UpdateAllModsBtn.Visibility = Visibility.Collapsed;
        UpdateAllModsProgress.Visibility = Visibility.Visible;
        UpdateAllModsProgress.IsIndeterminate = true;

        await _viewModel.ApplyAllModsUpdatesAsync();

        await ReloadModsList();

        UpdateAllModsProgress.IsIndeterminate = false;
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
                await Task.Delay(1200);
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
        if (_viewModel.IsCheckingModUpdates)
        {
            CancelModUpdateStatusAutoClose();
            SetModUpdateStatusOpen(true);
            ModUpdateStatusBar.Title = _viewModel.ModUpdateStatusText;
            ModUpdateStatusBar.Severity = InfoBarSeverity.Informational;
            ModUpdateStatusBar.IsClosable = false;
            CheckModUpdatesBtn.IsEnabled = false;
        }
        else if (_viewModel.IsUpdatingMods)
        {
            CancelModUpdateStatusAutoClose();
            SetModUpdateStatusOpen(true);
            ModUpdateStatusBar.Title = _viewModel.ModUpdateStatusText;
            ModUpdateStatusBar.Severity = InfoBarSeverity.Informational;
            ModUpdateStatusBar.IsClosable = false;
            CheckModUpdatesBtn.IsEnabled = false;
        }
        else if (!string.IsNullOrEmpty(_viewModel.ModUpdateStatusText))
        {
            SetModUpdateStatusOpen(true);
            ModUpdateStatusBar.Title = _viewModel.ModUpdateStatusText;
            ModUpdateStatusBar.Severity = _viewModel.HasModUpdates ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            ModUpdateStatusBar.IsClosable = true;
            CheckModUpdatesBtn.IsEnabled = true;
            AutoCloseModUpdateStatus();
        }
        else
        {
            CancelModUpdateStatusAutoClose();
            SetModUpdateStatusOpen(false);
            CheckModUpdatesBtn.IsEnabled = true;
        }

        UpdateAllModsContainer.Visibility = _viewModel.HasModUpdates ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Плавно открывает/закрывает InfoBar статуса: вертикальное расширение из центра
    /// при появлении и сжатие к центру при скрытии.
    /// </summary>
    private void SetModUpdateStatusOpen(bool open)
    {
        if (open == _modUpdateStatusBarOpen && (open == ModUpdateStatusBar.IsOpen || !open))
            return;

        CancelModUpdateStatusAnimation();

        if (open)
        {
            _modUpdateStatusBarOpen = true;
            ModUpdateStatusBar.IsOpen = true;
            ModUpdateStatusBarScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                CreateStatusBarAnimation(0.0, 1.0, 220, EasingMode.EaseOut));
            ModUpdateStatusBar.BeginAnimation(OpacityProperty,
                CreateStatusBarAnimation(0.0, 1.0, 220, EasingMode.EaseOut));
        }
        else
        {
            _modUpdateStatusBarOpen = false;
            var hide = CreateStatusBarAnimation(1.0, 0.0, 160, EasingMode.EaseIn);
            hide.Completed += (_, _) =>
            {
                CancelModUpdateStatusAnimation();
                ModUpdateStatusBar.IsOpen = false;
            };
            ModUpdateStatusBarScale.BeginAnimation(ScaleTransform.ScaleYProperty, hide);
            ModUpdateStatusBar.BeginAnimation(OpacityProperty,
                CreateStatusBarAnimation(1.0, 0.0, 160, EasingMode.EaseIn));
        }
    }

    private static DoubleAnimation CreateStatusBarAnimation(double from, double to, int milliseconds, EasingMode easingMode) =>
        new(from, to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
        {
            EasingFunction = new CubicEase { EasingMode = easingMode },
        };

    private void CancelModUpdateStatusAnimation()
    {
        ModUpdateStatusBarScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ModUpdateStatusBar.BeginAnimation(OpacityProperty, null);
        ModUpdateStatusBarScale.ScaleY = 1.0;
        ModUpdateStatusBar.Opacity = 1.0;
    }

    /// <summary>
    /// Автоматически скрывает статус обновления модов через 3 секунды.
    /// </summary>
    private void AutoCloseModUpdateStatus()
    {
        _modUpdateStatusCts?.Cancel();
        _modUpdateStatusCts?.Dispose();
        _modUpdateStatusCts = new CancellationTokenSource();
        var token = _modUpdateStatusCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);
                await this.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                        SetModUpdateStatusOpen(false);
                });
            }
            catch (OperationCanceledException)
            {
            }
        }, token).SafeFireAndForget(errorMessage: "Mod update status auto-close failed");
    }

    private void CancelModUpdateStatusAutoClose()
    {
        _modUpdateStatusCts?.Cancel();
        _modUpdateStatusCts?.Dispose();
        _modUpdateStatusCts = null;
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
        CancelModUpdateStatusAutoClose();
        CancelModUpdateStatusAnimation();
    }
}