using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IPAbuyer.Common;
using IPAbuyer.Data;
using IPAbuyer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IPAbuyer.Views
{
    public sealed partial class MainPage : Page
    {
        private readonly List<SearchResult> _allResults = new();
        private readonly DownloadQueueService _downloadQueueService = DownloadQueueService.Instance;
        private CancellationTokenSource _pageCts = new();
        private string _selectedFilter = "All";
        private const string TestAccountName = "test";

        public int SearchLimitNum { get; set; } = 100;

        public MainPage()
        {
            InitializeComponent();
        }

        public async void PerformSearchFromMainWindow(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                return;
            }

            await PerformSearchAsync(appName.Trim(), _pageCts.Token);
        }

        private async Task PerformSearchAsync(string appName, CancellationToken cancellationToken)
        {
            string account = GetActiveAccount();
            string countryCode = NormalizeCountryCode(KeychainConfig.GetCountryCode(account));

            var result = await ipatoolExecution.SearchAppAsync(appName, SearchLimitNum, account, countryCode, cancellationToken);
            if (result.TimedOut || string.IsNullOrWhiteSpace(result.OutputOrError))
            {
                if (ResultList != null)
                {
                    ResultList.ItemsSource = null;
                }
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

                var purchasedDict = string.IsNullOrWhiteSpace(account)
                    ? new Dictionary<string, string>()
                    : PurchasedAppDb.GetPurchasedApps(account).ToDictionary(x => x.appID, x => x.status);

                _allResults.Clear();
                foreach (var appElement in resultsElement.EnumerateArray())
                {
                    string bundleId = GetBundleId(appElement) ?? string.Empty;
                    string status = purchasedDict.TryGetValue(bundleId, out string? purchasedStatus)
                        ? purchasedStatus
                        : "可购买";

                    _allResults.Add(new SearchResult
                    {
                        bundleID = bundleId,
                        id = GetPropertyValue(appElement, "trackId"),
                        name = GetPropertyValue(appElement, "trackName"),
                        developer = GetPropertyValue(appElement, "sellerName"),
                        artworkUrl = GetPropertyValue(appElement, "artworkUrl100"),
                        price = GetPriceValue(appElement),
                        version = GetPropertyValue(appElement, "version"),
                        purchased = status
                    });
                }

                ApplyFilterAndRefresh();
            }
            catch (JsonException)
            {
                if (ResultList != null)
                {
                    ResultList.ItemsSource = null;
                }
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
                return;
            }

            if (BatchPurchaseButton != null)
            {
                BatchPurchaseButton.IsEnabled = false;
            }
            try
            {
                await PurchaseAppsAsync(selectedApps);
            }
            finally
            {
                if (BatchPurchaseButton != null)
                {
                    BatchPurchaseButton.IsEnabled = true;
                }
                ApplyFilterAndRefresh();
            }
        }

        private void AddToDownloadQueueButton_Click(object sender, RoutedEventArgs e)
        {
            if (ResultList == null)
            {
                return;
            }

            foreach (var app in ResultList.SelectedItems.OfType<SearchResult>())
            {
                _downloadQueueService.AddOrUpdateFromSearchResult(app);
            }
        }

        private async void ContextMenuPurchase_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveContextItem(sender) is not SearchResult app)
            {
                return;
            }

            if (BatchPurchaseButton != null)
            {
                BatchPurchaseButton.IsEnabled = false;
            }
            try
            {
                await PurchaseAppsAsync(new List<SearchResult> { app });
            }
            finally
            {
                if (BatchPurchaseButton != null)
                {
                    BatchPurchaseButton.IsEnabled = true;
                }
                ApplyFilterAndRefresh();
            }
        }

        private void ContextMenuAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveContextItem(sender) is not SearchResult app)
            {
                return;
            }

            _downloadQueueService.AddOrUpdateFromSearchResult(app);
        }

        private SearchResult? ResolveContextItem(object sender)
        {
            if (sender is not MenuFlyoutItem menuItem)
            {
                return null;
            }

            if (menuItem.DataContext is SearchResult result)
            {
                return result;
            }

            if (menuItem.Parent is MenuFlyout flyout && flyout.Target is FrameworkElement target && target.DataContext is SearchResult targetResult)
            {
                return targetResult;
            }

            return null;
        }

        private void ScreeningComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreeningComboBox != null && ScreeningComboBox.SelectedItem is ComboBoxItem selected)
            {
                _selectedFilter = selected.Tag?.ToString() ?? "All";
                ApplyFilterAndRefresh();
            }
        }

        private void ApplyFilterAndRefresh()
        {
            var filtered = GetFilteredResults();
            if (ResultList == null)
            {
                return;
            }

            ResultList.ItemsSource = null;
            ResultList.ItemsSource = filtered;
        }

        private List<SearchResult> GetFilteredResults()
        {
            return _selectedFilter switch
            {
                "OnlyPurchased" => _allResults.Where(a => a.purchased == "已购买").ToList(),
                "OnlyNotPurchased" => _allResults.Where(a => a.purchased == "未购买" || a.purchased == "可购买").ToList(),
                "OnlyHad" => _allResults.Where(a => a.purchased == "已拥有").ToList(),
                _ => _allResults.ToList(),
            };
        }

        private async Task PurchaseAppsAsync(List<SearchResult> selectedApps)
        {
            string account = GetActiveAccount();
            if (string.IsNullOrWhiteSpace(account))
            {
                return;
            }

            bool isTestAccount = IsTestAccount(account);

            foreach (var app in selectedApps)
            {
                if (string.IsNullOrWhiteSpace(app.bundleID))
                {
                    continue;
                }

                if (app.purchased is "已购买" or "已拥有")
                {
                    continue;
                }

                if (!IsPriceFree(app.price))
                {
                    app.purchased = "已拥有";
                    PurchasedAppDb.SavePurchasedApp(app.bundleID, account, "已拥有");
                    continue;
                }

                if (isTestAccount)
                {
                    app.purchased = "已购买";
                    PurchasedAppDb.SavePurchasedApp(app.bundleID, account, "已购买");
                    continue;
                }

                var result = await ipatoolExecution.PurchaseAppAsync(app.bundleID, account, _pageCts.Token);
                if (IsPurchaseSuccess(result.OutputOrError))
                {
                    app.purchased = "已购买";
                    PurchasedAppDb.SavePurchasedApp(app.bundleID, account, "已购买");
                }
                else
                {
                    app.purchased = "已拥有";
                    PurchasedAppDb.SavePurchasedApp(app.bundleID, account, "已拥有");
                }
            }
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
            if (string.IsNullOrWhiteSpace(account))
            {
                account = KeychainConfig.GetLastLoginUsername() ?? string.Empty;
            }

            return account.Trim();
        }

        private static bool IsTestAccount(string? account)
        {
            return string.Equals(account?.Trim(), TestAccountName, StringComparison.OrdinalIgnoreCase);
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

            return price.Trim().Equals("free", StringComparison.OrdinalIgnoreCase);
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

        private static string NormalizeCountryCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "cn";
            }

            string normalized = code.Trim().ToLowerInvariant();
            return KeychainConfig.IsValidCountryCode(normalized) ? normalized : "cn";
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (!_pageCts.IsCancellationRequested)
            {
                _pageCts.Cancel();
            }

            _pageCts.Dispose();
            _pageCts = new CancellationTokenSource();
        }
    }
}
