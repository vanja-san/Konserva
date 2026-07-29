using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using Microsoft.Win32;
using System.Windows;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace Konserva.Pages;

/// <summary>
/// Java-секция страницы настроек (вынесена в отдельный partial-файл)
/// </summary>
public partial class SettingsPage
{
    private void UpdateJavaEmptyVisibility()
    {
        JavaEmptyText.Visibility = _viewModel.IsJavaEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void ScanJava_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScanJavaButton.IsEnabled = false;
            ScanJavaButton.ToolTip = LocalizationManager.Get("Settings_Java_Scanning");
            JavaScanResultText.Visibility = Visibility.Collapsed;

            var totalFound = await Task.Run(() => _viewModel.ScanJava());

            RefreshUI();

            if (totalFound > 0)
            {
                JavaScanResultText.Text = LocalizationManager.Get("Settings_Java_Scan_Success", totalFound.ToString());
                JavaScanResultText.Visibility = Visibility.Visible;

                _ = AutoHideJavaScanResultAsync();
            }
            else
            {
                JavaScanResultText.Text = LocalizationManager.Get("Settings_Java_Scan_NoneFound");
                JavaScanResultText.Visibility = Visibility.Visible;

                _ = AutoHideJavaScanResultAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"ScanJava_Click error: {ex.Message}", ex, "SettingsPage");
            JavaScanResultText.Text = LocalizationManager.Get("Settings_Java_Scan_Error");
            JavaScanResultText.Visibility = Visibility.Visible;
            _ = AutoHideJavaScanResultAsync();
        }
        finally
        {
            ScanJavaButton.IsEnabled = true;
            ScanJavaButton.ToolTip = LocalizationManager.Get("Settings_Java_Scan");
        }
    }

    private async Task AutoHideJavaScanResultAsync()
    {
        try
        {
            await Task.Delay(Constants.InfoBarAutoCloseDelayMs);
            Dispatcher.Invoke(() => JavaScanResultText.Visibility = Visibility.Collapsed);
        }
        catch (Exception ex)
        {
            Logger.Warning($"AutoHideJavaScanResultAsync error: {ex.Message}", "SettingsPage");
        }
    }

    private async void AddJava_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationManager.Get("Settings_SelectJava"),
                Filter = LocalizationManager.Get("Settings_JavaFilter"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            if (dialog.ShowDialog() == true)
            {
                var java = _viewModel.AddJava(dialog.FileName);

                if (java != null)
                {
                    RefreshUI();

                    JavaSuccessInfoBar.Title = LocalizationManager.Get("Settings_JavaAdded");
                    JavaSuccessInfoBar.Message = $"{LocalizationManager.Get("Settings_JavaVersion")}: {java.Version}\n{LocalizationManager.Get("Settings_JavaPath")}: {java.Path}";
                    JavaSuccessInfoBar.IsOpen = true;

                    _ = AutoHideInfoBarAsync(JavaSuccessInfoBar, Constants.InfoBarAutoCloseDelayMs);
                }
                else
                {
                    await UiHelper.ShowWarning(LocalizationManager.Get("Settings_JavaInvalid"));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"AddJava_Click error: {ex.Message}", ex, "SettingsPage");
        }
    }

    private async void DeleteJava_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button { Tag: JavaInstallation java })
            {
                var result = await Dialogs.ConfirmDeleteDialog.ShowAsync(
                    java.DisplayName,
                    title: LocalizationManager.Get("Settings_Java_Delete_Confirm_Title"),
                    messageFormat: LocalizationManager.Get("Settings_Java_Delete_Confirm_Message"));

                if (result == ContentDialogResult.Primary)
                {
                    var removed = _viewModel.RemoveJava(java.Id);
                    if (removed)
                    {
                        RefreshUI();
                    }
                    else
                    {
                        await UiHelper.ShowWarning(LocalizationManager.Get("Settings_Java_Delete_Failed"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"DeleteJava_Click error: {ex.Message}", ex, "SettingsPage");
        }
    }
}
