using System;

namespace IPAbuyer.Models
{
    public sealed class DownloadQueueItem
    {
        public string BundleId { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Developer { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public DownloadQueueStatus Status { get; set; } = DownloadQueueStatus.Pending;
        public string LastMessage { get; set; } = string.Empty;
    }
}
