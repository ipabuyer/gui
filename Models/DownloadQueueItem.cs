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
        public string Price { get; set; } = string.Empty;
        public string ArtworkUrl { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public DownloadQueueStatus Status { get; set; } = DownloadQueueStatus.Pending;
        public string LastMessage { get; set; } = string.Empty;

        public string StatusText => Status switch
        {
            DownloadQueueStatus.Pending => "等待下载",
            DownloadQueueStatus.Downloading => string.IsNullOrWhiteSpace(LastMessage) ? "下载中" : LastMessage,
            DownloadQueueStatus.Success => "下载成功",
            DownloadQueueStatus.Failed => "下载失败",
            DownloadQueueStatus.Canceled => "已终止",
            _ => Status.ToString()
        };
    }
}
