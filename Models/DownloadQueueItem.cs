using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IPAbuyer.Models
{
    public sealed class DownloadQueueItem : INotifyPropertyChanged
    {
        private DownloadQueueStatus _status = DownloadQueueStatus.Pending;
        private string _lastMessage = string.Empty;

        public string BundleId { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Developer { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
        public string ArtworkUrl { get; set; } = string.Empty;
        public DateTime AddedAt { get; set; } = DateTime.Now;

        public DownloadQueueStatus Status
        {
            get => _status;
            set
            {
                if (_status == value)
                {
                    return;
                }

                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string LastMessage
        {
            get => _lastMessage;
            set
            {
                string newValue = value ?? string.Empty;
                if (string.Equals(_lastMessage, newValue, StringComparison.Ordinal))
                {
                    return;
                }

                _lastMessage = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText => Status switch
        {
            DownloadQueueStatus.Pending => "等待下载",
            DownloadQueueStatus.Downloading => string.IsNullOrWhiteSpace(LastMessage) ? "下载中" : LastMessage,
            DownloadQueueStatus.Success => "下载成功",
            DownloadQueueStatus.Failed => "下载失败",
            DownloadQueueStatus.Canceled => "已终止",
            _ => Status.ToString()
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
