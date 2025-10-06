using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class SearchPage : Page
    {
        // 搜索范围属性
        public int SearchLimitNum { get; set; } = 5;

        private void SearchLimit_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SearchLimitNum = (int)e.NewValue;
            var tb = this.FindName("SearchLimitNumText") as TextBlock;
            if (tb != null) tb.Text = SearchLimitNum.ToString();
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

        // 分页按钮事件
        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage > 1)
            {
                currentPage--;
                UpdatePage();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPage < totalPages)
            {
                currentPage++;
                UpdatePage();
            }
        }

        // 批量购买事件
        private async void BatchPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResultList.SelectedItems;
            if (selected == null || selected.Count == 0)
            {
                ResultText.Text = "请先选择要批量购买的可购买项。";
                return;
            }
            int success = 0, fail = 0, skip = 0;
            foreach (var item in selected)
            {
                if (item is AppResult app && app.purchased == "可购买" && app.price == "0")
                {
                    string cmd = $"./ipatool.exe purchase --keychain-passphrase 12345678 --bundle-identifier {app.bundleID}";
                    string result = await RunCommandAsync(cmd);
                    if ((result.Contains("success") && result.Contains("true")) || result.Contains("购买成功"))
                    {
                        success++;
                    }
                    else
                    {
                        fail++;
                    }
                }
                else
                {
                    skip++;
                }
            }
            ResultText.Text = $"批量购买完成：成功{success}，失败{fail}，跳过{skip}";
            RefreshPurchasedStatus();
        }

        // 刷新已购买状态
        private void RefreshPurchasedStatus()
        {
            // 重新检测已购买状态（如有本地数据库可集成）
            foreach (var app in allResults)
            {
                // 示例：假设purchased字段已正确设置
                // 可根据实际业务从数据库查询已购bundleID
            }
            UpdatePage();
        }

        // 分页数据刷新
        private void UpdatePage()
        {
            int start = (currentPage - 1) * pageSize;
            int end = System.Math.Min(start + pageSize, allResults.Count);
            ResultList.ItemsSource = allResults.GetRange(start, end - start);
        }
        private const string keychainPassphrase = "12345678";
        public SearchPage()
        {
            this.InitializeComponent();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string appName = AppNameBox.Text.Trim();
            if (string.IsNullOrEmpty(appName))
            {
                ResultText.Text = "请输入APP名称";
                return;
            }
            SearchButton.IsEnabled = false;
            string cmd = $"./ipatool.exe search --keychain-passphrase {keychainPassphrase} {appName} --limit {SearchLimitNum} --non-interactive --format json";
            var result = await RunCommandAsync(cmd);
            SearchButton.IsEnabled = true;

            // 直接解析JSON对象，遍历apps数组
            this.allResults.Clear();
            int successCount = 0, failCount = 0;
            // 获取已购历史 bundleID 列表
            var purchasedList = IPAbuyer.Data.PurchasedAppDb.GetPurchasedApps().Select(x => x.bundleID).ToHashSet();
            try
            {
                var root = System.Text.Json.JsonDocument.Parse(result).RootElement;
                if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var obj in apps.EnumerateArray())
                    {
                        try
                        {
                            var bundleId = obj.TryGetProperty("bundleID", out var bid) ? bid.GetString() : obj.TryGetProperty("bundleId", out var bid2) ? bid2.GetString() : "";
                            var app = new AppResult
                            {
                                bundleID = bundleId,
                                id = obj.TryGetProperty("id", out var idv) ? idv.GetRawText() : "",
                                name = obj.TryGetProperty("name", out var namev) ? namev.GetString() : "",
                                price = obj.TryGetProperty("price", out var pricev) ? pricev.GetRawText() : "",
                                version = obj.TryGetProperty("version", out var ver) ? ver.GetString() : "",
                                purchased = purchasedList.Contains(bundleId) ? "已购" : "可购买"
                            };
                            this.allResults.Add(app);
                            successCount++;
                        }
                        catch { failCount++; }
                    }
                }
            }
            catch { failCount++; }
            // 计算总页数
            totalPages = (allResults.Count + pageSize - 1) / pageSize;
            currentPage = 1;
            UpdatePage();
            ResultText.Text = $"解析成功 {successCount} 条，失败 {failCount} 条，总数 {allResults.Count}";
        }

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
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                using (var process = Process.Start(psi))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    return string.IsNullOrEmpty(error) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PurchasedListPage));
        }
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            IPAbuyer.Data.AccountHistoryDb.SetLogoutFlag();
            Frame.Navigate(typeof(LoginPage));
        }
        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatePage();
            //Frame.Navigate(typeof(SettingPage));
        }
    }
}
