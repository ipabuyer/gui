using System;
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
                AppendLog($"[错误] {ex.Message}");
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
                AppendLog("[提示] 请先选择要移出的队列项");
                return;
            }

            int removed = _queueService.RemoveItems(selected);
            AppendLog($"[提示] 已移出 {removed} 项");
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
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] 打开下载目录失败: {ex.Message}");
            }
        }

        private void CancelAllButton_Click(object sender, RoutedEventArgs e)
        {
            _queueService.CancelAll();
            RefreshQueueView();
            UpdateButtons();
        }

        private void CopyLogButton_Click(object sender, RoutedEventArgs e)
        {
            string text = LogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
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
            LogTextBox.SelectionStart = LogTextBox.Text.Length;
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _queueService.LogReceived -= OnLogReceived;
            _queueService.QueueChanged -= OnQueueChanged;
        }
    }
}
