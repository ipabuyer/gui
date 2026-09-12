using IPAbuyer.Core.Configuration;
using IPAbuyer.Core.Integration.Ipatool;
using IPAbuyer.Core.Logging;
using IPAbuyer.Core.Models;
using IPAbuyer.Core.Services.AppCatalog;
using IPAbuyer.Core.Services.Downloads;
using IPAbuyer.Core.Services.Purchases;
using IPAbuyer.Core.State;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Pages
{
    public sealed partial class MainPage : Page
    {
        private static readonly ResourceLoader Loader = new();
        private readonly List<SearchResult> _allResults = new();
        private SearchResult[] _visibleResults = Array.Empty<SearchResult>();
        private string[] _visibleResultSnapshots = Array.Empty<string>();
        private readonly DownloadQueueService _downloadQueueService = DownloadQueueService.Instance;
        private CancellationTokenSource _pageCts = new();
        private bool _isInactive;
        private bool _hasCompletedSearch;
        private bool _isUpdatingDeveloperFilter;
        private string _selectedFilter = "All";
        private string? _selectedDeveloper;
        private static readonly string StatusPurchased = PurchaseStatusPolicy.PurchasedStatus;
        private static readonly string StatusOwned = PurchaseStatusPolicy.OwnedStatus;
        private static readonly string StatusCanPurchase = PurchaseStatusPolicy.CanPurchaseStatus;

        public int SearchLimitNum { get; set; } = 200;

        public MainPage()
        {
            InitializeComponent();
            UpdateDeveloperFilterOptions();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isInactive = false;
            IpatoolClient.CommandExecuting -= OnIpatoolCommandExecuting;
            IpatoolClient.CommandExecuting += OnIpatoolCommandExecuting;
            IpatoolClient.CommandOutputReceived -= OnIpatoolCommandOutputReceived;
            IpatoolClient.CommandOutputReceived += OnIpatoolCommandOutputReceived;
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
            IReadOnlyList<SearchResult>? results;
            try
            {
                results = await AppCatalogService.SearchAsync(appName, SearchLimitNum, account, cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                if (ResultList != null)
                {
                    SetResultListItemsSource(null);
                }

                AppendHomeLog(L("MainPage/Log/SearchParseFailed"), UiLogLevel.Error);
                return;
            }

            if (results == null)
            {
                if (ResultList != null)
                {
                    SetResultListItemsSource(null);
                }

                AppendHomeLog(L("MainPage/Log/SearchTimeoutOrEmpty"), UiLogLevel.Error);
                return;
            }

            _allResults.Clear();
            _allResults.AddRange(results);
            _hasCompletedSearch = true;
            UpdateDeveloperFilterOptions();
            ApplyFilterAndRefresh();
            AppendHomeLog(LF("MainPage/Log/SearchCompleted", _allResults.Count), UiLogLevel.Success);
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
            QueueUi(UpdateDownloadActionState);
        }

        private void OnDownloadQueueLogReceived(UiLogMessage log)
        {
            QueueUi(() => AppendHomeLog(log.Message, log.Level, log.Source));
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
            DownloadActivityRing.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            DownloadActivityRing.IsActive = isRunning;
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
                string bundleId = app.bundleId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(bundleId))
                {
                    continue;
                }

                if (status == StatusCanPurchase)
                {
                    ReplaceSearchResultStatus(app, ResolveUnpurchasedStatusForPrice(app.price));
                    if (!string.IsNullOrWhiteSpace(account))
                    {
                        PurchaseHistoryService.RemoveMark(bundleId, account);
                    }
                }
                else
                {
                    ReplaceSearchResultStatus(app, status);
                    if (!string.IsNullOrWhiteSpace(account))
                    {
                        PurchaseHistoryService.Mark(bundleId, account, status);
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

        private void ContextMenuCopyId_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.bundleId ?? string.Empty, L("MainPage/Field/Id"));
        }

        private async void ContextMenuOpenAppStore_Click(object sender, RoutedEventArgs e)
        {
            SearchResult? app = ResolveContextItem(sender);
            if (app == null)
            {
                return;
            }

            if (!long.TryParse(app.id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long trackId) || trackId <= 0)
            {
                AppendHomeLog(L("MainPage/Log/AppStoreMissingId"), UiLogLevel.Tip);
                return;
            }

            try
            {
                string countryCode = AppCatalogService.NormalizeCountryCode(ApplicationSettings.GetCountryCode());
                var appStoreUri = new Uri(FormattableString.Invariant($"https://apps.apple.com/{countryCode}/app/id{trackId}"));
                bool opened = await Windows.System.Launcher.LaunchUriAsync(appStoreUri);
                if (opened)
                {
                    AppendHomeLog(LF("MainPage/Log/AppStoreOpened", GetAppDisplayLabel(app, app.bundleId ?? app.id ?? string.Empty)), UiLogLevel.Success);
                    return;
                }

                AppendHomeLog(LF("MainPage/Log/AppStoreOpenFailed", appStoreUri), UiLogLevel.Error);
            }
            catch (Exception ex)
            {
                AppendHomeLog(LF("MainPage/Log/AppStoreOpenFailed", ex.Message), UiLogLevel.Error);
            }
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

        private void DeveloperFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingDeveloperFilter || DeveloperFilterComboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            _selectedDeveloper = item.Tag is string developer && !string.Equals(developer, "All", StringComparison.Ordinal)
                ? developer
                : null;
            ApplyFilterAndRefresh();
        }

        private void UpdateDeveloperFilterOptions()
        {
            if (DeveloperFilterComboBox == null)
            {
                return;
            }

            _isUpdatingDeveloperFilter = true;
            try
            {
                while (DeveloperFilterComboBox.Items.Count > 1)
                {
                    DeveloperFilterComboBox.Items.RemoveAt(1);
                }

                foreach (DeveloperFilterOption developer in DeveloperFilter.BuildOptions(
                    _allResults.Select(result => result.developer)))
                {
                    DeveloperFilterComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = developer.DisplayName,
                        Tag = developer.DisplayName
                    });
                }

                _selectedDeveloper = null;
                DeveloperFilterComboBox.SelectedIndex = 0;
            }
            finally
            {
                _isUpdatingDeveloperFilter = false;
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
                return;
            }

            string[] snapshots = results.Select(CreateSearchResultSnapshot).ToArray();
            if (_visibleResults.Length == results.Count && _visibleResultSnapshots.Length == snapshots.Length)
            {
                bool sameItems = true;
                for (int i = 0; i < results.Count; i++)
                {
                    if (!ReferenceEquals(_visibleResults[i], results[i])
                        || !string.Equals(_visibleResultSnapshots[i], snapshots[i], StringComparison.Ordinal))
                    {
                        sameItems = false;
                    }
                }

                if (sameItems)
                {
                    return;
                }
            }

            _visibleResults = results.ToArray();
            _visibleResultSnapshots = snapshots;
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

                if (ReferenceEquals(ResultList.Items[i], result))
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

        private SearchResult ReplaceSearchResultStatus(SearchResult app, string status)
        {
            var updated = new SearchResult
            {
                bundleId = app.bundleId,
                id = app.id,
                name = app.name,
                developer = app.developer,
                artworkUrl = app.artworkUrl,
                price = app.price,
                version = app.version,
                purchased = status
            };

            int index = _allResults.FindIndex(candidate => ReferenceEquals(candidate, app));
            if (index < 0 && !string.IsNullOrWhiteSpace(app.bundleId))
            {
                index = _allResults.FindIndex(candidate => string.Equals(candidate.bundleId, app.bundleId, StringComparison.OrdinalIgnoreCase));
            }

            if (index >= 0)
            {
                _allResults[index] = updated;
            }

            return updated;
        }

        private static string GetAppDisplayLabel(SearchResult app, string fallbackBundleId)
        {
            return string.IsNullOrWhiteSpace(app.name) ? fallbackBundleId : app.name;
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
            IEnumerable<SearchResult> filtered = _selectedFilter switch
            {
                "OnlyPurchased" => _allResults.Where(a => IsPurchasedStatus(a.purchased)),
                "OnlyNotPurchased" => _allResults.Where(a => IsCanPurchaseStatus(a.purchased)),
                "OnlyHad" => _allResults.Where(a => IsOwnedStatus(a.purchased)),
                _ => _allResults,
            };

            if (!string.IsNullOrWhiteSpace(_selectedDeveloper))
            {
                filtered = filtered.Where(app => DeveloperFilter.Matches(app.developer, _selectedDeveloper));
            }

            return filtered.ToList();
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

            foreach (var app in selectedApps)
            {
                string bundleId = app.bundleId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(bundleId))
                {
                    continue;
                }

                string appLabel = GetAppDisplayLabel(app, bundleId);
                PurchaseResult result = await PurchaseService.PurchaseAsync(app, account, _pageCts.Token);
                switch (result.Outcome)
                {
                    case PurchaseOutcome.Skipped when string.Equals(result.Detail, "NonFree", StringComparison.Ordinal):
                        AppendHomeLog(LF("MainPage/Purchase/SkipNonFree", appLabel), UiLogLevel.Tip);
                        break;

                    case PurchaseOutcome.Purchased:
                        {
                            SearchResult updatedApp = ReplaceSearchResultStatus(app, PurchaseStatusPolicy.PurchasedStatus);
                            string key = string.Equals(result.Detail, "Mock", StringComparison.Ordinal)
                                ? "MainPage/Purchase/MockSuccess"
                                : "MainPage/Purchase/Success";
                            AppendHomeLog(LF(key, GetAppDisplayLabel(updatedApp, bundleId)), UiLogLevel.Success);
                            break;
                        }

                    case PurchaseOutcome.AlreadyOwned:
                        {
                            SearchResult updatedApp = ReplaceSearchResultStatus(app, PurchaseStatusPolicy.OwnedStatus);
                            AppendHomeLog(LF("MainPage/Purchase/OwnedDetected", GetAppDisplayLabel(updatedApp, bundleId)), UiLogLevel.Success);
                            break;
                        }

                    case PurchaseOutcome.NeedsOwnedConfirmation:
                        if (await ConfirmMarkOwnedAsync(app).ConfigureAwait(true))
                        {
                            SearchResult updatedApp = ReplaceSearchResultStatus(app, PurchaseStatusPolicy.OwnedStatus);
                            PurchaseService.Mark(bundleId, account, PurchaseStatusPolicy.OwnedStatus);
                            AppendHomeLog(LF("MainPage/Purchase/OwnedMarked", GetAppDisplayLabel(updatedApp, bundleId)), UiLogLevel.Success);
                        }
                        else
                        {
                            AppendHomeLog(LF("MainPage/Purchase/OwnedNotMarked", appLabel), UiLogLevel.Info);
                        }
                        break;

                    case PurchaseOutcome.Failed:
                        string reason = string.IsNullOrWhiteSpace(result.Detail)
                            ? L("MainPage/Purchase/UnknownError")
                            : result.Detail;
                        AppendHomeLog(LF("MainPage/Purchase/Failed", appLabel, reason), UiLogLevel.Error);
                        break;
                }
            }
            return true;
        }

        private async Task<bool> ConfirmMarkOwnedAsync(SearchResult app)
        {
            if (ApplicationSettings.GetOwnedCheckEnabled())
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
                ApplicationSettings.SaveOwnedCheckEnabled(true);
            }

            return shouldMark;
        }

        private static string ResolveUnpurchasedStatusForPrice(string? price)
        {
            return PurchaseStatusPolicy.ResolveUnpurchasedStatus(price);
        }

        private static bool IsPurchasedStatus(string? status)
        {
            return PurchaseStatusPolicy.IsPurchased(status);
        }

        private static bool IsOwnedStatus(string? status)
        {
            return PurchaseStatusPolicy.IsOwned(status);
        }

        private static bool IsCanPurchaseStatus(string? status)
        {
            return PurchaseStatusPolicy.IsCanPurchase(status);
        }

        private static bool IsPurchaseBlockedStatus(string? status)
        {
            return PurchaseStatusPolicy.IsPurchaseBlocked(status);
        }

        private static string GetActiveAccount()
        {
            string account = SessionState.IsLoggedIn ? SessionState.CurrentAccount : string.Empty;
            return account.Trim();
        }

        private void ShowHomeLogDialog_Click(object sender, RoutedEventArgs e)
        {
            TryShowHomeLogWindow();
        }

        private void TryShowHomeLogWindow()
        {
            Window? ownerWindow = (Application.Current as App)?.MainWindowInstance;

            LogViewerWindow.ShowOrActivate(ownerWindow);
        }

        private void AppendHomeLog(string message, UiLogLevel level, UiLogSource source = UiLogSource.App)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            UiLogEntry entry = UiLogStore.Append(message, level);
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
            EmptySearchHintTextBlock.Text = ResolveEmptySearchHintText();
            EmptySearchHintTextBlock.Visibility = !loading && _visibleResults.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private string ResolveEmptySearchHintText()
        {
            if (_allResults.Count > 0)
            {
                return L("MainPage/EmptySearchHint/FilterEmpty");
            }

            return _hasCompletedSearch
                ? L("MainPage/EmptySearchHint/SearchEmpty")
                : L("MainPage/EmptySearchHint/Initial");
        }

        private void EnsureHomeLogScrollToBottom()
        {
            // Popup log mode does not need in-page auto scrolling.
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            _isInactive = true;
            base.OnNavigatedFrom(e);
            IpatoolClient.CommandExecuting -= OnIpatoolCommandExecuting;
            IpatoolClient.CommandOutputReceived -= OnIpatoolCommandOutputReceived;
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
            _hasCompletedSearch = false;
            UpdateDeveloperFilterOptions();
            if (ResultList != null)
            {
                SetResultListItemsSource(null);
            }
        }

        private void OnIpatoolCommandExecuting(string command)
        {
            QueueUi(() => AppendHomeLog(command, UiLogLevel.Ipatool, UiLogSource.Ipatool));
        }

        private void OnIpatoolCommandOutputReceived(string line)
        {
            QueueUi(() => AppendHomeLog(line, UiLogLevel.Ipatool, UiLogSource.Ipatool));
        }

        private void QueueUi(Action action)
        {
            if (_isInactive)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isInactive)
                {
                    action();
                }
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

    public sealed class MainPagePurchaseBlockedHelpVisibilityConverter : IValueConverter
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly string PurchaseBlockedText = Loader.GetString("Common/Status/PurchaseBlocked");

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? status = value as string;
            return !string.IsNullOrWhiteSpace(status)
                && status.Trim().Equals(PurchaseBlockedText, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class MainPagePurchaseBlockedReasonConverter : IValueConverter
    {
        private static readonly ResourceLoader Loader = new();

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string price = value as string ?? string.Empty;
            return string.IsNullOrWhiteSpace(price)
                ? Loader.GetString("MainPage/PurchaseBlockedReason/Unknown")
                : string.Format(
                    CultureInfo.CurrentCulture,
                    Loader.GetString("MainPage/PurchaseBlockedReason/NonFree"),
                    price.Trim());
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
