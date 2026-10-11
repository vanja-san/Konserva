using Konserva.Localization;
using Konserva.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WpfButton = Wpf.Ui.Controls.Button;

namespace Konserva.Utilities;

/// <summary>
/// Common UI helpers for item lists (mods, plugins):
/// counter, "enable all" button, context menus, toggling and deletion.
/// </summary>
public static class ItemUiHelper
{
    /// <summary>
    /// Updates the counter in the InfoBadge and the visibility/state of the "enable all" button.
    /// </summary>
    public static void ReloadItemsPanel(ItemsControl list, InfoBadge badge, int count, Action updateToggle)
    {
        badge.Value = count > 0 ? count.ToString() : string.Empty;
        badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        updateToggle();
    }

    /// <summary>
    /// Updates the tooltip of the "enable all" button (enable/disable all).
    /// </summary>
    public static void UpdateToggleBtn(WpfButton toggleBtn, Func<bool> checkAllDisabled, string enableKey, string disableKey)
    {
        toggleBtn.Visibility = Visibility.Visible;
        toggleBtn.ToolTip = LocalizationManager.Get(checkAllDisabled() ? enableKey : disableKey);
    }

    /// <summary>
    /// Opens the "…" menu of an item card.
    /// </summary>
    public static void ShowItemMoreMenu(WpfButton btn, IItemEntry item, string enableKey, string disableKey, string deleteKey,
        RoutedEventHandler toggleHandler, RoutedEventHandler deleteHandler)
    {
        var contextMenu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            MinWidth = btn.ActualWidth
        };
        contextMenu.Items.Add(BuildToggleItem(item, enableKey, disableKey, toggleHandler));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(BuildDeleteItem(deleteKey, deleteHandler));
        contextMenu.IsOpen = true;
    }

    /// <summary>
    /// Builds the context menu of a card (enable/disable, delete).
    /// </summary>
    public static void BuildItemContextMenu(CardControl card, IItemEntry item, string enableKey, string disableKey, string deleteKey,
        RoutedEventHandler toggleHandler, RoutedEventHandler deleteHandler)
    {
        card.ContextMenu = new ContextMenu();
        card.ContextMenu.Items.Add(BuildToggleItem(item, enableKey, disableKey, toggleHandler));
        card.ContextMenu.Items.Add(new Separator());
        card.ContextMenu.Items.Add(BuildDeleteItem(deleteKey, deleteHandler));
    }

    public static System.Windows.Controls.MenuItem BuildToggleItem(IItemEntry item, string enableKey, string disableKey, RoutedEventHandler clickHandler)
    {
        var isEnabled = item.Enabled;
        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationManager.Get(isEnabled ? disableKey : enableKey),
            Tag = item
        };
        var toggleIcon = new SymbolIcon
        {
            FontSize = 16,
            Symbol = isEnabled ? SymbolRegular.CheckboxChecked20 : SymbolRegular.CheckboxUnchecked20
        };
        if (isEnabled)
            toggleIcon.Foreground = CriticalBrush;
        toggleItem.Icon = toggleIcon;
        toggleItem.Click += clickHandler;
        return toggleItem;
    }

    public static System.Windows.Controls.MenuItem BuildDeleteItem(string deleteKey, RoutedEventHandler clickHandler)
    {
        var deleteItem = new System.Windows.Controls.MenuItem
        {
            Header = LocalizationManager.Get(deleteKey)
        };
        deleteItem.Icon = new SymbolIcon
        {
            FontSize = 16,
            Symbol = SymbolRegular.Delete20,
            Foreground = CriticalBrush
        };
        deleteItem.Click += clickHandler;
        return deleteItem;
    }

    /// <summary>
    /// The "critical" brush from the theme (red), with a fallback for static contexts.
    /// </summary>
    private static Brush CriticalBrush =>
        Application.Current.TryFindResource("SystemFillColorCriticalBrush") as Brush
        ?? new SolidColorBrush(Color.FromRgb(220, 100, 100));

    /// <summary>
    /// Extracts the event sender item (a button with Tag or a MenuItem with Tag).
    /// </summary>
    public static T? ExtractSender<T>(object sender) where T : class, IItemEntry
    {
        if (sender is WpfButton btn && btn.Tag is T bt) return bt;
        if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is T mt) return mt;
        return null;
    }

    /// <summary>
    /// Toggles the state of an item and reloads the list.
    /// </summary>
    public static async Task ToggleItemAsync<T>(object sender, Func<T, Task> toggleAsync, Action reload) where T : class, IItemEntry
    {
        var item = ExtractSender<T>(sender);
        if (item == null) return;
        await toggleAsync(item);
        reload();
    }

    /// <summary>
    /// Safe call of ShowError from a sync context (fire-and-forget with try/catch).
    /// </summary>
    public static async void ShowErrorSafe(string message, string context)
    {
        try { await UiHelper.ShowError(message); }
        catch (Exception ex) { Logger.Warning($"[ShowErrorSafe] Error: {ex.Message}", context); }
    }

    /// <summary>
    /// Safe call of ShowWarning from a sync context (fire-and-forget with try/catch).
    /// </summary>
    public static async void ShowWarningSafe(string message, string context)
    {
        try { await UiHelper.ShowWarning(message); }
        catch (Exception ex) { Logger.Warning($"[ShowWarningSafe] Error: {ex.Message}", context); }
    }

    /// <summary>
    /// Opens the server subdirectory folder (mods/plugins), otherwise shows a warning.
    /// </summary>
    public static void OpenItemFolder(Server server, string subDir, string notFoundKey, string context)
    {
        if (server == null) return;
        var dir = Path.Combine(server.Path, subDir);
        if (Directory.Exists(dir)) UiHelper.OpenFolder(dir);
        else ShowWarningSafe(LocalizationManager.Get(notFoundKey), context);
    }

    /// <summary>
    /// Confirms and deletes an item, then reloads the list.
    /// </summary>
    public static async Task DeleteItemAsync<T>(object sender, Func<T, Task> deleteAsync, Action reload, string confirmKey, string titleKey)
        where T : class, IItemEntry
    {
        var item = ExtractSender<T>(sender);
        if (item == null) return;
        var result = await UiHelper.ShowConfirm(
            string.Format(LocalizationManager.Get(confirmKey), item.Name, item.FileName),
            LocalizationManager.Get(titleKey));
        if (result != ContentDialogResult.Primary) return;
        await deleteAsync(item);
        reload();
    }
}