using System;
using System.Windows;
using System.Windows.Controls;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using Konserva.Localization;

namespace Konserva.Controls
{
    public partial class UpdateNotification : UserControl
    {
        private UpdateInfo? _updateInfo;
        private bool _isUpdating;

        public UpdateNotification()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Показывает уведомление о доступном обновлении.
        /// </summary>
        public void Show(UpdateInfo updateInfo)
        {
            _updateInfo = updateInfo;
            _isUpdating = false;

            VersionText.Text = $"v{updateInfo.NewVersion}";
            UpdateButtonText.Text = LocalizationManager.Get("Update_Button", "Update");
            UpdateButton.IsEnabled = true;
            UpdateButton.ToolTip = LocalizationManager.Get("Update_Available_Tooltip", "Click to update");

            Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Прячет уведомление.
        /// </summary>
        public void Hide()
        {
            Visibility = Visibility.Collapsed;
            _updateInfo = null;
        }

        /// <summary>
        /// Показывает статус загрузки.
        /// </summary>
        private void SetUpdatingState(string message)
        {
            UpdateButtonText.Text = message;
            UpdateButton.IsEnabled = false;
            UpdateButton.ToolTip = null;
        }

        /// <summary>
        /// Показывает ошибку.
        /// </summary>
        private void SetErrorState()
        {
            UpdateButtonText.Text = LocalizationManager.Get("Update_Retry", "Retry");
            UpdateButton.IsEnabled = true;
            UpdateButton.ToolTip = LocalizationManager.Get("Update_Failed_Tooltip", "Update failed. Click to retry.");
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null || _isUpdating)
                return;

            _isUpdating = true;
            SetUpdatingState(LocalizationManager.Get("Update_Downloading", "Downloading..."));

            try
            {
                var progress = new Progress<double>(p =>
                {
                    // Обновляем текст прогресса в UI-потоке
                    Dispatcher.Invoke(() =>
                    {
                        var pct = (int)p;
                        if (pct < 90)
                            SetUpdatingState($"{LocalizationManager.Get("Update_Downloading", "Downloading...")} {pct}%");
                        else if (pct < 100)
                            SetUpdatingState(LocalizationManager.Get("Update_Installing", "Installing..."));
                        else
                            SetUpdatingState(LocalizationManager.Get("Update_Success", "Restarting..."));
                    });
                });

                var success = await AppUpdater.ApplyAsync(_updateInfo, progress);
                if (!success)
                {
                    SetErrorState();
                    _isUpdating = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Update button error: {ex.Message}", ex, "UpdateNotification");
                SetErrorState();
                _isUpdating = false;
            }
        }
    }
}
