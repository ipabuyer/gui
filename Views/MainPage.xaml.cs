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
        private readonly DownloadQueueService _downloadQueueService = DownloadQueueService.Instance;
        private readonly StringBuilder _homeLogBuilder = new();
        private readonly List<UiLogEntry> _homeLogEntries = new();
        private CancellationTokenSource _pageCts = new();
        private bool _isHomeLogDialogOpen;
        private string _selectedFilter = "All";
        private static readonly string StatusPurchased = L("Common/Status/Purchased");
        private static readonly string StatusOwned = L("Common/Status/Owned");
        private static readonly string StatusCanPurchase = L("Common/Status/NotPurchased");

        private static readonly string[] PurchasedAliases = { StatusPurchased };
        private static readonly string[] OwnedAliases = { StatusOwned };
        private static readonly string[] CanPurchaseAliases = { L("Common/Status/CanPurchase"), StatusCanPurchase };
        private static readonly string NameHeaderBase = L("MainPage/Header/NameButton/Content");
        private static readonly string IdHeaderBase = L("MainPage/Header/IdButton/Content");
        private static readonly string DeveloperHeaderBase = L("MainPage/Header/DeveloperButton/Content");
        private static readonly string VersionHeaderBase = L("MainPage/Header/VersionButton/Content");
        private static readonly string PriceHeaderBase = L("MainPage/Header/PriceButton/Content");
        private static readonly string PurchasedHeaderBase = L("MainPage/Header/PurchasedButton/Content");
        private const int MaxLogLines = 1000;

        public int SearchLimitNum { get; set; } = 100;

        public MainPage()
        {
            InitializeComponent();
            InitializeResultColumns();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        }

        private void InitializeResultColumns()
        {
            if (NameColumn == null)
            {
                return;
            }

            NameColumn.Header = NameHeaderBase;
            IdColumn.Header = IdHeaderBase;
            DeveloperColumn.Header = DeveloperHeaderBase;
            VersionColumn.Header = VersionHeaderBase;
            PriceColumn.Header = PriceHeaderBase;
            PurchasedColumn.Header = PurchasedHeaderBase;
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
                AppendHomeLog(L("MainPage/Log/SearchEmptyIgnored"));
                return;
            }

            AppendHomeLog(LF("MainPage/Log/SearchStarted", appName.Trim()));
            SetTableLoading(true);
            try
            {
                await PerformSearchAsync(appName.Trim(), _pageCts.Token);
            }
            catch (Exception ex)
            {
                AppendHomeLog(LF("MainPage/Log/SearchException", ex.Message));
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
                    ResultList.ItemsSource = null;
                }

                AppendHomeLog(L("MainPage/Log/SearchTimeoutOrEmpty"));
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
                AppendHomeLog(LF("MainPage/Log/SearchCompleted", _allResults.Count));
            }
            catch (JsonException)
            {
                if (ResultList != null)
                {
                    ResultList.ItemsSource = null;
                }

                AppendHomeLog(L("MainPage/Log/SearchParseFailed"));
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
                AppendHomeLog(L("MainPage/Log/BatchPurchaseEmpty"));
                return;
            }

            if (BatchPurchaseButton != null)
            {
                BatchPurchaseButton.IsEnabled = false;
            }
            SetTableLoading(true);

            try
            {
                AppendHomeLog(LF("MainPage/Log/BatchPurchaseStarted", selectedApps.Count));
                bool executed = await PurchaseAppsAsync(selectedApps);
                if (executed)
                {
                    AppendHomeLog(L("MainPage/Log/BatchPurchaseCompleted"));
                }
            }
            catch (OperationCanceledException)
            {
                AppendHomeLog(L("MainPage/Log/BatchPurchaseCanceled"));
            }
            catch (Exception ex)
            {
                AppendHomeLog(LF("MainPage/Log/BatchPurchaseException", ex.Message));
            }
            finally
            {
                if (BatchPurchaseButton != null)
                {
                    BatchPurchaseButton.IsEnabled = true;
                }
                SetTableLoading(false);

                ApplyFilterAndRefresh(preserveScrollPosition: true);
            }
        }

        private async void AddToDownloadQueueButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultList == null)
            {
                return;
            }

            int added = 0;
            int updated = 0;
            int ignored = 0;
            foreach (var app in ResultList.SelectedItems.OfType<SearchResult>())
            {
                CountDownloadQueueAddResult(_downloadQueueService.AddOrUpdateFromSearchResult(app), ref added, ref updated, ref ignored);
            }

            if (added == 0 && updated == 0)
            {
                AppendHomeLog(ignored > 0 ? L("MainPage/DownloadQueue/AddSelectedIgnored") : L("MainPage/DownloadQueue/AddSelectedEmpty"));
                return;
            }

            AppendHomeLog(BuildDownloadQueueAddSummary(added, updated));
            await StartDownloadQueueFromMainAsync();
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
            SetTableLoading(true);

            try
            {
                AppendHomeLog(LF("MainPage/Log/ContextPurchaseStarted", selectedApps.Count));
                _ = await PurchaseAppsAsync(selectedApps);
            }
            catch (OperationCanceledException)
            {
                AppendHomeLog(L("MainPage/Log/ContextPurchaseCanceled"));
            }
            catch (Exception ex)
            {
                AppendHomeLog(LF("MainPage/Log/ContextPurchaseException", ex.Message));
            }
            finally
            {
                if (BatchPurchaseButton != null)
                {
                    BatchPurchaseButton.IsEnabled = true;
                }
                SetTableLoading(false);

                ApplyFilterAndRefresh(preserveScrollPosition: true);
            }
        }

        private async void ContextMenuAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var selectedApps = GetContextTargetApps(sender);
            if (selectedApps.Count == 0)
            {
                return;
            }

            int added = 0;
            int updated = 0;
            int ignored = 0;
            foreach (var app in selectedApps)
            {
                CountDownloadQueueAddResult(_downloadQueueService.AddOrUpdateFromSearchResult(app), ref added, ref updated, ref ignored);
            }

            if (added == 0 && updated == 0)
            {
                AppendHomeLog(ignored > 0 ? L("MainPage/DownloadQueue/AddContextIgnored") : L("MainPage/DownloadQueue/AddContextEmpty"));
                return;
            }

            AppendHomeLog(LF("MainPage/DownloadQueue/ContextAddSummaryPrefix", BuildDownloadQueueAddSummary(added, updated)));
            await StartDownloadQueueFromMainAsync();
        }

        private async Task StartDownloadQueueFromMainAsync()
        {
            if (_downloadQueueService.IsRunning)
            {
                AppendHomeLog(L("MainPage/DownloadQueue/AlreadyRunningContinue"));
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
                AppendHomeLog(LF("MainPage/DownloadQueue/StartFailed", ex.Message));
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
                AppendHomeLog(log.Message, log.Source);
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
            AddToDownloadQueueButton.IsEnabled = !isRunning;
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
            AppendHomeLog(LF("MainPage/Log/MarkedStatus", selectedApps.Count, status));
        }

        private void ContextMenuCopyName_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.name ?? string.Empty, L("MainPage/Field/Name"));
        }

        private void ContextMenuCopyId_Click(object sender, RoutedEventArgs e)
        {
            CopyField(sender, app => app.id ?? string.Empty, L("MainPage/Field/Id"));
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
                AppendHomeLog(LF("MainPage/Log/CopyFieldEmpty", fieldName));
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(value);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendHomeLog(LF("MainPage/Log/CopyFieldSuccess", fieldName, selectedApps.Count));
        }

        private void ScreeningComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreeningComboBox != null && ScreeningComboBox.SelectedItem is ComboBoxItem selected)
            {
                _selectedFilter = selected.Tag?.ToString() ?? "All";
                ApplyFilterAndRefresh();
                AppendHomeLog(LF("MainPage/Log/FilterChanged", _selectedFilter));
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
                AppendHomeLog(message);
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
                    AppendHomeLog(LF("MainPage/Purchase/SkipNonFree", app.name ?? app.bundleId));
                    continue;
                }

                if (isTestAccount)
                {
                    app.purchased = StatusPurchased;
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusPurchased);
                    AppendHomeLog(LF("MainPage/Purchase/MockSuccess", app.name ?? app.bundleId));
                    continue;
                }

                var result = await IpatoolExecution.PurchaseAppAsync(app.bundleId, account, _pageCts.Token);
                if (IsPurchaseSuccess(result.OutputOrError))
                {
                    app.purchased = StatusPurchased;
                    PurchasedAppDb.SavePurchasedApp(app.bundleId, account, StatusPurchased);
                    AppendHomeLog(LF("MainPage/Purchase/Success", app.name ?? app.bundleId));
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
                            AppendHomeLog(LF("MainPage/Purchase/OwnedMarked", app.name ?? app.bundleId));
                        }
                        else
                        {
                            AppendHomeLog(LF("MainPage/Purchase/OwnedNotMarked", app.name ?? app.bundleId));
                        }
                    }
                    else
                    {
                        string reason = string.IsNullOrWhiteSpace(result.OutputOrError)
                            ? L("MainPage/Purchase/UnknownError")
                            : result.OutputOrError;
                        AppendHomeLog(LF("MainPage/Purchase/Failed", app.name ?? app.bundleId, reason));
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
                Text = LF("MainPage/OwnedPrompt/Message", app.name ?? app.bundleId),
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
                || normalized.Equals(L("Common/Price/Free"), StringComparison.OrdinalIgnoreCase);
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
                AppendHomeLog(L("MainPage/Log/CopyEmptyLog"));
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendHomeLog(L("MainPage/Log/CopiedToClipboard"));
        }

        private void ClearHomeLog_Click(object sender, RoutedEventArgs e)
        {
            ClearHomeLog();
        }

        private void ClearHomeLog()
        {
            _homeLogBuilder.Clear();
            _homeLogEntries.Clear();
            AppendHomeLog(L("MainPage/Log/Cleared"));
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

        private void AppendHomeLog(string message, UiLogSource source = UiLogSource.App)
        {
            if (string.IsNullOrWhiteSpace(message))
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

    }

    public sealed class MainPagePriceForegroundConverter : IValueConverter
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly string FreeText = Loader.GetString("Common/Price/Free");

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            string? price = value as string;
            if (string.IsNullOrWhiteSpace(price)
                || price.Trim().Equals(FreeText, StringComparison.OrdinalIgnoreCase)
                || price.Trim().Equals("free", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));
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

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
