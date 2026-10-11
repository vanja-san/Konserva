namespace Konserva.Models
{
    /// <summary>
    /// Information about an available update.
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
        /// Expected SHA-256 of the asset (64 hex characters) from version.json.
        /// An empty string means the manifest has no checksum —
        /// in that case the update is not applied (see <see cref="Services.AppUpdater"/>).
        /// </summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>
        /// true, if version.json was read successfully (HTTP 200).
        /// false on timeout or another network error.
        /// </summary>
        public bool IsCheckSuccessful { get; set; }
    }
}
