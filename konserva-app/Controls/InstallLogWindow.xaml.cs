using Konserva.Localization;
using System.Collections.ObjectModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace Konserva.Controls;

/// <summary>
/// Окно для просмотра подробного лога установки сервера
/// </summary>
public partial class InstallLogWindow : FluentWindow
{
    public InstallLogWindow(IEnumerable<string>? logEntries = null)
    {
        InitializeComponent();
        Title = LocalizationManager.Get("Installer_Log_Title");

        if (logEntries != null)
        {
            var clean = logEntries.Select(l => l.Replace("\r", ""));
            LogTextBox.Text = string.Join(Environment.NewLine, clean);
            LogTextBox.ScrollToEnd();
        }
    }

    public InstallLogWindow(ObservableCollection<string> logEntries)
    {
        InitializeComponent();
        Title = LocalizationManager.Get("Installer_Log_Title");

        if (logEntries.Count > 0)
        {
            var clean = logEntries.Select(l => l.Replace("\r", ""));
            LogTextBox.Text = string.Join(Environment.NewLine, clean);
            LogTextBox.ScrollToEnd();
        }
    }

    /// <summary>
    /// Добавить новую строку в лог
    /// </summary>
    public void AppendLog(string line)
    {
        // Удаляем \r — иначе текст может «съезжать» в WPF TextBox
        var cleanLine = line.Replace("\r", "");
        if (!string.IsNullOrEmpty(LogTextBox.Text))
            LogTextBox.AppendText(Environment.NewLine);
        LogTextBox.AppendText(cleanLine);
        LogTextBox.ScrollToEnd();
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(LogTextBox.Text))
            {
                Clipboard.SetText(LogTextBox.Text);
            }

            CopyButton.Content = LocalizationManager.Get("Common_Copied");
            CopyButton.IsEnabled = false;
            await Task.Delay(2000);
            CopyButton.Content = LocalizationManager.Get("Common_Copy");
            CopyButton.IsEnabled = true;
        }
        catch { /* ignore clipboard errors */ }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
