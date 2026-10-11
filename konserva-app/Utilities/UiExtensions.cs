using Konserva.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Konserva.Utilities;

/// <summary>
/// Extension methods for WPF UI elements.
/// </summary>
public static class UiExtensions
{
    #region Dispatcher helpers

    /// <summary>
    /// Asynchronous Invoke for Dispatcher with CancellationToken support.
    /// </summary>
    public static Task InvokeAsync(this Dispatcher dispatcher, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        dispatcher.BeginInvoke(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, priority);

        return tcs.Task;
    }

    /// <summary>
    /// Synchronous Invoke for Dispatcher.
    /// </summary>
    public static void Invoke(this Dispatcher dispatcher, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action, priority);
        }
    }

    /// <summary>
    /// Synchronous Invoke for DispatcherObject (Window, Page, etc.).
    /// </summary>
    public static void Invoke(this DispatcherObject obj, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        obj.Dispatcher.Invoke(action, priority);
    }

    /// <summary>
    /// Async invoke for DispatcherObject (Window, Page, etc.) with Func<Task>.
    /// </summary>
    public static async Task InvokeAsync(this DispatcherObject obj, Func<Task> action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        await obj.Dispatcher.InvokeAsync(async () => await action(), priority);
    }

    /// <summary>
    /// Async invoke for DispatcherObject (Window, Page, etc.) with a sync action.
    /// </summary>
    public static async Task InvokeAsync(this DispatcherObject obj, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        await obj.Dispatcher.InvokeAsync(action, priority);
    }

    #endregion

    #region ComboBox helpers

    /// <summary>
    /// Selects a ComboBoxItem by its Tag value.
    /// </summary>
    public static bool SelectItemByTag(this ComboBox comboBox, string tagValue)
    {
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == tagValue)
            {
                comboBox.SelectedIndex = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the Tag value of the selected item.
    /// </summary>
    public static string? GetSelectedTag(this ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString();
        return null;
    }

    #endregion

    #region ProgressRing helpers

    /// <summary>
    /// Shows the <see cref="SpinnerRing"/> spinner and starts spinning.
    /// </summary>
    public static void ShowIndeterminate(this SpinnerRing ring)
    {
        ring.StartSpin();
        ring.Visibility = System.Windows.Visibility.Visible;
    }

    /// <summary>
    /// Hides the spinner and stops spinning.
    /// </summary>
    public static void HideIndeterminate(this SpinnerRing ring)
    {
        ring.StopSpin();
        ring.Visibility = System.Windows.Visibility.Collapsed;
    }

    #endregion
}
