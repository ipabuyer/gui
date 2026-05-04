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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace IPAbuyer.Views
{
    public sealed partial class MainPage : Page
    {
        private static readonly ResourceLoader Loader = new();
        private readonly List<SearchResult> _allResults = new();
        private SearchResult[] _visibleResults = Array.Empty<SearchResult>();
        private string[] _visibleResultSnapshots = Array.Empty<string>();
        private bool[] _visibleResultChanged = Array.Empty<bool>();
        private readonly DownloadQueueService _downloadQueueService = DownloadQueueService.Instance;
        private readonly StringBuilder _homeLogBuilder = new();
        private readonly List<UiLogEntry> _homeLogEntries = new();
        private CancellationTokenSource _pageCts = new();
        private bool _isHomeLogDialogOpen;
        private string _selectedFilter = "All";
        private static readonly string StatusPurchased = L("Common/Status/Purchased");
        private static readonly string StatusOwned = L("Common/Status/Owned");
        private static readonly string StatusCanPurchase = L("Common/Status/NotPurchased");
        private static readonly string StatusPurchaseBlocked = L("Common/Status/PurchaseBlocked");

        private static readonly string[] PurchasedAliases = { StatusPurchased };
        private static readonly string[] OwnedAliases = { StatusOwned };
        private static readonly string[] CanPurchaseAliases = { L("Common/Status/CanPurchase"), StatusCanPurchase };
        private const int MaxLogLines = 1000;

        public int SearchLimitNum { get; set; } = 200;

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
            _downloadQueueService.LogReceived -= OnDownloadQueueLogReceived;
            _downloadQueueService.LogReceived += OnDownloadQueueLogReceived;
            _downloadQueueService.QueueChanged -= OnDownloadQueueChanged;
            _downloadQueueService.QueueChanged += OnDownloadQueueChanged;
            UpdateDownloadActionState();

            if (MainPageCacheState.ConsumeSearchCacheInvalidation())
            {
                ClearSearchCache();
            }
        }

        public async void PerformSearchFromMainWindow(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                AppendHomeLog(L("MainPage/Log/SearchEmptyIgnored"), UiLogLevel.Tip);
                return;
            }

            AppendHomeLog(LF("MainPage/Log/SearchStarted", appName.Trim()), UiLogLevel.Info);
            SetTableLoading(true);
            try
            {
                await PerformSearchAsync(appName.Trim(), _pageCts.Token);
            }
            catch (Exception ex)
            {
                AppendHomeLog(LF("MainPage/Log/SearchException", ex.Message), UiLogLevel.Error);
                AppendHomeLog(ex.ToString(), UiLogLevel.Error, UiLogSource.Auto);
            }
            finally
            {
                SetTableLoading(false);
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
                    SetResultListItemsSource(null);
                }

                AppendHomeLog(L("MainPage/Log/SearchTimeoutOrEmpty"), UiLogLevel.Error);
                return;
            }

            ParseSearchResponse(result.OutputOrError, account);
        }

        private void ParseSearchResponse(string payload, string account)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("results", out JsonElement resultsElement)
                    || resultsElement.ValueKind != JsonValueKind.Array)
                {
                    if (ResultList != null)
                    {
                        SetResultListItemsSource(null);
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
                foreach (JsonElement appElement in resultsElement.EnumerateArray())
                {
                    string bundleId = GetBundleId(appElement) ?? string.Empty;
                    string price = NormalizePriceForDisplay(GetPriceValue(appElement));
                    string status = ResolveSearchStatus(bundleId, price, purchasedDict);

                    _allResults.Add(new SearchResult
                    {
                        bundleId = bundleId,
                        id = GetPropertyValue(appElement, "trackId"),
                        name = GetPropertyValue(appElement, "trackName"),
                        developer = GetPropertyValue(appElement, "sellerName"),
                        artworkUrl = GetPropertyValue(appElement, "artworkUrl100"),
                        price = price,
                        version = GetPropertyValue(appElement, "version"),
                        purchased = status
                    });
                }

                ApplyFilterAndRefresh();
                AppendHomeLog(LF("MainPage/Log/SearchCompleted", _allResults.Count), UiLogLevel.Success);
            }
            catch (JsonException)
            {
                if (ResultList != null)
                {
                    SetResultListItemsSource(null);
                }

                AppendHomeLog(L("MainPage/Log/SearchParseFailed"), UiLogLevel.Error);
            }
        }

        private async void AppActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SearchResult app })
            {
                return;
            }

            if (IsPurchasedStatus(app.purchased) || IsOwnedStatus(app.purchased))
            {
                await AddSingleAppToDownloadQueueAsync(app);
                return;
            }

            SetTableLoading(true);

            try
            {
                _ = await PurchaseAppsAsync(new List<SearchResult> { app });
            }
            catch (OperationCanceledException)
            {
                AppendHomeLog(L("MainPage/Log/ContextPurchaseCanceled"), UiLogLevel.Tip);
            }
            catch (Exception ex)
            {
                AppendHomeLog(LF("MainPage/Log/ContextPurchaseException", ex.Message), UiLogLevel.Error);
            }
            finally
            {
                SetTableLoading(false);
                ApplyFilterAndRefresh();
            }
        }

        private async Task AddSingleAppToDownloadQueueAsync(SearchResult app)
        {
            int added = 0;
            int updated = 0;
            int ignored = 0;
            CountDownloadQueueAddResult(_downloadQueueService.AddOrUpdateFromSearchResult(app), ref added, ref updated, ref ignored);

            if (added == 0 && updated == 0)
            {
                AppendHomeLog(ignored > 0 ? L("MainPage/DownloadQueue/AddContextIgnored") : L("MainPage/DownloadQueue/AddContextEmpty"), UiLogLevel.Tip);
                return;
            }

            AppendHomeLog(BuildDownloadQueueAddSummary(added, updated), UiLogLevel.Success);
            await StartDownloadQueueFromMainAsync();
        }

        private async Task StartDownloadQueueFromMainAsync()
        {
            if (_downloadQueueService.IsRunning)
            {
                AppendHomeLog(L("MainPage/DownloadQueue/AlreadyRunningContinue"), UiLogLevel.Info);
                UpdateDownloadActionState();
                return;
            }

            try
            {
                UpdateDownloadActionState();
                _ = await _downloadQueueService.StartQueueAsync();
            }
            catch (Exception ex)
            {
                AppendHomeLog(LF("MainPage/DownloadQueue/StartFailed", ex.Message), UiLogLevel.Error);
            }
            finally
            {
                UpdateDownloadActionState();
            }
        }

        private static void CountDownloadQueueAddResult(DownloadQueueAddResult result, ref int added, ref int updated, ref int ignored)
        {
            switch (result)
            {
                case DownloadQueueAddResult.Added:
                    added++;
                    break;
                case DownloadQueueAddResult.Updated:
                case DownloadQueueAddResult.Requeued:
                    updated++;
                    break;
                default:
                    ignored++;
                    break;
            }
        }

        private static string BuildDownloadQueueAddSummary(int added, int updated)
        {
            if (added > 0 && updated > 0)
            {
                return LF("MainPage/DownloadQueue/AddSummaryAddedAndUpdated", added, updated);
            }

            if (added > 0)
            {
                return LF("MainPage/DownloadQueue/AddSummaryAdded", added);
            }

            return LF("MainPage/DownloadQueue/AddSummaryUpdated", updated);
        }

        private void CancelAllDownloadsButton_Click(object sender, RoutedEventArgs e)
        {
            _downloadQueueService.CancelAll();
            UpdateDownloadActionState();
        }

        private void OnDownloadQueueChanged()
        {
            DispatcherQueue.TryEnqueue(UpdateDownloadActionState);
        }

        private void OnDownloadQueueLogReceived(UiLogMessage log)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendHomeLog(log.Message, log.Level, log.Source);
            });
        }

        private void UpdateDownloadActionState()
        {
            if (CancelAllDownloadsButton == null)
            {
                return;
            }

            bool isRunning = _downloadQueueService.IsRunning;
            CancelAllDownloadsButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            CancelAllDownloadsButton.IsEnabled = isRunning;
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
            if (contextItem == null)
            {
                return new List<SearchResult>();
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

                if (status == StatusCanPurchase)
                {
                    app.purchased = ResolveUnpurchasedStatusForPrice(app.price);
                    if (!string.IsNullOrWhiteSpace(account))
                    {
                        PurchasedAppDb.RemovePurchasedApp(app.bundleId, account);
                    }
                }
                else
                {
                    app.purchased = status;
                    if (!string.IsNullOrWhiteSpace(account))
                    {
                        PurchasedAppDb.SavePurchasedApp(app.bundleId, account, status);
                    }
                }
            }

            ApplyFilterAndRefresh();
            AppendHomeLog(LF("MainPage/Log/MarkedStatus", selectedApps.Count, status), UiLogLevel.Success);
        }

        private void ContextMenuCopyName_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.name ?? string.Empty, L("MainPage/Field/Name"));
        }

        private void ContextMenuCopyVersion_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.version ?? string.Empty, L("MainPage/Field/Version"));
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
                AppendHomeLog(LF("MainPage/Log/CopyFieldEmpty", fieldName), UiLogLevel.Tip);
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(value);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendHomeLog(LF("MainPage/Log/CopyFieldSuccess", fieldName, selectedApps.Count), UiLogLevel.Success);
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton selected)
            {
                return;
            }

            _selectedFilter = selected.Tag?.ToString() ?? "All";
            UpdateFilterButtonState();
            ApplyFilterAndRefresh();
            AppendHomeLog(LF("MainPage/Log/FilterChanged", _selectedFilter), UiLogLevel.Info);
        }

        private void UpdateFilterButtonState()
        {
            SetFilterButtonState(AllFilterButton, "All");
            SetFilterButtonState(OnlyNotPurchasedFilterButton, "OnlyNotPurchased");
            SetFilterButtonState(OnlyPurchasedFilterButton, "OnlyPurchased");
            SetFilterButtonState(OnlyHadFilterButton, "OnlyHad");
        }

        private void SetFilterButtonState(ToggleButton? button, string filter)
        {
            if (button != null)
            {
                button.IsChecked = string.Equals(_selectedFilter, filter, StringComparison.Ordinal);
            }
        }

        private void ApplyFilterAndRefresh()
        {
            if (ResultList == null)
            {
                return;
            }

            var filtered = GetFilteredResults();
            SetResultListItemsSource(filtered);
        }

        private void SetResultListItemsSource(List<SearchResult>? results)
        {
            if (ResultList == null)
            {
                return;
            }

            if (ResultList.ItemsSource != null)
            {
                ResultList.ItemsSource = null;
            }
            double? verticalOffset = GetResultListVerticalOffset();
            UpdateVisibleResults(results);
            SyncResultListItems();
            RestoreResultListVerticalOffset(verticalOffset);

            UpdateEmptySearchHintVisibility();
        }

        private void UpdateVisibleResults(IReadOnlyList<SearchResult>? results)
        {
            if (results == null || results.Count == 0)
            {
                _visibleResults = Array.Empty<SearchResult>();
                _visibleResultSnapshots = Array.Empty<string>();
                _visibleResultChanged = Array.Empty<bool>();
                return;
            }

            string[] snapshots = results.Select(CreateSearchResultSnapshot).ToArray();
            bool[] changed = new bool[results.Count];
            if (_visibleResults.Length == results.Count && _visibleResultSnapshots.Length == snapshots.Length)
            {
                bool sameItems = true;
                for (int i = 0; i < results.Count; i++)
                {
                    if (!ReferenceEquals(_visibleResults[i], results[i])
                        || !string.Equals(_visibleResultSnapshots[i], snapshots[i], StringComparison.Ordinal))
                    {
                        changed[i] = true;
                        sameItems = false;
                    }
                }

                if (sameItems)
                {
                    _visibleResultChanged = Array.Empty<bool>();
                    return;
                }
            }
            else
            {
                Array.Fill(changed, true);
            }

            _visibleResults = results.ToArray();
            _visibleResultSnapshots = snapshots;
            _visibleResultChanged = changed;
        }

        private void SyncResultListItems()
        {
            if (ResultList == null)
            {
                return;
            }

            for (int i = ResultList.Items.Count - 1; i >= _visibleResults.Length; i--)
            {
                ResultList.Items.RemoveAt(i);
            }

            for (int i = 0; i < _visibleResults.Length; i++)
            {
                SearchResult result = _visibleResults[i];
                if (i >= ResultList.Items.Count)
                {
                    ResultList.Items.Add(result);
                    continue;
                }

                bool itemChanged = i < _visibleResultChanged.Length && _visibleResultChanged[i];
                if (ReferenceEquals(ResultList.Items[i], result) && !itemChanged)
                {
                    continue;
                }

                ResultList.Items.RemoveAt(i);
                ResultList.Items.Insert(i, result);
            }
        }

        private static string CreateSearchResultSnapshot(SearchResult result)
        {
            return string.Join(
                "\u001F",
                result.bundleId,
                result.id,
                result.name,
                result.developer,
                result.artworkUrl,
                result.price,
                result.version,
                result.purchased);
        }

        private double? GetResultListVerticalOffset()
        {
            ScrollViewer? scrollViewer = FindDescendantScrollViewer(ResultList);
            return scrollViewer?.VerticalOffset;
        }

        private void RestoreResultListVerticalOffset(double? verticalOffset)
        {
            if (verticalOffset == null || ResultList == null)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                ScrollViewer? scrollViewer = FindDescendantScrollViewer(ResultList);
                scrollViewer?.ChangeView(null, verticalOffset.Value, null, disableAnimation: true);
            });
        }

        private static ScrollViewer? FindDescendantScrollViewer(DependencyObject? root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                ScrollViewer? childScrollViewer = FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (childScrollViewer != null)
                {
                    return childScrollViewer;
                }
            }

            return null;
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

            return filtered;
        }

        private async Task<bool> PurchaseAppsAsync(List<SearchResult> selectedApps)
        {
            string account = GetActiveAccount();
            if (string.IsNullOrWhiteSpace(account))
            {
                string message = SessionState.IsLoggedIn
                    ? L("MainPage/Purchase/MissingSessionEmail")
                    : L("MainPage/Purchase/LoginRequired");
                AppendHomeLog(message, UiLogLevel.Error);
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
                    AppendHomeLog(LF("MainPage/Purchase/SkipNonFree", app.name ?? app.bundleId), UiLogLevel.Tip);
                    continue;
                }

                if (isTestAccount)
                {
                    app.purchased = StatusPurchased;
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusPurchased);
                    AppendHomeLog(LF("MainPage/Purchase/MockSuccess", app.name ?? app.bundleId), UiLogLevel.Success);
                    continue;
                }

                var result = await IpatoolExecution.PurchaseAppAsync(app.bundleId, account, _pageCts.Token);
                if (IsPurchaseAlreadyOwned(result.OutputOrError))
                {
                    app.purchased = StatusOwned;
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusOwned);
                    AppendHomeLog(LF("MainPage/Purchase/OwnedDetected", app.name ?? app.bundleId), UiLogLevel.Success);
                }
                else if (IsPurchaseSuccess(result.OutputOrError))
                {
                    app.purchased = StatusPurchased;
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusPurchased);
                    AppendHomeLog(LF("MainPage/Purchase/Success", app.name ?? app.bundleId), UiLogLevel.Success);
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
                            AppendHomeLog(LF("MainPage/Purchase/OwnedMarked", app.name ?? app.bundleId), UiLogLevel.Success);
                        }
                        else
                        {
                            AppendHomeLog(LF("MainPage/Purchase/OwnedNotMarked", app.name ?? app.bundleId), UiLogLevel.Info);
                        }
                    }
                    else
                    {
                        string reason = string.IsNullOrWhiteSpace(result.OutputOrError)
                            ? L("MainPage/Purchase/UnknownError")
                            : result.OutputOrError;
                        AppendHomeLog(LF("MainPage/Purchase/Failed", app.name ?? app.bundleId, reason), UiLogLevel.Error);
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
                Content = L("MainPage/OwnedPrompt/DisablePrompt")
            };

            var contentPanel = new StackPanel { Spacing = 8 };
            contentPanel.Children.Add(new TextBlock
            {
                Text = LF("MainPage/OwnedPrompt/Message", app.name ?? app.bundleId ?? string.Empty),
                TextWrapping = TextWrapping.Wrap
            });
            contentPanel.Children.Add(disablePromptCheckBox);

            var dialog = new ContentDialog
            {
                Title = L("MainPage/OwnedPrompt/Title"),
                Content = contentPanel,
                PrimaryButtonText = L("MainPage/OwnedPrompt/PrimaryButton"),
                CloseButtonText = L("Common/Cancel"),
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

        private static string ResolveSearchStatus(string bundleId, string price, IReadOnlyDictionary<string, string> purchasedDict)
        {
            if (!string.IsNullOrWhiteSpace(bundleId) && purchasedDict.TryGetValue(bundleId, out string? purchasedStatus))
            {
                return NormalizePurchasedStatus(purchasedStatus);
            }

            return string.IsNullOrWhiteSpace(price) || IsPriceFree(price)
                ? StatusCanPurchase
                : StatusPurchaseBlocked;
        }

        private static string ResolveUnpurchasedStatusForPrice(string? price)
        {
            return string.IsNullOrWhiteSpace(price) || IsPriceFree(price)
                ? StatusCanPurchase
                : StatusPurchaseBlocked;
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

            return IsPurchaseBlockedStatus(status)
                || CanPurchaseAliases.Any(alias => string.Equals(status, alias, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPurchaseBlockedStatus(string? status)
        {
            return !string.IsNullOrWhiteSpace(status)
                && status.Trim().Equals(StatusPurchaseBlocked, StringComparison.OrdinalIgnoreCase);
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

            foreach (var token in JsonPayload.EnumerateTokens(response))
            {
                if (JsonPayload.TryReadBoolean(token, "success", out bool success))
                {
                    return success;
                }
            }

            return false;
        }

        private static bool IsPurchaseAlreadyOwned(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            if (response.Contains("\"alreadyOwned\":true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var token in JsonPayload.EnumerateTokens(response))
            {
                if (JsonPayload.TryReadBoolean(token, "alreadyOwned", out bool alreadyOwned) && alreadyOwned)
                {
                    return true;
                }
            }

            return false;
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
                || normalized.Equals(L("Common/Price/Free"), StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetBundleId(JsonElement element)
        {
            string? bundleId = GetPropertyValue(element, "bundleID");
            if (!string.IsNullOrWhiteSpace(bundleId))
            {
                return bundleId;
            }

            return GetPropertyValue(element, "bundleId");
        }

        private static string? GetPropertyValue(JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out JsonElement property)
                ? ReadJsonScalarAsString(property)
                : null;
        }

        private static string? GetPriceValue(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("price", out JsonElement priceElement))
            {
                return null;
            }

            if (priceElement.ValueKind == JsonValueKind.Number
                && priceElement.TryGetDecimal(out decimal priceValue))
            {
                return priceValue.ToString("0.00", CultureInfo.InvariantCulture);
            }

            return ReadJsonScalarAsString(priceElement);
        }

        private static string? ReadJsonScalarAsString(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetDecimal(out decimal value) => value.ToString(CultureInfo.InvariantCulture),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => element.GetRawText()
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
                return L("Common/Price/Free");
            }

            if (rawPrice.Trim().Equals("free", StringComparison.OrdinalIgnoreCase))
            {
                return L("Common/Price/Free");
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

        private void CopyHomeLog_Click(object sender, RoutedEventArgs e)
        {
            CopyHomeLog();
        }

        private void CopyHomeLog()
        {
            string text = _homeLogBuilder.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendHomeLog(L("MainPage/Log/CopyEmptyLog"), UiLogLevel.Tip);
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendHomeLog(L("MainPage/Log/CopiedToClipboard"), UiLogLevel.Success);
        }

        private void ClearHomeLog_Click(object sender, RoutedEventArgs e)
        {
            ClearHomeLog();
        }

        private void ClearHomeLog()
        {
            _homeLogBuilder.Clear();
            _homeLogEntries.Clear();
            AppendHomeLog(L("MainPage/Log/Cleared"), UiLogLevel.Info);
        }

        private async void ShowHomeLogDialog_Click(object sender, RoutedEventArgs e)
        {
            await TryShowHomeLogDialogAsync();
        }

        private async Task TryShowHomeLogDialogAsync()
        {
            if (_isHomeLogDialogOpen || XamlRoot == null)
            {
                return;
            }

            _isHomeLogDialogOpen = true;
            try
            {
            var dialog = new LogViewerDialog(
                _homeLogEntries,
                GetLogColor,
                CopyHomeLog,
                ClearHomeLog,
                XamlRoot);

            await dialog.ShowAsync();
            }
            finally
            {
                _isHomeLogDialogOpen = false;
            }
        }

        private void AppendHomeLog(string message, UiLogLevel level, UiLogSource source = UiLogSource.App)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            UiLogEntry entry = UiLogFormatter.Build(message, level);
            _homeLogEntries.Add(entry);
            if (_homeLogEntries.Count > MaxLogLines)
            {
                _homeLogEntries.RemoveAt(0);
            }

            RebuildHomeLogView();
            EnsureHomeLogScrollToBottom();
            if (source == UiLogSource.App)
            {
                ShowHomeStatus(message, entry.Level);
            }
        }

        private void ShowHomeStatus(string message, UiLogLevel level)
        {
            if (HomeStatusInfoBar == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            HomeStatusInfoBar.Message = message.Trim();
            HomeStatusInfoBar.Severity = ToInfoBarSeverity(level);
            HomeStatusInfoBar.IsOpen = true;
        }

        private static InfoBarSeverity ToInfoBarSeverity(UiLogLevel level)
        {
            return level switch
            {
                UiLogLevel.Error => InfoBarSeverity.Error,
                UiLogLevel.Success => InfoBarSeverity.Success,
                UiLogLevel.Tip => InfoBarSeverity.Warning,
                _ => InfoBarSeverity.Informational
            };
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }

        private void SetTableLoading(bool isLoading)
        {
            if (TableLoadingRing == null)
            {
                return;
            }

            TableLoadingRing.IsActive = isLoading;
            TableLoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            UpdateEmptySearchHintVisibility(isLoading);
        }

        private void UpdateEmptySearchHintVisibility(bool? isLoading = null)
        {
            if (EmptySearchHintTextBlock == null)
            {
                return;
            }

            bool loading = isLoading ?? TableLoadingRing?.IsActive == true;
            EmptySearchHintTextBlock.Visibility = !loading && _visibleResults.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void EnsureHomeLogScrollToBottom()
        {
            // Popup log mode does not need in-page auto scrolling.
        }

        private void RebuildHomeLogView()
        {
            _homeLogBuilder.Clear();
            foreach (UiLogEntry entry in _homeLogEntries)
            {
                _homeLogBuilder.AppendLine(entry.FormattedText);
            }
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
                _ => Windows.UI.Color.FromArgb(0xFF, 0xE6, 0xE6, 0xE6)
            };
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            IpatoolExecution.CommandExecuting -= OnIpatoolCommandExecuting;
            IpatoolExecution.CommandOutputReceived -= OnIpatoolCommandOutputReceived;
            _downloadQueueService.LogReceived -= OnDownloadQueueLogReceived;
            _downloadQueueService.QueueChanged -= OnDownloadQueueChanged;
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
                SetResultListItemsSource(null);
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
                AppendHomeLog(command, UiLogLevel.Ipatool, UiLogSource.Ipatool);
            });
        }

        private void OnIpatoolCommandOutputReceived(string line)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendHomeLog(line, UiLogLevel.Ipatool, UiLogSource.Ipatool);
            });
        }

    }

    public sealed class MainPageFreePriceVisibilityConverter : IValueConverter
    {
        private static readonly ResourceLoader Loader = new();

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            return IsFreePrice(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }

        internal static bool IsFreePrice(string? price)
        {
            if (string.IsNullOrWhiteSpace(price))
            {
                return true;
            }

            string freeText = Loader.GetString("Common/Price/Free");
            return price.Trim().Equals(freeText, StringComparison.OrdinalIgnoreCase)
                || price.Trim().Equals("free", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class MainPageNonFreePriceVisibilityConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            return MainPageFreePriceVisibilityConverter.IsFreePrice(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class MainPagePurchasedForegroundConverter : IValueConverter
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly string PurchasedText = Loader.GetString("Common/Status/Purchased");
        private static readonly string OwnedText = Loader.GetString("Common/Status/Owned");
        private static readonly string PurchaseBlockedText = Loader.GetString("Common/Status/PurchaseBlocked");

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            string? status = value as string;
            if (string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            if (status.Trim().Equals(PurchasedText, StringComparison.OrdinalIgnoreCase)
                || status.Trim().Equals(OwnedText, StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43));
            }

            if (status.Trim().Equals(PurchaseBlockedText, StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class MainPageImageUriConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            return value is string uri && Uri.TryCreate(uri, UriKind.Absolute, out Uri? result)
                ? result
                : null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class MainPageAppActionTextConverter : IValueConverter
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly string PurchasedText = Loader.GetString("Common/Status/Purchased");
        private static readonly string OwnedText = Loader.GetString("Common/Status/Owned");
        private static readonly string PurchaseBlockedText = Loader.GetString("Common/Status/PurchaseBlocked");

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? status = value as string;
            if (IsStatus(status, PurchaseBlockedText))
            {
                return PurchaseBlockedText;
            }

            if (IsStatus(status, PurchasedText) || IsStatus(status, OwnedText))
            {
                return Loader.GetString("MainPage/Context/AddToQueueItem/Text");
            }

            return Loader.GetString("MainPage/Context/PurchaseItem/Text");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }

        private static bool IsStatus(string? status, string expected)
        {
            return !string.IsNullOrWhiteSpace(status)
                && status.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class MainPageAppActionEnabledConverter : IValueConverter
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly string PurchaseBlockedText = Loader.GetString("Common/Status/PurchaseBlocked");

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? status = value as string;
            return string.IsNullOrWhiteSpace(status)
                || !status.Trim().Equals(PurchaseBlockedText, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
