using System.Diagnostics;
using IPAbuyer.Data;
using IPAbuyer.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace IPAbuyer.Views
{
    public sealed partial class MainPage : Page
    {
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
            UpdateStatusBar($"已调整搜索限制为 {SearchLimitNum} 个结果");
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
        private int pageSize = 15;
        private int totalPages = 1;
        private bool isLoggedIn = false;
        private bool isPageLoaded = false; // 添加页面加载标记
        private const string keychainPassphrase = "12345678";

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
        }

        /// <summary>
        /// 页面加载时检查登录状态
        /// </summary>
        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            isPageLoaded = true; // 标记页面已加载
            await CheckLoginStatus();
            UpdateLoginButton();
        }

        /// <summary>
        /// 检查登录状态
        /// </summary>
        private async Task CheckLoginStatus()
        {
            UpdateStatusBar("正在检查登录状态...");

            // 检查是否有退出标记
            if (AccountHistoryDb.IsLogoutFlag())
            {
                isLoggedIn = false;
                UpdateStatusBar("未登录,请先登录账户");
                return;
            }

            // 尝试验证登录状态
            try
            {
                string cmd =
                    $"./ipatool.exe search --keychain-passphrase {keychainPassphrase} test --limit 1 --non-interactive";
                var result = await RunCommandAsync(cmd);

                if (result.Contains("not logged in") || result.Contains("未登录"))
                {
                    isLoggedIn = false;
                    UpdateStatusBar("登录已过期,请重新登录");
                }
                else if (result.Contains("apps") || result.Contains("success"))
                {
                    isLoggedIn = true;
                    var accounts = AccountHistoryDb.GetAccounts();
                    var lastAccount = accounts.LastOrDefault();
                    var email = lastAccount.email ?? "未知账户";
                    UpdateStatusBar($"已登录账户: {email}");
                }
                else
                {
                    isLoggedIn = false;
                    UpdateStatusBar("无法验证登录状态,请尝试重新登录");
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
            if (LogoutButton == null)
                return; // 添加空值检查

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
        /// 更新状态栏信息（带空值检查）
        /// </summary>
        private void UpdateStatusBar(string message, bool isError = false)
        {
            if (ResultText == null || !isPageLoaded)
            {
                Debug.WriteLine($"[状态栏] {message}"); // 如果控件未加载，输出到调试窗口
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
                    string cmd =
                        $"./ipatool.exe purchase --keychain-passphrase {keychainPassphrase} --bundle-identifier {app.bundleID}";
                    string result = await RunCommandAsync(cmd);

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
                            app.version ?? ""
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
                                app.version ?? ""
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
                    ? $"，购买失败但已拥有: {failedOwnedNames.Count} 个"
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
            var purchasedList = PurchasedAppDb
                .GetPurchasedApps()
                .Select(x => x.bundleID)
                .ToHashSet();
            foreach (var app in allResults)
            {
                if (purchasedList.Contains(app.bundleID))
                {
                    app.purchased = "已购买";
                }
            }
            UpdatePage();
        }

        /// <summary>
        /// 分页更新刷新
        /// </summary>
        private void UpdatePage()
        {
            if (allResults.Count == 0)
            {
                ResultList.ItemsSource = null;
                if (PrevPageButton != null)
                    PrevPageButton.IsEnabled = false;
                if (NextPageButton != null)
                    NextPageButton.IsEnabled = false;
                return;
            }

            int start = (currentPage - 1) * pageSize;
            int end = System.Math.Min(start + pageSize, allResults.Count);
            ResultList.ItemsSource = allResults.GetRange(start, end - start);

            if (PrevPageButton != null)
                PrevPageButton.IsEnabled = currentPage > 1;
            if (NextPageButton != null)
                NextPageButton.IsEnabled = currentPage < totalPages;
        }

        /// <summary>
        /// 搜索按钮点击
        /// </summary>
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        /// <summary>
        /// 执行命令
        /// </summary>
        private async Task<string> RunCommandAsync(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        return "无法启动进程";
                    }

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    return string.IsNullOrEmpty(error) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
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
                AccountHistoryDb.SetLogoutFlag();
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
        /// 下载按钮点击
        /// </summary>
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoggedIn)
            {
                UpdateStatusBar("请先登录账户", true);
                return;
            }

            var selected = ResultList.SelectedItems;
            if (selected == null || selected.Count == 0)
            {
                UpdateStatusBar("请先选择要下载的应用", true);
                return;
            }

            UpdateStatusBar($"下载功能开发中,已选择 {selected.Count} 个应用");
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

            string cmd =
                $"./ipatool.exe search --keychain-passphrase {keychainPassphrase} {appName} --limit {SearchLimitNum} --non-interactive --format json";
            var result = await RunCommandAsync(cmd);
            SearchButton.IsEnabled = true;

            allResults.Clear();
            int successCount = 0,
                failCount = 0;
            var purchasedList = PurchasedAppDb
                .GetPurchasedApps()
                .Select(x => x.bundleID)
                .ToHashSet();

            try
            {
                var root = System.Text.Json.JsonDocument.Parse(result).RootElement;
                if (
                    root.TryGetProperty("apps", out var apps)
                    && apps.ValueKind == System.Text.Json.JsonValueKind.Array
                )
                {
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
                                purchased = purchasedList.Contains(bundleId) ? "已购买" : "可购买",
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
            UpdateStatusBar(
                $"搜索完成 - 找到 {allResults.Count} 个应用 (成功: {successCount}, 失败: {failCount})"
            );
        }
    }
}
