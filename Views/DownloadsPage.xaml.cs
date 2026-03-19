using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IPAbuyer.Common;
using IPAbuyer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace IPAbuyer.Views
{
    public sealed partial class DownloadsPage : Page
    {
        private readonly DownloadQueueService _queueService = DownloadQueueService.Instance;
        private readonly StringBuilder _logBuilder = new();
        private string? _sortKey;
        private SortDirection _sortDirection = SortDirection.None;

        private const string NameHeaderBase = "App名称";
        private const string IdHeaderBase = "AppID";
        private const string DeveloperHeaderBase = "开发者";
        private const string VersionHeaderBase = "版本号";
        private const string PriceHeaderBase = "价格";
        private const string StatusHeaderBase = "下载状态";

        public DownloadsPage()
        {
            InitializeComponent();

            RefreshQueueView();
            UpdateSortHeaderTexts();
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
            QueueListView.ItemsSource = GetDisplayedItems();
        }

        private List<DownloadQueueItem> GetDisplayedItems()
        {
            var source = _queueService.Items.ToList();
            if (string.IsNullOrWhiteSpace(_sortKey) || _sortDirection == SortDirection.None)
            {
                return source;
            }

            Func<DownloadQueueItem, object?> keySelector = _sortKey switch
            {
                "name" => item => item.Name ?? string.Empty,
                "appid" => item => item.AppId ?? string.Empty,
                "developer" => item => item.Developer ?? string.Empty,
                "version" => item => item.Version ?? string.Empty,
                "price" => item => GetPriceSortValue(item.Price),
                "status" => item => item.StatusText ?? string.Empty,
                _ => item => item.Name ?? string.Empty
            };

            IOrderedEnumerable<DownloadQueueItem> ordered = _sortDirection == SortDirection.Ascending
                ? source.OrderBy(keySelector)
                : source.OrderByDescending(keySelector);

            return ordered.ToList();
        }

        private static decimal GetPriceSortValue(string? price)
        {
            if (string.IsNullOrWhiteSpace(price))
            {
                return decimal.MaxValue;
            }

            string normalized = price.Trim();
            if (normalized.Equals("免费", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("free", StringComparison.OrdinalIgnoreCase))
            {
                return 0m;
            }

            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                ? value
                : decimal.MaxValue - 1;
        }

        private void SortHeader_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not TextBlock header)
            {
                return;
            }

            string key = header.Tag?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!string.Equals(_sortKey, key, StringComparison.Ordinal))
            {
                _sortKey = key;
                _sortDirection = SortDirection.Ascending;
            }
            else
            {
                _sortDirection = _sortDirection switch
                {
                    SortDirection.None => SortDirection.Ascending,
                    SortDirection.Ascending => SortDirection.Descending,
                    SortDirection.Descending => SortDirection.None,
                    _ => SortDirection.None
                };

                if (_sortDirection == SortDirection.None)
                {
                    _sortKey = null;
                }
            }

            UpdateSortHeaderTexts();
            RefreshQueueView();
        }

        private void UpdateSortHeaderTexts()
        {
            SetHeaderText(DownloadNameHeaderText, NameHeaderBase, "name");
            SetHeaderText(DownloadIdHeaderText, IdHeaderBase, "appid");
            SetHeaderText(DownloadDeveloperHeaderText, DeveloperHeaderBase, "developer");
            SetHeaderText(DownloadVersionHeaderText, VersionHeaderBase, "version");
            SetHeaderText(DownloadPriceHeaderText, PriceHeaderBase, "price");
            SetHeaderText(DownloadStatusHeaderText, StatusHeaderBase, "status");
        }

        private void SetHeaderText(TextBlock? textBlock, string baseText, string key)
        {
            if (textBlock == null)
            {
                return;
            }

            if (!string.Equals(_sortKey, key, StringComparison.Ordinal) || _sortDirection == SortDirection.None)
            {
                textBlock.Text = baseText;
                return;
            }

            string suffix = _sortDirection == SortDirection.Ascending ? " ↑" : " ↓";
            textBlock.Text = baseText + suffix;
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

        private enum SortDirection
        {
            None = 0,
            Ascending = 1,
            Descending = 2
        }
    }
}
