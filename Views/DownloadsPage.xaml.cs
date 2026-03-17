using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IPAbuyer.Common;
using IPAbuyer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace IPAbuyer.Views
{
    public sealed partial class DownloadsPage : Page
    {
        private readonly DownloadQueueService _queueService = DownloadQueueService.Instance;
        private readonly StringBuilder _logBuilder = new();
        private readonly Queue<(string message, InfoBarSeverity severity)> _notificationQueue = new();
        private readonly DispatcherTimer _notificationTimer = new();
        private bool _notificationShowing;

        public DownloadsPage()
        {
            InitializeComponent();

            QueueListView.ItemsSource = _queueService.Items;
            _queueService.LogReceived += OnLogReceived;
            _queueService.QueueChanged += OnQueueChanged;

            _notificationTimer.Interval = TimeSpan.FromSeconds(2.8);
            _notificationTimer.Tick += NotificationTimer_Tick;

            UpdateButtons();
        }

        private async void StartQueueButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartQueueButton.IsEnabled = false;
                await _queueService.StartQueueAsync();
            }
            catch (Exception ex)
            {
                string message = $"开始下载失败: {ex.Message}";
                AppendLog($"[错误] {message}");
                EnqueueNotification(message, InfoBarSeverity.Error);
            }
            finally
            {
                UpdateButtons();
            }
        }

        private void RemoveQueueButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = QueueListView.SelectedItems.OfType<DownloadQueueItem>().ToList();
            if (selected.Count == 0)
            {
                const string message = "请先选择要移出的队列项";
                AppendLog($"[提示] {message}");
                EnqueueNotification(message, InfoBarSeverity.Warning);
                return;
            }

            int removed = _queueService.RemoveItems(selected);
            string removedMessage = $"已移出 {removed} 项";
            AppendLog($"[提示] {removedMessage}");
            EnqueueNotification(removedMessage, InfoBarSeverity.Informational);
            RefreshQueueView();
        }

        private void OpenDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string outputDirectory = KeychainConfig.GetDownloadDirectory();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{outputDirectory}\"",
                    UseShellExecute = true
                });

                EnqueueNotification("已打开下载目录", InfoBarSeverity.Informational);
            }
            catch (Exception ex)
            {
                string message = $"打开下载目录失败: {ex.Message}";
                AppendLog($"[错误] {message}");
                EnqueueNotification(message, InfoBarSeverity.Error);
            }
        }

        private void CancelAllButton_Click(object sender, RoutedEventArgs e)
        {
            _queueService.CancelAll();
            RefreshQueueView();
            UpdateButtons();
            EnqueueNotification("已请求终止所有下载任务", InfoBarSeverity.Warning);
        }

        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            string text = LogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                EnqueueNotification("日志为空，无可复制内容", InfoBarSeverity.Warning);
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();

            EnqueueNotification("日志已复制到剪贴板", InfoBarSeverity.Success);
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logBuilder.Clear();
            LogTextBox.Text = string.Empty;
            EnqueueNotification("日志已清空", InfoBarSeverity.Informational);
        }

        private void CancelCurrentButton_Click(object sender, RoutedEventArgs e)
        {
            _queueService.CancelCurrent();
            RefreshQueueView();
            EnqueueNotification("已请求终止当前下载任务", InfoBarSeverity.Warning);
        }

        private void OnLogReceived(string log)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendLog(log);

                if (TryClassifyLog(log, out string message, out InfoBarSeverity severity))
                {
                    EnqueueNotification(message, severity);
                }
            });
        }

        private void OnQueueChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                RefreshQueueView();
                UpdateButtons();
            });
        }

        private void RefreshQueueView()
        {
            QueueListView.ItemsSource = null;
            QueueListView.ItemsSource = _queueService.Items;
        }

        private void UpdateButtons()
        {
            bool running = _queueService.IsRunning;
            StartQueueButton.IsEnabled = !running;
            RemoveQueueButton.IsEnabled = !running;
            CancelAllButton.IsEnabled = running;
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _logBuilder.AppendLine(message);
            LogTextBox.Text = _logBuilder.ToString();
            LogTextBox.SelectionStart = LogTextBox.Text.Length;
        }

        private void EnqueueNotification(string message, InfoBarSeverity severity)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _notificationQueue.Enqueue((message, severity));
            TryShowNextNotification();
        }

        private void TryShowNextNotification()
        {
            if (_notificationShowing || _notificationQueue.Count == 0)
            {
                return;
            }

            var item = _notificationQueue.Dequeue();
            StatusInfoBar.Severity = item.severity;
            StatusInfoBar.Message = item.message;
            StatusInfoBar.IsOpen = true;

            _notificationShowing = true;
            _notificationTimer.Stop();
            _notificationTimer.Start();
        }

        private void NotificationTimer_Tick(object sender, object e)
        {
            _notificationTimer.Stop();
            if (StatusInfoBar.IsOpen)
            {
                StatusInfoBar.IsOpen = false;
            }
            else
            {
                _notificationShowing = false;
                TryShowNextNotification();
            }
        }

        private void StatusInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            _notificationTimer.Stop();
            _notificationShowing = false;
            TryShowNextNotification();
        }

        private static bool TryClassifyLog(string log, out string message, out InfoBarSeverity severity)
        {
            message = StripTimestampPrefix(log);
            severity = InfoBarSeverity.Informational;

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            if (message.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || message.Contains("错误", StringComparison.OrdinalIgnoreCase)
                || message.Contains("异常", StringComparison.OrdinalIgnoreCase))
            {
                severity = InfoBarSeverity.Error;
                return true;
            }

            if (message.Contains("成功", StringComparison.OrdinalIgnoreCase)
                || message.Contains("完成", StringComparison.OrdinalIgnoreCase))
            {
                severity = InfoBarSeverity.Success;
                return true;
            }

            if (message.Contains("终止", StringComparison.OrdinalIgnoreCase)
                || message.Contains("取消", StringComparison.OrdinalIgnoreCase)
                || message.Contains("超时", StringComparison.OrdinalIgnoreCase))
            {
                severity = InfoBarSeverity.Warning;
                return true;
            }

            return false;
        }

        private static string StripTimestampPrefix(string log)
        {
            if (string.IsNullOrWhiteSpace(log))
            {
                return string.Empty;
            }

            string trimmed = log.Trim();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int end = trimmed.IndexOf("]", StringComparison.Ordinal);
            if (end < 0 || end + 1 >= trimmed.Length)
            {
                return trimmed;
            }

            return trimmed.Substring(end + 1).Trim();
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            _notificationTimer.Stop();
            _notificationTimer.Tick -= NotificationTimer_Tick;

            _queueService.LogReceived -= OnLogReceived;
            _queueService.QueueChanged -= OnQueueChanged;
        }
    }
}