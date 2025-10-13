using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.UI.Xaml.Input;

namespace IPAbuyer.Views
{
    public sealed partial class MainPage : Page
    {
        private readonly List<AppResult> _allResults = new();
        private readonly CancellationTokenSource _pageCts = new();
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalPages = 1;
        private bool _isPageLoaded;
        private bool _isLoggedIn;
        private string _selectedFilter = "All";
    private const string TestAccountName = "test";
    private static readonly TimeSpan TestPurchaseDelay = TimeSpan.FromMilliseconds(1000);

        public int SearchLimitNum { get; set; } = 5;

        public MainPage()
        {
            this.InitializeComponent();
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_pageCts.IsCancellationRequested)
            {
                _pageCts.Cancel();
            }
            _pageCts.Dispose();
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            _isLoggedIn = SessionState.IsLoggedIn;
            UpdateLoginButton();

            if (_isLoggedIn && !string.IsNullOrWhiteSpace(KeychainConfig.GetLastLoginUsername()))
            {
                UpdateStatusBar($"欢迎回来，{KeychainConfig.GetLastLoginUsername()}");
            }

            await RefreshLoginStatusAsync(_pageCts.Token);
        }

        private async Task RefreshLoginStatusAsync(CancellationToken cancellationToken)
        {
            var account = GetActiveAccount();
            if (!SessionState.IsLoggedIn || string.IsNullOrWhiteSpace(account))
            {
                _isLoggedIn = false;
                UpdateLoginButton();
                UpdateStatusBar("当前未登录", false);
                return;
            }

            if (IsTestAccount(account))
            {
                _isLoggedIn = true;
                SessionState.SetLoginState(TestAccountName, true);
                UpdateLoginButton();
                UpdateStatusBar("已登录账号名为: test");
                return;
            }

            UpdateStatusBar("正在检查登录状态...");

            try
            {
                var result = await ipatoolExecution.AuthInfoAsync(account, cancellationToken);
                string payload = result.OutputOrError;

                if (result.TimedOut)
                {
                    _isLoggedIn = false;
                    UpdateStatusBar("登录状态检查超时，请稍后再试", true);
                }
                else if (!string.IsNullOrWhiteSpace(payload))
                {
                    ParseLoginStatusResponse(payload, account);
                }
                else
                {
                    _isLoggedIn = false;
                    UpdateStatusBar("登录状态检查失败，未收到响应", true);
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatusBar("登录状态检查已取消", true);
            }
            catch (Exception ex)
            {
                _isLoggedIn = false;
                UpdateStatusBar($"登录状态检查失败: {ex.Message}", true);
            }

            UpdateLoginButton();
        }

        private void ParseLoginStatusResponse(string payload, string account)
        {
            foreach (var segment in EnumerateJsonSegments(payload))
            {
                if (TryHandleAuthSegment(segment, account))
                {
                    return;
                }
            }

            if (IsAuthenticationError(payload))
            {
                _isLoggedIn = false;
                SessionState.Reset();
                UpdateLoginButton();
                UpdateStatusBar("登录状态已失效，请重新登录", true);
                return;
            }

            UpdateStatusBar("未能确认登录状态，将维持当前登录状态", false);
        }

        private bool TryHandleAuthSegment(string segment, string account)
        {
            try
            {
                using var doc = JsonDocument.Parse(segment);
                var root = doc.RootElement;

                if (root.TryGetProperty("success", out var successElement))
                {
                    bool? success = TryReadBoolean(successElement);
                    if (success == true)
                    {
                        string displayAccount = KeychainConfig.GetLastLoginUsername() ?? account;
                        _isLoggedIn = true;
                        SessionState.SetLoginState(account, true);
                        UpdateLoginButton();
                        UpdateStatusBar($"已登录账户: {displayAccount}");
                        return true;
                    }

                    if (success == false)
                    {
                        string errorMessage = ExtractErrorMessage(root) ?? "登录状态无效";
                        _isLoggedIn = false;
                        SessionState.Reset();
                        UpdateLoginButton();
                        UpdateStatusBar($"未登录: {errorMessage}", true);
                        return true;
                    }
                }

                string? message = ExtractErrorMessage(root);
                if (!string.IsNullOrWhiteSpace(message) && IsAuthenticationError(message))
                {
                    _isLoggedIn = false;
                    SessionState.Reset();
                    UpdateLoginButton();
                    UpdateStatusBar("登录状态已失效，请重新登录", true);
                    return true;
                }
            }
            catch (JsonException)
            {
                // 尝试解析下一个片段
            }

            return false;
        }

        private static IEnumerable<string> EnumerateJsonSegments(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                yield break;
            }

            string normalized = payload.Replace("}{", "}\n{");
            string[] lines = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                {
                    yield return trimmed;
                }
            }

            if (lines.Length == 0)
            {
                string trimmed = payload.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    yield return trimmed;
                }
            }
        }

        private static string? ExtractAccountEmail(JsonElement root)
        {
            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
            {
                if (dataElement.TryGetProperty("account", out var accountElement) && accountElement.ValueKind == JsonValueKind.Object)
                {
                    if (accountElement.TryGetProperty("email", out var emailElement) && emailElement.ValueKind == JsonValueKind.String)
                    {
                        return emailElement.GetString();
                    }
                }
            }

            if (root.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String)
            {
                return email.GetString();
            }

            return null;
        }

        private static string? ExtractErrorMessage(JsonElement root)
        {
            if (root.TryGetProperty("error", out var errorElement))
            {
                return errorElement.GetString();
            }

            if (root.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }

            if (root.TryGetProperty("reason", out var reasonElement))
            {
                return reasonElement.GetString();
            }

            return null;
        }

        private static bool? TryReadBoolean(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(element.GetString(), out bool parsed) => parsed,
                JsonValueKind.Number => element.TryGetInt32(out int value) ? value != 0 : (bool?)null,
                _ => null,
            };
        }

        private string? GetActiveAccount()
        {
            if (!SessionState.IsLoggedIn)
            {
                return null;
            }

            var account = KeychainConfig.GetLastLoginUsername();
            var normalized = NormalizeAccount(account);
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }

        private void SearchLimit_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SearchLimitNum = (int)e.NewValue;
            var searchLimitText = GetControl<TextBlock>("SearchLimitNumText");
            if (searchLimitText != null)
            {
                searchLimitText.Text = SearchLimitNum.ToString();
            }
            UpdateStatusBar("已调整搜索范围");
        }

        private void UpdateLoginButton()
        {
            if (GetControl<Button>("LogoutButton") is not Button logoutButton)
            {
                return;
            }

            if (_isLoggedIn)
            {
                logoutButton.Content = "退出登录";
                logoutButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            else
            {
                logoutButton.Content = "前往登录";
                logoutButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
            }
        }

        private void UpdateStatusBar(string message, bool isError = false)
        {
            var resultText = GetControl<TextBlock>("ResultText");

            if (resultText == null || !_isPageLoaded)
            {
                Debug.WriteLine($"[状态栏] {message}");
                return;
            }

            try
            {
                resultText.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
                resultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(isError ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Gray);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新状态栏失败: {ex.Message}");
            }
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage <= 1)
            {
                UpdateStatusBar("已经是第一页了", true);
                return;
            }

            _currentPage--;
            UpdatePage();
            UpdateStatusBar($"已切换到第 {_currentPage}/{_totalPages} 页");
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage >= _totalPages)
            {
                UpdateStatusBar("已经是最后一页了", true);
                return;
            }

            _currentPage++;
            UpdatePage();
            UpdateStatusBar($"已切换到第 {_currentPage}/{_totalPages} 页");
        }

        private async void BatchPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoggedIn)
            {
                UpdateStatusBar("请先登录账户", true);
                return;
            }

            if (GetControl<ListView>("ResultList") is not ListView resultList || resultList.SelectedItems.Count == 0)
            {
                UpdateStatusBar("请先选择要购买的免费项目", true);
                return;
            }

            var account = GetActiveAccount();
            var normalizedAccount = NormalizeAccount(account);
            if (string.IsNullOrWhiteSpace(normalizedAccount))
            {
                UpdateStatusBar("无法获取当前账户，请重新登录", true);
                return;
            }

            bool isTestAccount = IsTestAccount(normalizedAccount);

            var appsToPurchase = resultList.SelectedItems.OfType<AppResult>().ToList();
            if (appsToPurchase.Count == 0)
            {
                UpdateStatusBar("未选择有效的应用条目", true);
                return;
            }

            var batchPurchaseButton = GetControl<Button>("BatchPurchaseButton");
            if (batchPurchaseButton != null)
            {
                batchPurchaseButton.IsEnabled = false;
            }

            int successCount = 0;
            int failCount = 0;
            int skipCount = 0;
            List<string> ownedButFailed = new();
            List<AppResult> pricedApps = new();

            UpdateStatusBar($"开始购买 {appsToPurchase.Count} 个应用...");

            foreach (var app in appsToPurchase)
            {
                if (app.purchased is "已购买" or "已拥有")
                {
                    skipCount++;
                    continue;
                }

                bool isFreeApp = IsPriceFree(app.price);

                if (isTestAccount)
                {
                    await Task.Delay(TestPurchaseDelay, _pageCts.Token);
                    successCount++;
                    app.purchased = "已拥有";
                    if (!string.IsNullOrWhiteSpace(app.bundleID))
                    {
                        PurchasedAppDb.SavePurchasedApp(app.bundleID, normalizedAccount, "已拥有");
                    }
                    UpdateStatusBar($"成功购买: {app.name}");
                    continue;
                }

                if (!isFreeApp)
                {
                    failCount++;
                    pricedApps.Add(app);
                    UpdateStatusBar($"购买失败(非免费): {app.name}", true);
                    continue;
                }

                UpdateStatusBar($"正在购买: {app.name}...");

                try
                {
                    var result = await ipatoolExecution.PurchaseAppAsync(app.bundleID ?? string.Empty, normalizedAccount, _pageCts.Token);
                    string payload = result.OutputOrError;

                    if (IsPurchaseSuccess(payload))
                    {
                        successCount++;
                        app.purchased = "已购买";
                        PurchasedAppDb.SavePurchasedApp(app.bundleID ?? string.Empty, normalizedAccount, "已购买");
                        UpdateStatusBar($"成功购买: {app.name}");
                    }
                    else
                    {
                        failCount++;
                        if (isFreeApp)
                        {
                            app.purchased = "已拥有";
                            PurchasedAppDb.SavePurchasedApp(app.bundleID ?? string.Empty, normalizedAccount, "已拥有");
                            ownedButFailed.Add(app.name ?? string.Empty);
                            UpdateStatusBar($"购买失败但已拥有: {app.name}", true);
                        }
                        else
                        {
                            UpdateStatusBar($"购买失败: {app.name}", true);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    UpdateStatusBar("购买操作被取消", true);
                    break;
                }
                catch (Exception ex)
                {
                    failCount++;
                    UpdateStatusBar($"购买失败: {app.name} ({ex.Message})", true);
                }
            }

            string extra = ownedButFailed.Count > 0 ? $"，购买失败但已拥有: {ownedButFailed.Count} 个" : string.Empty;
            UpdateStatusBar($"批量购买完成 - 成功: {successCount}, 失败: {failCount}, 跳过: {skipCount}{extra}");

            if (batchPurchaseButton != null)
            {
                batchPurchaseButton.IsEnabled = true;
            }
            RefreshPurchasedStatus();

            if (pricedApps.Count > 0)
            {
                var dialog = new ContentDialog
                {
                    Title = "发现付费应用",
                    Content = $"以下 {pricedApps.Count} 个应用价格不为 0，已标记为购买失败:" + Environment.NewLine + string.Join(Environment.NewLine, pricedApps.Select(a => $"• {a.name} ({a.price})")),
                    PrimaryButtonText = "知道了",
                    XamlRoot = this.XamlRoot
                };

                _ = dialog.ShowAsync();
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
                if (doc.RootElement.TryGetProperty("success", out var successElement))
                {
                    return successElement.ValueKind == JsonValueKind.True && successElement.GetBoolean();
                }
            }
            catch (JsonException)
            {
                // JSON 解析失败时退回到字符串判断
            }

            return false;
        }

        private void RefreshPurchasedStatus()
        {
            var account = GetActiveAccount();
            var normalizedAccount = NormalizeAccount(account);
            if (string.IsNullOrWhiteSpace(normalizedAccount))
            {
                return;
            }

            var purchasedDict = PurchasedAppDb.GetPurchasedApps(normalizedAccount).ToDictionary(x => x.appID, x => x.status);

            foreach (var app in _allResults)
            {
                var key = app.bundleID ?? string.Empty;
                if (purchasedDict.TryGetValue(key, out var status))
                {
                    app.purchased = status;
                }
            }

            UpdatePage();
        }

        private List<AppResult> GetFilteredResults()
        {
            return _selectedFilter switch
            {
                "OnlyPurchased" => _allResults.Where(a => a.purchased == "已购买").ToList(),
                "OnlyNotPurchased" => _allResults.Where(a => a.purchased == "未购买" || a.purchased == "可购买").ToList(),
                "OnlyHad" => _allResults.Where(a => a.purchased == "已拥有").ToList(),
                _ => _allResults.ToList(),
            };
        }

        private void ScreeningComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPageLoaded || GetControl<ComboBox>("ScreeningComboBox")?.SelectedItem is not ComboBoxItem selectedItem)
            {
                return;
            }

            _selectedFilter = selectedItem.Tag?.ToString() ?? "All";
            var displayList = GetFilteredResults();
            _totalPages = displayList.Count > 0 ? (displayList.Count + _pageSize - 1) / _pageSize : 1;
            _currentPage = 1;
            UpdatePage();
        }

        private void UpdatePage()
        {
            if (!_isPageLoaded)
            {
                return;
            }

            var resultList = GetControl<ListView>("ResultList");
            var prevPageButton = GetControl<Button>("PrevPageButton");
            var nextPageButton = GetControl<Button>("NextPageButton");

            if (resultList == null)
            {
                return;
            }

            var displayList = GetFilteredResults();

            if (displayList.Count == 0)
            {
                resultList.ItemsSource = null;
                if (prevPageButton != null)
                {
                    prevPageButton.IsEnabled = false;
                }
                if (nextPageButton != null)
                {
                    nextPageButton.IsEnabled = false;
                }
                return;
            }

            int start = (_currentPage - 1) * _pageSize;
            int end = Math.Min(start + _pageSize, displayList.Count);
            resultList.ItemsSource = displayList.GetRange(start, end - start);

            if (prevPageButton != null)
            {
                prevPageButton.IsEnabled = _currentPage > 1;
            }

            if (nextPageButton != null)
            {
                nextPageButton.IsEnabled = _currentPage < _totalPages;
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync(_pageCts.Token);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(Settings));
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var logoutButton = GetControl<Button>("LogoutButton");
            if (logoutButton != null)
            {
                logoutButton.IsEnabled = false;
            }

            if (!_isLoggedIn)
            {
                UpdateStatusBar("正在跳转到登录页面...");
                if (logoutButton != null)
                {
                    logoutButton.IsEnabled = true;
                }
                Frame.Navigate(typeof(LoginPage));
                return;
            }

            var account = GetActiveAccount();
            var normalizedAccount = NormalizeAccount(account);
            if (!string.IsNullOrWhiteSpace(normalizedAccount) && !IsTestAccount(normalizedAccount))
            {
                try
                {
                    UpdateStatusBar("正在退出登录...");
                    await ipatoolExecution.AuthLogoutAsync(normalizedAccount, _pageCts.Token);
                }
                catch (OperationCanceledException)
                {
                    UpdateStatusBar("退出登录被取消", true);
                }
                catch (Exception ex)
                {
                    UpdateStatusBar($"退出登录失败: {ex.Message}", true);
                }
            }

            _isLoggedIn = false;
            SessionState.Reset();
            UpdateLoginButton();
            UpdateStatusBar("已退出登录");
            if (logoutButton != null)
            {
                logoutButton.IsEnabled = true;
            }
            Frame.Navigate(typeof(LoginPage));
        }

        private async void Search_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;

            if (sender is Control control)
            {
                control.IsEnabled = false;
                control.IsEnabled = true;
            }

            await PerformSearchAsync(_pageCts.Token);
        }

        private async Task PerformSearchAsync(CancellationToken cancellationToken)
        {
            var account = GetActiveAccount();
            var normalizedAccount = NormalizeAccount(account);

            var appNameBox = GetControl<TextBox>("AppNameBox");
            string appName = appNameBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(appName))
            {
                UpdateStatusBar("请输入应用名称", true);
                appNameBox?.Focus(FocusState.Programmatic);
                return;
            }

            var searchButton = GetControl<Button>("SearchButton");
            if (searchButton != null)
            {
                searchButton.IsEnabled = false;
            }
            UpdateStatusBar($"正在搜索 \"{appName}\"...");

            try
            {
                var result = await ipatoolExecution.SearchAppAsync(appName, SearchLimitNum, normalizedAccount, cancellationToken);

                if (result.TimedOut)
                {
                    UpdateStatusBar("搜索超时，请稍后再试", true);
                    return;
                }

                string payload = result.OutputOrError;
                if (string.IsNullOrWhiteSpace(payload))
                {
                    UpdateStatusBar("搜索失败：未收到有效响应", true);
                    return;
                }

                ParseSearchResponse(payload, normalizedAccount);
            }
            catch (OperationCanceledException)
            {
                UpdateStatusBar("搜索已取消", true);
            }
            catch (Exception ex)
            {
                UpdateStatusBar($"搜索失败: {ex.Message}", true);
            }
            finally
            {
                if (searchButton != null)
                {
                    searchButton.IsEnabled = true;
                }
            }
        }

        private void ParseSearchResponse(string payload, string account)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                if (!root.TryGetProperty("results", out var resultsElement) || resultsElement.ValueKind != JsonValueKind.Array)
                {
                    UpdateStatusBar("搜索结果为空或格式错误", true);
                    return;
                }

                var purchasedDict = string.IsNullOrEmpty(account)
                    ? new Dictionary<string, string>()
                    : PurchasedAppDb.GetPurchasedApps(account).ToDictionary(x => x.appID, x => x.status);

                _allResults.Clear();
                int successCount = 0;
                int failCount = 0;

                foreach (var appElement in resultsElement.EnumerateArray())
                {
                    try
                    {
                        var bundleId = GetBundleId(appElement);
                        var app = new AppResult
                        {
                            bundleID = bundleId,
                            id = GetPropertyValue(appElement, "trackId"),
                            name = GetPropertyValue(appElement, "trackName"),
                            price = GetPriceValue(appElement),
                            version = GetPropertyValue(appElement, "version"),
                            purchased = purchasedDict.TryGetValue(bundleId ?? string.Empty, out var status)
                                ? status
                                : "可购买"
                        };

                        _allResults.Add(app);
                        successCount++;
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                _totalPages = _allResults.Count > 0 ? (_allResults.Count + _pageSize - 1) / _pageSize : 1;
                _currentPage = 1;
                UpdatePage();

                int resultCount = root.TryGetProperty("resultCount", out var countElement) && countElement.TryGetInt32(out int countValue)
                    ? countValue
                    : _allResults.Count;

                UpdateStatusBar($"搜索完成 - 找到 {_allResults.Count} 个应用 (成功: {successCount}, 失败: {failCount})");
            }
            catch (JsonException ex)
            {
                UpdateStatusBar($"解析搜索结果失败: {ex.Message}", true);
            }
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

        private static bool IsAuthenticationError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string lower = message.ToLowerInvariant();
            return lower.Contains("failed to get account")
                || lower.Contains("session expired")
                || lower.Contains("not logged in")
                || lower.Contains("please log in")
                || (lower.Contains("authentication") && (lower.Contains("fail") || lower.Contains("invalid")))
                || (lower.Contains("keychain") && lower.Contains("passphrase"));
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string accountParam && !string.IsNullOrWhiteSpace(accountParam))
            {
                string normalizedAccount = NormalizeAccount(accountParam);
                _isLoggedIn = true;
                SessionState.SetLoginState(normalizedAccount, true);
                UpdateLoginButton();
                UpdateStatusBar($"登录成功，欢迎使用 {normalizedAccount}");
            }
            else if (e.Parameter is bool loginStatus)
            {
                _isLoggedIn = loginStatus;
                if (_isLoggedIn)
                {
                    var account = GetActiveAccount();
                    if (!string.IsNullOrWhiteSpace(account))
                    {
                        SessionState.SetLoginState(account, true);
                    }

                    UpdateStatusBar("登录成功，欢迎使用");
                }
                UpdateLoginButton();
            }

            await RefreshLoginStatusAsync(_pageCts.Token);
        }

        private static string NormalizeAccount(string? account)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return string.Empty;
            }

            string trimmed = account.Trim();
            return trimmed.Equals(TestAccountName, StringComparison.OrdinalIgnoreCase) ? TestAccountName : trimmed;
        }

        private static bool IsTestAccount(string? account)
        {
            return NormalizeAccount(account) == TestAccountName;
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

        private T? GetControl<T>(string name)
            where T : class
        {
            return FindName(name) as T;
        }
    }
}