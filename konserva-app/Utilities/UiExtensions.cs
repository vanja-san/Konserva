using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Konserva.Utilities;

/// <summary>
/// Extension methods для WPF UI элементов.
/// </summary>
public static class UiExtensions
{
    #region Dispatcher helpers

    /// <summary>
    /// Асинхронный Invoke для Dispatcher с поддержкой CancellationToken.
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
    /// Синхронный Invoke для Dispatcher.
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
    /// Синхронный Invoke для DispatcherObject (Window, Page и т.д.).
    /// </summary>
    public static void Invoke(this DispatcherObject obj, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        obj.Dispatcher.Invoke(action, priority);
    }

    /// <summary>
    /// Async инвок для DispatcherObject (Window, Page и т.д.) с Func<Task>.
    /// </summary>
    public static async Task InvokeAsync(this DispatcherObject obj, Func<Task> action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        await obj.Dispatcher.InvokeAsync(async () => await action(), priority);
    }

    /// <summary>
    /// Async инвок для DispatcherObject (Window, Page и т.д.) с sync action.
    /// </summary>
    public static async Task InvokeAsync(this DispatcherObject obj, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        await obj.Dispatcher.InvokeAsync(action, priority);
    }

    #endregion

    #region ComboBox helpers

    /// <summary>
    /// Выбирает ComboBoxItem по значению Tag.
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
    /// Получает значение Tag выбранного элемента.
    /// </summary>
    public static string? GetSelectedTag(this ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString();
        return null;
    }

    #endregion
}
