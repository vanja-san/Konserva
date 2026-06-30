namespace Konserva.Models
{
    /// <summary>
    /// Информация о доступном обновлении.
    /// </summary>
    public class UpdateInfo
    {
        public bool IsAvailable { get; set; }
        public string NewVersion { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ChangelogUrl { get; set; } = string.Empty;

        /// <summary>
        /// true, если удалось прочитать version.json (HTTP 200).
        /// false при таймауте или другой сетевой ошибке.
        /// </summary>
        public bool IsCheckSuccessful { get; set; }
    }
}
