using System.Diagnostics;
using System.IO;
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
        private int pageSize = 15;
        private int totalPages = 1;
        private bool isLoggedIn = false;
        private bool isPageLoaded = false;
    private bool showOnlyNotPurchased = false; // 新增：是否仅显示未购买

        private string _ipatoolPath;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            
            // 查找ipatool.exe路径
            _ipatoolPath = FindIpatoolPath();
        }

        /// <summary>
        /// 查找ipatool.exe的路径，优先使用Include文件夹中的
        /// </summary>
        private string FindIpatoolPath()
        {
            try
            {
                // 获取当前应用程序的基础目录
                string baseDirectory = AppContext.BaseDirectory;
                
                // 优先查找项目根目录下的Include文件夹中的ipatool.exe
                string includePath = Path.Combine(baseDirectory, "Include", "ipatool.exe");
                if (File.Exists(includePath))
                {
                    Debug.WriteLine($"找到Include文件夹中的ipatool.exe: {includePath}");
                    return includePath;
                }

                // 如果Include文件夹中没有，查找当前目录下的ipatool.exe
                string currentDirPath = Path.Combine(baseDirectory, "ipatool.exe");
                if (File.Exists(currentDirPath))
                {
                    Debug.WriteLine($"找到当前目录下的ipatool.exe: {currentDirPath}");
                    return currentDirPath;
                }

                // 如果都找不到，返回默认的ipatool.exe（保持原有行为）
                Debug.WriteLine("未找到ipatool.exe，使用默认路径");
                return "ipatool.exe";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"查找ipatool.exe路径时出错: {ex.Message}");
                return "ipatool.exe";
            }
        }

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
                string pass = IPAbuyer.Common.KeychainConfig.GetPassphrase();
                string arguments = $"search --keychain-passphrase {pass} test --limit 1 --non-interactive";
                var result = await RunIpatoolCommandAsync(arguments);

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
                return;

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
                    string pass = IPAbuyer.Common.KeychainConfig.GetPassphrase();
                    string arguments = $"purchase --keychain-passphrase {pass} --bundle-identifier {app.bundleID}";
                    string result = await RunIpatoolCommandAsync(arguments);

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
            // 根据筛选状态决定显示的数据源
            var displayList = showOnlyNotPurchased
                ? allResults.Where(a => a.purchased != "已购买" && a.purchased != "已拥有").ToList()
                : allResults;

            if (displayList.Count == 0)
            {
                ResultList.ItemsSource = null;
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

        private void ScreeningButton_Click(object sender, RoutedEventArgs e)
        {
            // 切换筛选状态
            showOnlyNotPurchased = !showOnlyNotPurchased;

            // 更新按钮外观
            if (ScreeningButton != null)
            {
                ScreeningButton.Content = showOnlyNotPurchased ? "仅未购买 (ON)" : "仅未购买";
                ScreeningButton.Background = showOnlyNotPurchased
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }

            // 重置分页并刷新页面
            currentPage = 1;
            totalPages = (allResults.Count + pageSize - 1) / pageSize;
            UpdatePage();
        }

        /// <summary>
        /// 搜索按钮点击
        /// </summary>
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        /// <summary>
        /// 执行命令行命令 - 修复版本
        /// </summary>
        private async Task<string> RunCommandAsync(string command)
        {
            try
            {
                // 使用cmd.exe而不是powershell，因为cmd对路径中的空格处理更好
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{command}\"",
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

                    return string.IsNullOrWhiteSpace(error) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 执行ipatool命令 - 专门处理路径问题
        /// </summary>
        private async Task<string> RunIpatoolCommandAsync(string arguments)
        {
            try
            {
                // 直接执行ipatool.exe，而不是通过powershell或cmd
                var psi = new ProcessStartInfo
                {
                    FileName = _ipatoolPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    WorkingDirectory = Path.GetDirectoryName(_ipatoolPath) // 设置工作目录为ipatool所在目录
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

                    return string.IsNullOrWhiteSpace(error) ? output : error;
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

            string pass2 = IPAbuyer.Common.KeychainConfig.GetPassphrase();
            string arguments = $"search --keychain-passphrase {pass2} {appName} --limit {SearchLimitNum} --non-interactive --format json";
            var result = await RunIpatoolCommandAsync(arguments);
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

        private void ScreeningComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 你的处理逻辑
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