using System.Diagnostics;
using System.IO;
using IPAbuyer.Data;
using IPAbuyer.Views;
using IPAbuyer.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace IPAbuyer.Views
{
    public sealed partial class MainPage : Page
    {
        private string _email = KeychainConfig.GetLastLoginUsername();

        // 搜索范围限制
        public int SearchLimitNum { get; set; } = 5;

        private void SearchLimit_ValueChanged(
            object sender,
            Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e
        )
        {
            SearchLimitNum = (int)e.NewValue;
            var tb = this.FindName("SearchLimitNumText") as TextBlock;
            if (tb != null)
                tb.Text = SearchLimitNum.ToString();
            UpdateStatusBar($"已调整搜索范围");
        }

        // 查询结果数据模型
        public class AppResult
        {
            public string? bundleID { get; set; }
            public string? id { get; set; }
            public string? name { get; set; }
            public string? price { get; set; }
            public string? version { get; set; }
            public string? purchased { get; set; }
        }

        private List<AppResult> allResults = new List<AppResult>();
        private int currentPage = 1;
        private int pageSize = 10;
        private int totalPages = 1;
        private bool isLoggedIn = false;
        private bool isPageLoaded = false;
        private string selectedValue;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;

        }

        // ipatool 路径和执行统一交由 IPAbuyer.Common.IpatoolRunner 管理

        /// <summary>
        /// 页面加载时检查登录状态
        /// </summary>
        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            isPageLoaded = true;

            // 检查是否有从登录页面传递过来的登录状态
            if (this.DataContext is bool loginStatus)
            {
                isLoggedIn = loginStatus;
                if (isLoggedIn)
                {
                    UpdateStatusBar("登录成功，欢迎使用");
                }
            }
            else
            {
                // 如果没有传递状态，则检查本地登录状态
                await CheckLoginStatus();
            }

            UpdateLoginButton();
        }

        /// <summary>
        /// 检查登录状态
        /// </summary>
        private async Task CheckLoginStatus()
        {
            UpdateStatusBar("正在检查登录状态...");

            // 尝试验证登录状态
            try
            {
                var result = ipatoolExecution.searchApp("test", 1, KeychainConfig.GetSecretKey(_email ?? string.Empty));

                if (result.Contains("apps") || result.Contains("success"))
                {
                    isLoggedIn = true;
                    UpdateStatusBar($"已登录账户: {_email}");
                }
                else
                {
                    isLoggedIn = false;
                    UpdateStatusBar($"无法验证登录状态,请尝试重新登录");
                }
            }
            catch (Exception ex)
            {
                isLoggedIn = false;
                UpdateStatusBar($"登录状态检查失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新登录按钮状态
        /// </summary>
        private void UpdateLoginButton()
        {
            if (isLoggedIn)
            {
                LogoutButton.Content = "退出登录";
                LogoutButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red
                );
            }
            else
            {
                LogoutButton.Content = "前往登录";
                LogoutButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.DodgerBlue
                );
            }
        }

        /// <summary>
        /// 更新状态栏信息
        /// </summary>
        private void UpdateStatusBar(string message, bool isError = false)
        {
            if (ResultText == null || !isPageLoaded)
            {
                Debug.WriteLine($"[状态栏] {message}");
                return;
            }

            try
            {
                ResultText.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
                ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    isError ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Gray
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新状态栏失败: {ex.Message}");
            }
        }

        // 翻页按钮事件
        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage > 1)
            {
                currentPage--;
                UpdatePage();
                UpdateStatusBar($"已切换到第 {currentPage}/{totalPages} 页");
            }
            else
            {
                UpdateStatusBar("已经是第一页了", true);
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage < totalPages)
            {
                currentPage++;
                UpdatePage();
                UpdateStatusBar($"已切换到第 {currentPage}/{totalPages} 页");
            }
            else
            {
                UpdateStatusBar("已经是最后一页了", true);
            }
        }

        /// <summary>
        /// 批量购买事件
        /// </summary>
        private async void BatchPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoggedIn)
            {
                UpdateStatusBar("请先登录账户", true);
                return;
            }

            var selected = ResultList.SelectedItems;
            if (selected == null || selected.Count == 0)
            {
                UpdateStatusBar("请先选择要购买的免费项目", true);
                return;
            }

            BatchPurchaseButton.IsEnabled = false;
            int success = 0,
                fail = 0,
                skip = 0;
            List<string> failedOwnedNames = new List<string>();
            UpdateStatusBar($"开始购买 {selected.Count} 个应用...");

            foreach (var item in selected)
            {
                if (item is AppResult app)
                {
                    if (app.purchased == "已购买" || app.purchased == "已拥有")
                    {
                        skip++;
                        continue;
                    }

                    if (app.price != "0")
                    {
                        skip++;
                        UpdateStatusBar($"跳过付费应用: {app.name}");
                        continue;
                    }

                    UpdateStatusBar($"正在购买: {app.name}...");

                    var result = ipatoolExecution.purchaseApp(app.name ?? string.Empty, KeychainConfig.GetSecretKey(_email ?? string.Empty));

                    if (
                        (result.Contains("success") && result.Contains("true"))
                        || result.Contains("购买成功")
                    )
                    {
                        success++;
                        app.purchased = "已购买";
                        PurchasedAppDb.SavePurchasedApp(
                            app.bundleID ?? "",
                            app.name ?? "",
                            app.version ?? "",
                            "已购买"
                        );
                        UpdateStatusBar($"成功购买: {app.name}");
                    }
                    else
                    {
                        fail++;
                        // 购买失败但为免费应用，视为已购买
                        if (app.price == "0")
                        {
                            app.purchased = "已拥有";
                            PurchasedAppDb.SavePurchasedApp(
                                app.bundleID ?? "",
                                app.name ?? "",
                                app.version ?? "",
                                "已拥有"
                            );
                            failedOwnedNames.Add(app.name ?? "");
                            UpdateStatusBar($"购买失败但已拥有: {app.name}", true);
                        }
                        else
                        {
                            UpdateStatusBar($"购买失败: {app.name}", true);
                        }
                    }
                }
            }

            string extra =
                failedOwnedNames.Count > 0
                    ? $"，购买失败但已拥有: {skip + failedOwnedNames.Count} 个"
                    : "";
            UpdateStatusBar($"批量购买完成 - 成功: {success}, 失败: {fail}, 跳过: {skip}{extra}");
            BatchPurchaseButton.IsEnabled = true;
            RefreshPurchasedStatus();
        }

        /// <summary>
        /// 刷新已购买状态
        /// </summary>
        private void RefreshPurchasedStatus()
        {
            // 使用 bundleID -> status 的映射，保留数据库中的 status 信息
            var purchasedDict = PurchasedAppDb
                .GetPurchasedApps()
                .ToDictionary(x => x.bundleID, x => x.status);

            foreach (var app in allResults)
            {
                var key = app.bundleID ?? string.Empty;
                if (purchasedDict.TryGetValue(key, out var status))
                {
                    // 数据库中的语义可能是 "已拥有"，但 UI 需求是显示为 "已购买"
                    app.purchased = MapDbStatusToUi(status);
                }
            }
            UpdatePage();
        }

        // 将数据库中的 status 映射为界面上显示的状态
        private string MapDbStatusToUi(string? dbStatus)
        {
            if (string.IsNullOrEmpty(dbStatus))
                return "已拥有"; // 兼容旧数据或空值

            // 按照你的要求：数据库中保存为 "已拥有"，但界面上需要显示为 "已购买"
            if (dbStatus == "已拥有")
                return "已拥有";
            return dbStatus;
        }

        /// <summary>
        /// 分页更新刷新
        /// </summary>
        private void UpdatePage()
        {
            // 根据筛选状态决定显示的数据源
            List<AppResult> displayList = allResults.ToList();

            switch (selectedValue)
            {
                default:
                    displayList = allResults.ToList();
                    break;
                case "OnlyPurchased":
                    displayList = allResults.Where(a => a.purchased == "已购买").ToList();
                    break;
                case "OnlyNotPurchased":
                    displayList = allResults.Where(a => a.purchased == "未购买").ToList();
                    break;
                case "OnlyHad":
                    displayList = allResults.Where(a => a.purchased == "已拥有").ToList();
                    break;
            }

            if (displayList.Count == 0)
            {
                if (PrevPageButton != null)
                    PrevPageButton.IsEnabled = false;
                if (NextPageButton != null)
                    NextPageButton.IsEnabled = false;
                return;
            }

            int start = (currentPage - 1) * pageSize;
            int end = System.Math.Min(start + pageSize, displayList.Count);
            ResultList.ItemsSource = displayList.GetRange(start, end - start);

            if (PrevPageButton != null)
                PrevPageButton.IsEnabled = currentPage > 1;
            if (NextPageButton != null)
                NextPageButton.IsEnabled = currentPage < totalPages;
        }

        private void ScreeningComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreeningComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                selectedValue = selectedItem.Tag?.ToString() ?? "All";

                // 重置分页并刷新页面
                currentPage = 1;
                totalPages = (allResults.Count + pageSize - 1) / pageSize;
                UpdatePage();
            }
        }

        /// <summary>
        /// 搜索按钮点击
        /// </summary>
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        /// <summary>
        /// 设置按钮点击
        /// </summary>
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(Settings));
        }

        /// <summary>
        /// 登录/退出按钮点击
        /// </summary>
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (isLoggedIn)
            {
                // 退出登录
                isLoggedIn = false;
                UpdateStatusBar("已退出登录");
                Frame.Navigate(typeof(LoginPage));
            }
            else
            {
                // 前往登录
                UpdateStatusBar("正在跳转到登录页面...");
                Frame.Navigate(typeof(LoginPage));
            }
        }

        /// <summary>
        /// 搜索框按键事件
        /// </summary>
        private async void Search_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                // 阻止默认行为
                e.Handled = true;

                // 移除输入框焦点
                if (sender is Control control)
                {
                    control.IsEnabled = false;
                    control.IsEnabled = true;
                }

                // 触发搜索
                await PerformSearchAsync();
            }
        }

        /// <summary>
        /// 统一的搜索方法
        /// </summary>
        private async Task PerformSearchAsync()
        {
            if (!isLoggedIn)
            {
                UpdateStatusBar("请先登录账户", true);
                return;
            }

            string appName = AppNameBox.Text.Trim();
            if (string.IsNullOrEmpty(appName))
            {
                UpdateStatusBar("请输入应用名称", true);
                AppNameBox.Focus(FocusState.Programmatic);
                return;
            }

            SearchButton.IsEnabled = false;
            UpdateStatusBar($"正在搜索 \"{appName}\"...");

            var result = ipatoolExecution.searchApp(appName, SearchLimitNum, KeychainConfig.GetSecretKey(_email ?? string.Empty));
            SearchButton.IsEnabled = true;

            allResults.Clear();
            int successCount = 0,
                failCount = 0;
            // 使用 bundleID -> status 的映射，以便区分已拥有/已购买
            var purchasedDict = PurchasedAppDb
                .GetPurchasedApps()
                .ToDictionary(x => x.bundleID, x => x.status);

            try
            {
                var root = System.Text.Json.JsonDocument.Parse(result).RootElement;
                if (
                    root.TryGetProperty("apps", out var apps)
                    && apps.ValueKind == System.Text.Json.JsonValueKind.Array
                )
                {
                    int appsArrayCount = apps.GetArrayLength();
                    Debug.WriteLine($"JSON apps array length: {appsArrayCount}");
                    foreach (var obj in apps.EnumerateArray())
                    {
                        try
                        {
                            var bundleId =
                                obj.TryGetProperty("bundleID", out var bid) ? bid.GetString()
                                : obj.TryGetProperty("bundleId", out var bid2) ? bid2.GetString()
                                : "";
                            var app = new AppResult
                            {
                                bundleID = bundleId,
                                id = obj.TryGetProperty("id", out var idv) ? idv.GetRawText() : "",
                                name = obj.TryGetProperty("name", out var namev)
                                    ? namev.GetString()
                                    : "",
                                price = obj.TryGetProperty("price", out var pricev)
                                    ? pricev.GetRawText()
                                    : "",
                                version = obj.TryGetProperty("version", out var ver)
                                    ? ver.GetString()
                                    : "",
                                purchased = purchasedDict.TryGetValue(bundleId ?? string.Empty, out var stat)
                                    ? (string.IsNullOrEmpty(stat) ? "已购买" : stat)
                                    : "可购买",
                            };
                            allResults.Add(app);
                            successCount++;
                        }
                        catch
                        {
                            failCount++;
                        }
                    }
                }
                else
                {
                    UpdateStatusBar("搜索结果为空或格式错误", true);
                    return;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusBar($"解析搜索结果失败: {ex.Message}", true);
                return;
            }

            totalPages = (allResults.Count + pageSize - 1) / pageSize;
            currentPage = 1;
            UpdatePage();
            // 记录实际返回数，便于诊断 ipatool 是否按请求返回了足够数量
            Debug.WriteLine($"Search requested {SearchLimitNum}, returned {allResults.Count}");
            UpdateStatusBar(
                $"搜索完成 - 请求: {SearchLimitNum}, 找到 {allResults.Count} 个应用 (成功: {successCount}, 失败: {failCount})"
            );
        }

        /// <summary>
        /// 当页面通过导航到达时调用
        /// </summary>
        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 检查是否有传递的登录状态
            if (e.Parameter is bool loginStatus)
            {
                isLoggedIn = loginStatus;
                if (isLoggedIn)
                {
                    UpdateStatusBar("登录成功，欢迎使用");
                }
                UpdateLoginButton();
            }
            else if (!isPageLoaded)
            {
                // 如果页面还没加载，检查登录状态
                await CheckLoginStatus();
                UpdateLoginButton();
            }
        }
    }
}