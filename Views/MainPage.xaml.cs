using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IPAbuyer.Common;
using IPAbuyer.Data;
using IPAbuyer.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace IPAbuyer.Views
{
    public sealed partial class MainPage : Page
    {
        private readonly List<SearchResult> _allResults = new();
        private readonly DownloadQueueService _downloadQueueService = DownloadQueueService.Instance;
        private readonly StringBuilder _homeLogBuilder = new();
        private readonly List<UiLogEntry> _homeLogEntries = new();
        private CancellationTokenSource _pageCts = new();
        private string _selectedFilter = "All";
        private string? _sortKey;
        private SortDirection _sortDirection = SortDirection.None;
        public event Action<bool>? SearchLoadingChanged;

        private const string StatusPurchased = "已购买";
        private const string StatusOwned = "已拥有";
        private const string StatusCanPurchase = "未购买";

        private static readonly string[] PurchasedAliases = { "已购买", "宸茶喘涔?" };
        private static readonly string[] OwnedAliases = { "已拥有", "宸叉嫢鏈?" };
        private static readonly string[] CanPurchaseAliases = { "可购买", "未购买", "鍙喘涔?", "鏈喘涔?" };
        private const string NameHeaderBase = "App名称";
        private const string IdHeaderBase = "AppID";
        private const string DeveloperHeaderBase = "开发者";
        private const string VersionHeaderBase = "版本号";
        private const string PriceHeaderBase = "价格";
        private const string PurchasedHeaderBase = "购买状态";
        private const int MaxLogLines = 1000;

        public int SearchLimitNum { get; set; } = 100;

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            IpatoolExecution.CommandExecuting -= OnIpatoolCommandExecuting;
            IpatoolExecution.CommandExecuting += OnIpatoolCommandExecuting;
            IpatoolExecution.CommandOutputReceived -= OnIpatoolCommandOutputReceived;
            IpatoolExecution.CommandOutputReceived += OnIpatoolCommandOutputReceived;

            if (MainPageCacheState.ConsumeSearchCacheInvalidation())
            {
                ClearSearchCache();
            }
        }

        public async void PerformSearchFromMainWindow(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                AppendHomeLog("搜索词为空，已忽略。");
                return;
            }

            AppendHomeLog($"开始搜索: {appName.Trim()}");
            SearchLoadingChanged?.Invoke(true);
            try
            {
                await PerformSearchAsync(appName.Trim(), _pageCts.Token);
            }
            catch (Exception ex)
            {
                AppendHomeLog($"搜索异常: {ex.Message}");
            }
            finally
            {
                SearchLoadingChanged?.Invoke(false);
            }
        }

        private async Task PerformSearchAsync(string appName, CancellationToken cancellationToken)
        {
            string account = GetActiveAccount();
            string countryCode = NormalizeCountryCode(KeychainConfig.GetCountryCode(account));

            var result = await IpatoolExecution.SearchAppAsync(appName, SearchLimitNum, account, countryCode, cancellationToken);
            if (result.TimedOut || string.IsNullOrWhiteSpace(result.OutputOrError))
            {
                if (ResultList != null)
                {
                    ResultList.ItemsSource = null;
                }

                AppendHomeLog("搜索超时或无返回结果。");
                return;
            }

            ParseSearchResponse(result.OutputOrError, account);
        }

        private void ParseSearchResponse(string payload, string account)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (!root.TryGetProperty("results", out var resultsElement) || resultsElement.ValueKind != JsonValueKind.Array)
                {
                    if (ResultList != null)
                    {
                        ResultList.ItemsSource = null;
                    }

                    return;
                }

                var purchasedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(account))
                {
                    foreach (var record in PurchasedAppDb.GetPurchasedApps(account))
                    {
                        if (string.IsNullOrWhiteSpace(record.appID))
                        {
                            continue;
                        }

                        purchasedDict[record.appID] = record.status;
                    }
                }

                _allResults.Clear();
                foreach (var appElement in resultsElement.EnumerateArray())
                {
                    string bundleId = GetBundleId(appElement) ?? string.Empty;
                    string status = purchasedDict.TryGetValue(bundleId, out string? purchasedStatus)
                        ? NormalizePurchasedStatus(purchasedStatus)
                        : StatusCanPurchase;

                    _allResults.Add(new SearchResult
                    {
                        bundleId = bundleId,
                        id = GetPropertyValue(appElement, "trackId"),
                        name = GetPropertyValue(appElement, "trackName"),
                        developer = GetPropertyValue(appElement, "sellerName"),
                        artworkUrl = GetPropertyValue(appElement, "artworkUrl100"),
                        price = NormalizePriceForDisplay(GetPriceValue(appElement)),
                        version = GetPropertyValue(appElement, "version"),
                        purchased = status
                    });
                }

                ApplyFilterAndRefresh();
                AppendHomeLog($"搜索完成，共 {_allResults.Count} 条结果。");
            }
            catch (JsonException)
            {
                if (ResultList != null)
                {
                    ResultList.ItemsSource = null;
                }

                AppendHomeLog("搜索结果解析失败。");
            }
        }

        private async void BatchPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultList == null)
            {
                return;
            }

            var selectedApps = ResultList.SelectedItems.OfType<SearchResult>().ToList();
            if (selectedApps.Count == 0)
            {
                AppendHomeLog("未选择应用，无法批量购买。");
                return;
            }

            if (BatchPurchaseButton != null)
            {
                BatchPurchaseButton.IsEnabled = false;
            }
            SetPurchaseLoading(true);

            try
            {
                AppendHomeLog($"开始批量购买，共 {selectedApps.Count} 项。");
                bool executed = await PurchaseAppsAsync(selectedApps);
                if (executed)
                {
                    AppendHomeLog("批量购买执行完成。");
                }
            }
            catch (OperationCanceledException)
            {
                AppendHomeLog("批量购买已取消。");
            }
            catch (Exception ex)
            {
                AppendHomeLog($"批量购买异常: {ex.Message}");
            }
            finally
            {
                if (BatchPurchaseButton != null)
                {
                    BatchPurchaseButton.IsEnabled = true;
                }
                SetPurchaseLoading(false);

                ApplyFilterAndRefresh(preserveScrollPosition: true);
            }
        }

        private void AddToDownloadQueueButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultList == null)
            {
                return;
            }

            int added = 0;
            foreach (var app in ResultList.SelectedItems.OfType<SearchResult>())
            {
                _downloadQueueService.AddOrUpdateFromSearchResult(app);
                added++;
            }

            AppendHomeLog(added == 0 ? "未选择应用，未加入下载队列。" : $"已加入下载队列: {added} 项。");
        }

        private async void ContextMenuPurchase_Click(object sender, RoutedEventArgs e)
        {
            var selectedApps = GetContextTargetApps(sender);
            if (selectedApps.Count == 0)
            {
                return;
            }

            if (BatchPurchaseButton != null)
            {
                BatchPurchaseButton.IsEnabled = false;
            }
            SetPurchaseLoading(true);

            try
            {
                AppendHomeLog($"右键购买，共 {selectedApps.Count} 项。");
                _ = await PurchaseAppsAsync(selectedApps);
            }
            catch (OperationCanceledException)
            {
                AppendHomeLog("右键购买已取消。");
            }
            catch (Exception ex)
            {
                AppendHomeLog($"右键购买异常: {ex.Message}");
            }
            finally
            {
                if (BatchPurchaseButton != null)
                {
                    BatchPurchaseButton.IsEnabled = true;
                }
                SetPurchaseLoading(false);

                ApplyFilterAndRefresh(preserveScrollPosition: true);
            }
        }

        private void ContextMenuAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var selectedApps = GetContextTargetApps(sender);
            if (selectedApps.Count == 0)
            {
                return;
            }

            foreach (var app in selectedApps)
            {
                _downloadQueueService.AddOrUpdateFromSearchResult(app);
            }

            AppendHomeLog($"右键加入下载队列: {selectedApps.Count} 项。");
        }

        private SearchResult? ResolveContextItem(object sender)
        {
            if (sender is not MenuFlyoutItem menuItem)
            {
                return null;
            }

            if (menuItem.DataContext is SearchResult direct)
            {
                return direct;
            }

            if (menuItem.Parent is MenuFlyout flyout &&
                flyout.Target is FrameworkElement target &&
                target.DataContext is SearchResult fromTarget)
            {
                return fromTarget;
            }

            return null;
        }

        private List<SearchResult> GetContextTargetApps(object sender)
        {
            var contextItem = ResolveContextItem(sender);
            if (contextItem == null || ResultList == null)
            {
                return new List<SearchResult>();
            }

            var selectedItems = ResultList.SelectedItems.OfType<SearchResult>().ToList();
            if (selectedItems.Count > 1 && selectedItems.Contains(contextItem))
            {
                return selectedItems;
            }

            return new List<SearchResult> { contextItem };
        }

        private void ContextMenuMarkNotPurchased_Click(object sender, RoutedEventArgs e)
        {
            MarkAppsStatus(sender, StatusCanPurchase);
        }

        private void ContextMenuMarkPurchased_Click(object sender, RoutedEventArgs e)
        {
            MarkAppsStatus(sender, StatusPurchased);
        }

        private void ContextMenuMarkOwned_Click(object sender, RoutedEventArgs e)
        {
            MarkAppsStatus(sender, StatusOwned);
        }

        private void MarkAppsStatus(object sender, string status)
        {
            var selectedApps = GetContextTargetApps(sender);
            if (selectedApps.Count == 0)
            {
                return;
            }

            string account = GetActiveAccount();
            foreach (var app in selectedApps)
            {
                if (string.IsNullOrWhiteSpace(app.bundleId))
                {
                    continue;
                }

                app.purchased = status;
                if (string.IsNullOrWhiteSpace(account))
                {
                    continue;
                }

                if (status == StatusCanPurchase)
                {
                    PurchasedAppDb.RemovePurchasedApp(app.bundleId, account);
                }
                else
                {
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, status);
                }
            }

            ApplyFilterAndRefresh(preserveScrollPosition: true);
            AppendHomeLog($"已标记 {selectedApps.Count} 项为 {status}。");
        }

        private void ContextMenuCopyName_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.name ?? string.Empty, "App 名称");
        }

        private void ContextMenuCopyId_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.id ?? string.Empty, "ID");
        }

        private void ContextMenuCopyVersion_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.version ?? string.Empty, "版本号");
        }

        private void CopyField(object sender, Func<SearchResult, string> selector, string fieldName)
        {
            var selectedApps = GetContextTargetApps(sender);
            if (selectedApps.Count == 0)
            {
                return;
            }

            string value = string.Join(Environment.NewLine, selectedApps.Select(selector).Where(v => !string.IsNullOrWhiteSpace(v)));
            if (string.IsNullOrWhiteSpace(value))
            {
                AppendHomeLog($"复制失败：{fieldName}为空。");
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(value);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendHomeLog($"已复制{fieldName}，共 {selectedApps.Count} 项。");
        }

        private void ScreeningComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreeningComboBox != null && ScreeningComboBox.SelectedItem is ComboBoxItem selected)
            {
                _selectedFilter = selected.Tag?.ToString() ?? "All";
                ApplyFilterAndRefresh();
                AppendHomeLog($"切换筛选: {_selectedFilter}");
            }
        }

        private void ApplyFilterAndRefresh(bool preserveScrollPosition = false)
        {
            if (ResultList == null)
            {
                return;
            }

            ScrollViewer? listScrollViewer = null;
            double? previousVerticalOffset = null;
            if (preserveScrollPosition)
            {
                listScrollViewer = FindDescendantScrollViewer(ResultList);
                previousVerticalOffset = listScrollViewer?.VerticalOffset;
            }

            var filtered = GetFilteredResults();
            ResultList.ItemsSource = filtered;

            if (preserveScrollPosition && listScrollViewer != null && previousVerticalOffset.HasValue)
            {
                double targetOffset = previousVerticalOffset.Value;
                DispatcherQueue.TryEnqueue(() =>
                {
                    listScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
                });
            }
        }

        private List<SearchResult> GetFilteredResults()
        {
            List<SearchResult> filtered = _selectedFilter switch
            {
                "OnlyPurchased" => _allResults.Where(a => IsPurchasedStatus(a.purchased)).ToList(),
                "OnlyNotPurchased" => _allResults.Where(a => IsCanPurchaseStatus(a.purchased)).ToList(),
                "OnlyHad" => _allResults.Where(a => IsOwnedStatus(a.purchased)).ToList(),
                _ => _allResults.ToList(),
            };

            return ApplySorting(filtered);
        }

        private async Task<bool> PurchaseAppsAsync(List<SearchResult> selectedApps)
        {
            string account = GetActiveAccount();
            if (string.IsNullOrWhiteSpace(account))
            {
                AppendHomeLog("请先在账户页面登录后再购买。");
                return false;
            }

            bool isTestAccount = SessionState.IsLoggedIn
                && SessionState.IsMockAccount
                && string.Equals(SessionState.CurrentAccount, account, StringComparison.OrdinalIgnoreCase);

            foreach (var app in selectedApps)
            {
                if (string.IsNullOrWhiteSpace(app.bundleId))
                {
                    continue;
                }

                if (IsPurchasedStatus(app.purchased) || IsOwnedStatus(app.purchased))
                {
                    continue;
                }

                if (!IsPriceFree(app.price))
                {
                    AppendHomeLog($"非免费应用，已跳过购买: {app.name ?? app.bundleId}");
                    continue;
                }

                if (isTestAccount)
                {
                    app.purchased = StatusPurchased;
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusPurchased);
                    AppendHomeLog($"测试账户购买成功: {app.name ?? app.bundleId}");
                    continue;
                }

                var result = await IpatoolExecution.PurchaseAppAsync(app.bundleId, account, _pageCts.Token);
                if (IsPurchaseSuccess(result.OutputOrError))
                {
                    app.purchased = StatusPurchased;
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusPurchased);
                    AppendHomeLog($"购买成功: {app.name ?? app.bundleId}");
                }
                else
                {
                    if (IsStdqOwnedCandidate(result.OutputOrError))
                    {
                        bool shouldMarkOwned = await ConfirmMarkOwnedAsync(app).ConfigureAwait(true);
                        if (shouldMarkOwned)
                        {
                            app.purchased = StatusOwned;
                            PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusOwned);
                            AppendHomeLog($"疑似已拥有，已标记: {app.name ?? app.bundleId}");
                        }
                        else
                        {
                            AppendHomeLog($"疑似已拥有，未标记: {app.name ?? app.bundleId}");
                        }
                    }
                    else
                    {
                        string reason = string.IsNullOrWhiteSpace(result.OutputOrError)
                            ? "未知错误"
                            : result.OutputOrError;
                        AppendHomeLog($"购买失败: {app.name ?? app.bundleId} - {reason}");
                    }
                }
            }
            return true;
        }

        private async Task<bool> ConfirmMarkOwnedAsync(SearchResult app)
        {
            if (KeychainConfig.GetOwnedCheckEnabled())
            {
                return true;
            }

            var disablePromptCheckBox = new CheckBox
            {
                Content = "不再提示，后续直接标记为已拥有"
            };

            var contentPanel = new StackPanel { Spacing = 8 };
            contentPanel.Children.Add(new TextBlock
            {
                Text = $"ipatool 返回 STDQ：{app.name ?? app.bundleId} 可能已拥有，是否标记为“已拥有”？",
                TextWrapping = TextWrapping.Wrap
            });
            contentPanel.Children.Add(disablePromptCheckBox);

            var dialog = new ContentDialog
            {
                Title = "疑似已拥有",
                Content = contentPanel,
                PrimaryButtonText = "标记为已拥有",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };

            ContentDialogResult dialogResult = await dialog.ShowAsync();
            bool shouldMark = dialogResult == ContentDialogResult.Primary;
            if (shouldMark && disablePromptCheckBox.IsChecked == true)
            {
                KeychainConfig.SaveOwnedCheckEnabled(true);
            }

            return shouldMark;
        }

        private static string NormalizePurchasedStatus(string? status)
        {
            if (IsPurchasedStatus(status))
            {
                return StatusPurchased;
            }

            if (IsOwnedStatus(status))
            {
                return StatusOwned;
            }

            return StatusCanPurchase;
        }

        private static bool IsPurchasedStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return PurchasedAliases.Any(alias => string.Equals(status, alias, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOwnedStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return OwnedAliases.Any(alias => string.Equals(status, alias, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCanPurchaseStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return true;
            }

            return CanPurchaseAliases.Any(alias => string.Equals(status, alias, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPurchaseSuccess(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            if (response.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                using var doc = JsonDocument.Parse(response);
                return doc.RootElement.TryGetProperty("success", out var successElement)
                    && successElement.ValueKind == JsonValueKind.True
                    && successElement.GetBoolean();
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string GetActiveAccount()
        {
            string account = SessionState.IsLoggedIn ? SessionState.CurrentAccount : string.Empty;
            return account.Trim();
        }

        private static bool IsPriceFree(string? price)
        {
            if (string.IsNullOrWhiteSpace(price))
            {
                return false;
            }

            if (decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
            {
                return value <= 0m;
            }

            string normalized = price.Trim();
            return normalized.Equals("free", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("免费", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetBundleId(JsonElement element)
        {
            if (element.TryGetProperty("bundleID", out var bundleId))
            {
                return bundleId.GetString();
            }

            if (element.TryGetProperty("bundleId", out var bundleIdAlt))
            {
                return bundleIdAlt.GetString();
            }

            return null;
        }

        private static string? GetPropertyValue(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.GetRawText()
            };
        }

        private static string? GetPriceValue(JsonElement element)
        {
            if (!element.TryGetProperty("price", out var priceElement))
            {
                return null;
            }

            return priceElement.ValueKind switch
            {
                JsonValueKind.Number => priceElement.TryGetDecimal(out var decimalValue)
                    ? decimalValue.ToString("0.00")
                    : priceElement.GetRawText(),
                JsonValueKind.String => priceElement.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => priceElement.GetRawText()
            };
        }

        private static string NormalizePriceForDisplay(string? rawPrice)
        {
            if (string.IsNullOrWhiteSpace(rawPrice))
            {
                return string.Empty;
            }

            if (decimal.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                && value <= 0m)
            {
                return "免费";
            }

            if (rawPrice.Trim().Equals("free", StringComparison.OrdinalIgnoreCase))
            {
                return "免费";
            }

            return rawPrice.Trim();
        }

        private static string NormalizeCountryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "cn";
            }

            string normalized = code.Trim().ToLowerInvariant();
            return KeychainConfig.IsValidCountryCode(normalized) ? normalized : "cn";
        }

        private void SortHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button header)
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
            ApplyFilterAndRefresh(preserveScrollPosition: true);
        }

        private List<SearchResult> ApplySorting(List<SearchResult> input)
        {
            if (string.IsNullOrWhiteSpace(_sortKey) || _sortDirection == SortDirection.None)
            {
                return input;
            }

            Func<SearchResult, object?> keySelector = _sortKey switch
            {
                "name" => item => item.name ?? string.Empty,
                "id" => item => item.id ?? string.Empty,
                "developer" => item => item.developer ?? string.Empty,
                "version" => item => item.version ?? string.Empty,
                "price" => item => GetPriceSortValue(item.price),
                "purchased" => item => item.purchased ?? string.Empty,
                _ => item => item.name ?? string.Empty
            };

            IOrderedEnumerable<SearchResult> ordered = _sortDirection == SortDirection.Ascending
                ? input.OrderBy(keySelector)
                : input.OrderByDescending(keySelector);

            return ordered.ToList();
        }

        private static decimal GetPriceSortValue(string? price)
        {
            if (string.IsNullOrWhiteSpace(price))
            {
                return decimal.MaxValue;
            }

            if (price.Trim().Equals("免费", StringComparison.OrdinalIgnoreCase)
                || price.Trim().Equals("free", StringComparison.OrdinalIgnoreCase))
            {
                return 0m;
            }

            return decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                ? value
                : decimal.MaxValue - 1;
        }

        private void UpdateSortHeaderTexts()
        {
            SetHeaderText(NameHeaderText, NameHeaderBase, "name");
            SetHeaderText(IdHeaderText, IdHeaderBase, "id");
            SetHeaderText(DeveloperHeaderText, DeveloperHeaderBase, "developer");
            SetHeaderText(VersionHeaderText, VersionHeaderBase, "version");
            SetHeaderText(PriceHeaderText, PriceHeaderBase, "price");
            SetHeaderText(PurchasedHeaderText, PurchasedHeaderBase, "purchased");
        }

        private void SetHeaderText(Button? button, string baseText, string key)
        {
            if (button == null)
            {
                return;
            }

            if (!string.Equals(_sortKey, key, StringComparison.Ordinal) || _sortDirection == SortDirection.None)
            {
                button.Content = baseText;
                return;
            }

            string suffix = _sortDirection == SortDirection.Ascending ? " ↑" : " ↓";
            button.Content = baseText + suffix;
        }

        private void CopyHomeLog_Click(object sender, RoutedEventArgs e)
        {
            string text = _homeLogBuilder.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendHomeLog("日志为空，无可复制内容。");
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendHomeLog("日志已复制到剪贴板。");
        }

        private void ClearHomeLog_Click(object sender, RoutedEventArgs e)
        {
            _homeLogBuilder.Clear();
            _homeLogEntries.Clear();
            HomeLogTextBlock?.Blocks.Clear();
            AppendHomeLog("日志已清空。");
        }

        private void AppendHomeLog(string message, UiLogSource source = UiLogSource.App)
        {
            if (string.IsNullOrWhiteSpace(message) || HomeLogTextBlock == null)
            {
                return;
            }

            UiLogEntry entry = UiLogFormatter.Build(message, source);
            _homeLogEntries.Add(entry);
            if (_homeLogEntries.Count > MaxLogLines)
            {
                _homeLogEntries.RemoveAt(0);
            }

            RebuildHomeLogView();
            ScrollLogToBottom(HomeLogScrollViewer);
            DispatcherQueue.TryEnqueue(() => ScrollLogToBottom(HomeLogScrollViewer));
        }

        private void SetPurchaseLoading(bool isLoading)
        {
            if (PurchaseLoadingBar == null)
            {
                return;
            }

            PurchaseLoadingBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResultList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer?.ContentTemplateRoot is not Grid rowGrid)
            {
                return;
            }

            bool isOddRow = args.ItemIndex % 2 == 1;
            if (!isOddRow)
            {
                rowGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00));
                ApplyPurchasedStatusColor(rowGrid, args.Item as SearchResult);
                ApplyPriceColor(rowGrid, args.Item as SearchResult);
                return;
            }

            // Use neutral gray striping and keep it independent from pointer visual states.
            var stripeColor = ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(0x1A, 0x80, 0x80, 0x80)
                : Windows.UI.Color.FromArgb(0x14, 0x70, 0x70, 0x70);

            rowGrid.Background = new SolidColorBrush(stripeColor);
            ApplyPurchasedStatusColor(rowGrid, args.Item as SearchResult);
            ApplyPriceColor(rowGrid, args.Item as SearchResult);
        }

        private void ApplyPurchasedStatusColor(Grid rowGrid, SearchResult? item)
        {
            if (rowGrid.FindName("PurchasedTextBlock") is not TextBlock statusTextBlock)
            {
                return;
            }

            if (item != null && (IsPurchasedStatus(item.purchased) || IsOwnedStatus(item.purchased)))
            {
                var greenColor = ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x8D, 0xE6, 0x9A)
                    : Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43);
                statusTextBlock.Foreground = new SolidColorBrush(greenColor);
                return;
            }

            statusTextBlock.ClearValue(TextBlock.ForegroundProperty);
        }

        private void ApplyPriceColor(Grid rowGrid, SearchResult? item)
        {
            if (rowGrid.FindName("PriceTextBlock") is not TextBlock priceTextBlock)
            {
                return;
            }

            if (item != null && !IsPriceFree(item.price))
            {
                var redColor = ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x99, 0x99)
                    : Windows.UI.Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C);
                priceTextBlock.Foreground = new SolidColorBrush(redColor);
                return;
            }

            priceTextBlock.ClearValue(TextBlock.ForegroundProperty);
        }

        private static void ScrollLogToBottom(ScrollViewer scrollViewer)
        {
            scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: true);
        }

        private void RebuildHomeLogView()
        {
            _homeLogBuilder.Clear();
            if (HomeLogTextBlock == null)
            {
                return;
            }

            HomeLogTextBlock.Blocks.Clear();
            var paragraph = new Paragraph();
            foreach (UiLogEntry entry in _homeLogEntries)
            {
                _homeLogBuilder.AppendLine(entry.FormattedText);
                var run = new Run
                {
                    Text = entry.FormattedText,
                    Foreground = new SolidColorBrush(GetLogColor(entry.Level))
                };
                paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new LineBreak());
            }

            HomeLogTextBlock.Blocks.Add(paragraph);
        }

        private Windows.UI.Color GetLogColor(UiLogLevel level)
        {
            return level switch
            {
                UiLogLevel.Tip => ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xD5, 0x8A)
                    : Windows.UI.Color.FromArgb(0xFF, 0x9A, 0x67, 0x00),
                UiLogLevel.Success => ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x8D, 0xE6, 0x9A)
                    : Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43),
                UiLogLevel.Error => ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x99, 0x99)
                    : Windows.UI.Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C),
                UiLogLevel.Ipatool => ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xC8, 0xFF)
                    : Windows.UI.Color.FromArgb(0xFF, 0x00, 0x55, 0xAA),
                _ => ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8)
                    : Windows.UI.Color.FromArgb(0xFF, 0x44, 0x44, 0x44)
            };
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
            IpatoolExecution.CommandExecuting -= OnIpatoolCommandExecuting;
            IpatoolExecution.CommandOutputReceived -= OnIpatoolCommandOutputReceived;
            if (!_pageCts.IsCancellationRequested)
            {
                _pageCts.Cancel();
            }

            _pageCts.Dispose();
            _pageCts = new CancellationTokenSource();
        }

        private void ClearSearchCache()
        {
            _allResults.Clear();
            if (ResultList != null)
            {
                ResultList.ItemsSource = null;
            }
        }

        private static bool IsStdqOwnedCandidate(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            return response.Contains("failed to purchase item with param 'STDQ'", StringComparison.OrdinalIgnoreCase);
        }

        private void OnIpatoolCommandExecuting(string command)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendHomeLog(command, UiLogSource.Ipatool);
            });
        }

        private void OnIpatoolCommandOutputReceived(string line)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendHomeLog(line, UiLogSource.Ipatool);
            });
        }

        private enum SortDirection
        {
            None = 0,
            Ascending = 1,
            Descending = 2
        }
    }
}
