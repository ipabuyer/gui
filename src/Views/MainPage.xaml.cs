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
        /// <summary>
        /// 下载按钮点击事件（仅下载已购买应用）
        /// </summary>
        /*
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
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

            // 禁用相关按钮防止重复操作
            DownloadButton.IsEnabled = false;
            BatchPurchaseButton.IsEnabled = false;
            SearchButton.IsEnabled = false;

            int success = 0,
                fail = 0,
                skip = 0;
            List<string> failedNames = new List<string>();
            List<string> successNames = new List<string>();
            List<string> skipNames = new List<string>();

            UpdateStatusBar($"检查选中应用的购买状态...");

            // 创建进度对话框
            var progressDialog = new ContentDialog
            {
                Title = "下载进度",
                Content = $"正在检查 {selected.Count} 个应用的购买状态...",
                PrimaryButtonText = "取消下载",
                XamlRoot = this.XamlRoot,
            };

            bool isCancelled = false;
            progressDialog.PrimaryButtonClick += (s, args) =>
            {
                isCancelled = true;
                UpdateStatusBar("用户取消了下载", true);
            };

            // 显示进度对话框
            _ = progressDialog.ShowAsync();

            try
            {
                // 获取已购买应用列表
                var purchasedList = PurchasedAppDb
                    .GetPurchasedApps()
                    .Select(x => x.bundleID)
                    .ToHashSet();

                // 筛选已购买的应用
                var purchasedApps = selected
                    .OfType<AppResult>()
                    .Where(app => purchasedList.Contains(app.bundleID))
                    .ToList();

                if (purchasedApps.Count == 0)
                {
                    progressDialog.Hide();
                    UpdateStatusBar("选中的应用中没有已购买的应用", true);

                    var dialog = new ContentDialog
                    {
                        Title = "无法下载",
                        Content = "选中的应用中没有已购买的应用，请先购买后再下载。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot,
                    };
                    await dialog.ShowAsync();

                    // 重新启用按钮
                    DownloadButton.IsEnabled = true;
                    BatchPurchaseButton.IsEnabled = true;
                    SearchButton.IsEnabled = true;
                    return;
                }

                // 记录跳过的应用（未购买）
                var skippedApps = selected
                    .OfType<AppResult>()
                    .Where(app => !purchasedList.Contains(app.bundleID))
                    .ToList();
                skip = skippedApps.Count;
                skipNames = skippedApps.Select(app => app.name ?? "未知应用").ToList();

                UpdateStatusBar($"开始下载 {purchasedApps.Count} 个已购买应用...");
                progressDialog.Content = $"准备下载 {purchasedApps.Count} 个已购买应用...";

                int currentIndex = 0;
                foreach (var app in purchasedApps)
                {
                    if (isCancelled)
                        break;

                    currentIndex++;

                    // 更新进度对话框
                    progressDialog.Content =
                        $"正在下载 ({currentIndex}/{purchasedApps.Count}): {app.name}";

                    // 再次确认购买状态（防止数据不一致）
                    if (!purchasedList.Contains(app.bundleID))
                    {
                        skip++;
                        skipNames.Add(app.name ?? "");
                        UpdateStatusBar($"跳过未购买的应用: {app.name}", true);
                        continue;
                    }

                    // 检查必要的参数
                    if (string.IsNullOrEmpty(app.id) || string.IsNullOrEmpty(app.bundleID))
                    {
                        skip++;
                        skipNames.Add(app.name ?? "");
                        UpdateStatusBar($"跳过参数不全的应用: {app.name}", true);
                        continue;
                    }

                    UpdateStatusBar(
                        $"正在下载 ({currentIndex}/{purchasedApps.Count}): {app.name}..."
                    );

                    // 构建下载命令
                    string cmd =
                        $"./ipatool download --keychain-passphrase {keychainPassphrase} --app-id {app.id} --bundle-identifier {app.bundleID} --format json --non-interactive --verbose";
                    string result = await RunCommandAsync(cmd);

                    // 解析下载结果
                    if (IsDownloadSuccess(result))
                    {
                        success++;
                        successNames.Add(app.name ?? "");
                        UpdateStatusBar(
                            $"成功下载 ({currentIndex}/{purchasedApps.Count}): {app.name}"
                        );

                        // 保存下载记录
                        //SaveDownloadRecord(app);
                    }
                    else
                    {
                        fail++;
                        failedNames.Add(app.name ?? "");
                        UpdateStatusBar(
                            $"下载失败 ({currentIndex}/{purchasedApps.Count}): {app.name}",
                            true
                        );

                        // 记录详细的错误信息
                        Debug.WriteLine(
                            $"下载失败详情 - 应用: {app.name}, ID: {app.id}, BundleID: {app.bundleID}, 错误: {result}"
                        );
                    }

                    // 短暂延迟，避免请求过于频繁
                    if (!isCancelled && currentIndex < purchasedApps.Count)
                    {
                        await Task.Delay(1000);
                    }
                }

                // 显示下载结果摘要
                string successMsg =
                    successNames.Count > 0
                        ? $"成功: {string.Join(", ", successNames.Take(3))}"
                        : "";
                if (successNames.Count > 3)
                    successMsg += "...";

                string failMsg =
                    failedNames.Count > 0 ? $"失败: {string.Join(", ", failedNames.Take(3))}" : "";
                if (failedNames.Count > 3)
                    failMsg += "...";

                string skipMsg =
                    skipNames.Count > 0 ? $"跳过: {string.Join(", ", skipNames.Take(3))}" : "";
                if (skipNames.Count > 3)
                    skipMsg += "...";

                string summary = isCancelled ? "下载已取消 - " : "下载完成 - ";
                summary +=
                    $"选中: {selected.Count}, 已购买: {purchasedApps.Count}, 成功: {success}, 失败: {fail}, 跳过: {skip}";
                
                if (!string.IsNullOrEmpty(successMsg))
                    summary += $"\n{successMsg}";
                if (!string.IsNullOrEmpty(failMsg))
                    summary += $"\n{failMsg}";
                if (!string.IsNullOrEmpty(skipMsg))
                    summary += $"\n{skipMsg}";
                
                UpdateStatusBar(summary);

                // 显示完成对话框
                progressDialog.Hide();
                var completeDialog = new ContentDialog
                {
                    Title = isCancelled ? "下载取消" : "下载完成",
                    Content = summary,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot,
                };
                await completeDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                UpdateStatusBar($"下载过程发生错误: {ex.Message}", true);
                progressDialog.Hide();
            }
            finally
            {
                // 重新启用按钮
                DownloadButton.IsEnabled = true;
                BatchPurchaseButton.IsEnabled = true;
                SearchButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 判断下载是否成功
        /// </summary>
        private bool IsDownloadSuccess(string result)
        {
            // 根据 ipatool 的实际输出调整这些判断条件
            return result.Contains("\"success\":true")
                || result.Contains("download completed")
                || result.Contains("下载完成")
                || result.Contains("successfully downloaded")
                || (result.Contains("success") && !result.Contains("false"))
                || result.Contains(".ipa")
                || // 如果输出中包含ipa文件路径
                result.Contains("file saved"); // 文件保存成功
        }
    */
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
        private void ScreeningComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 你的处理逻辑
        }
    }
}
