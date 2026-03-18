using System;
using System.Linq;
using System.Text;
using IPAbuyer.Common;
using IPAbuyer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace IPAbuyer.Views
{
    public sealed partial class DownloadsPage : Page
    {
        private readonly DownloadQueueService _queueService = DownloadQueueService.Instance;
        private readonly StringBuilder _logBuilder = new();

        public DownloadsPage()
        {
            InitializeComponent();

            QueueListView.ItemsSource = _queueService.Items;
            _queueService.LogReceived += OnLogReceived;
            _queueService.QueueChanged += OnQueueChanged;

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
                return;
            }

            int removed = _queueService.RemoveItems(selected);
            string removedMessage = $"已移出 {removed} 项";
            AppendLog($"[提示] {removedMessage}");
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
                AppendLog("[提示] 已打开下载目录");
            }
            catch (Exception ex)
            {
                string message = $"打开下载目录失败: {ex.Message}";
                AppendLog($"[错误] {message}");
            }
        }

        private void CancelAllButton_Click(object sender, RoutedEventArgs e)
        {
            _queueService.CancelAll();
            RefreshQueueView();
            UpdateButtons();
            AppendLog("[提示] 已请求终止所有下载任务");
        }

        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            string text = LogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendLog("[提示] 日志为空，无可复制内容");
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
            AppendLog("[提示] 日志已复制到剪贴板");
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _logBuilder.Clear();
            LogTextBox.Text = string.Empty;
        }

        private void CancelCurrentButton_Click(object sender, RoutedEventArgs e)
        {
            _queueService.CancelCurrent();
            RefreshQueueView();
            AppendLog("[提示] 已请求终止当前下载任务");
        }

        private void OnLogReceived(string log)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendLog(log);
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
            ScrollLogToBottom(LogTextBox);
        }

        private static void ScrollLogToBottom(TextBox textBox)
        {
            textBox.SelectionStart = textBox.Text.Length;
            textBox.SelectionLength = 0;

            var scrollViewer = FindDescendantScrollViewer(textBox);
            scrollViewer?.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: true);
        }

        private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
        {
            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }

                ScrollViewer? nested = FindDescendantScrollViewer(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            _queueService.LogReceived -= OnLogReceived;
            _queueService.QueueChanged -= OnQueueChanged;
        }
    }
}
