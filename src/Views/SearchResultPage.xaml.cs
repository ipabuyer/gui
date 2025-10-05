using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace IPAbuyer.Views
{
    public sealed partial class SearchResultPage : Page
    {
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPurchasedStatus();
        }

        private void RefreshPurchasedStatus()
        {
            // 重新检测已购买状态
            var purchasedApps = IPAbuyer.Data.PurchasedAppDb.GetPurchasedApps();
            var purchasedBundleIDs = new HashSet<string>(purchasedApps.Select(x => x.bundleID));
            foreach (var app in allResults)
            {
                app.purchased = purchasedBundleIDs.Contains(app.bundleID) ? "已购买" : "可购买";
            }
            UpdatePage();
        }
        private async void BatchPurchaseButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResultList.SelectedItems;
            if (selected == null || selected.Count == 0)
            {
                await ShowDialogAsync("请先选择要批量购买的可购买项。");
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
                        IPAbuyer.Data.PurchasedAppDb.SavePurchasedApp(app.bundleID, app.name, app.version);
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
            await ShowDialogAsync($"批量购买完成：成功{success}，失败{fail}，跳过{skip}");
            RefreshPurchasedStatus();
        }

        private async Task ShowDialogAsync(string msg)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = msg,
                CloseButtonText = "确定"
            };
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
        }

        async void ResultRow_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is AppResult app)
            {
                if (app.price != "0")
                {
                    ShowDialog("购买失败，价格不为0");
                    return;
                }
                string cmd = $"./ipatool.exe purchase --keychain-passphrase 12345678 --bundle-identifier {app.bundleID}";
                string result = await RunCommandAsync(cmd);
                if (result.Contains("success") && result.Contains("false"))
                {
                    // 保存已购买商品到数据库
                    IPAbuyer.Data.PurchasedAppDb.SavePurchasedApp(app.bundleID, app.name, app.version);
                    ShowDialog("购买失败，已购买该商品");
                }
                else if ((result.Contains("success") && result.Contains("true")) || result.Contains("购买成功"))
                {
                    // 保存购买记录到数据库
                    IPAbuyer.Data.PurchasedAppDb.SavePurchasedApp(app.bundleID, app.name, app.version);
                    ShowDialog("购买成功");
                }
                else
                {
                    ShowDialog("购买失败");
                }
            }
        }

        async Task<string> RunCommandAsync(string command)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    return string.IsNullOrEmpty(error) ? output : error;
                }
            }
            catch (System.Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }

        async void ShowDialog(string msg)
        {
            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = msg,
                CloseButtonText = "确定"
            };
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
        }
        public SearchResultPage()
        {
            this.InitializeComponent();
        }

        // 查询结果模型
        public class AppResult
        {
            public string bundleID { get; set; }
            public string id { get; set; }
            public string name { get; set; }
            public string price { get; set; }
            public string version { get; set; }
            public string purchased { get; set; } // 新增字段
        }

        private List<AppResult> allResults = new List<AppResult>();
        private int currentPage = 1;
        private int pageSize = 15;
        private int totalPages = 1;

        public void SetResults(List<string> results)
        {
            allResults.Clear();
            // 获取已购买bundleID列表
            var purchasedApps = IPAbuyer.Data.PurchasedAppDb.GetPurchasedApps();
            var purchasedBundleIDs = new HashSet<string>(purchasedApps.Select(x => x.bundleID));
            foreach (var line in results)
            {
                var app = new AppResult
                {
                    bundleID = GetValue(line, "\"bundleID\":\"", "\""),
                    id = GetValue(line, "\"id\":", ","),
                    name = GetValue(line, "\"name\":\"", "\""),
                    price = GetPriceValue(line),
                    version = GetValue(line, "\"version\":\"", "\"")
                };
                app.purchased = purchasedBundleIDs.Contains(app.bundleID) ? "已购买" : "可购买";
                allResults.Add(app);
            }
            totalPages = (allResults.Count + pageSize - 1) / pageSize;
            currentPage = 1;
            UpdatePage();
        }

        private void UpdatePage()
        {
            int start = (currentPage - 1) * pageSize;
            int end = System.Math.Min(start + pageSize, allResults.Count);
            ResultList.ItemsSource = allResults.GetRange(start, end - start);
            // 只更新上下页按钮
            PrevPageButton.IsEnabled = currentPage > 1;
            NextPageButton.IsEnabled = currentPage < totalPages;
        }

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

        private string GetValue(string src, string start, string end)
        {
            int i1 = src.IndexOf(start);
            if (i1 < 0) return "";
            i1 += start.Length;
            int i2 = src.IndexOf(end, i1);
            if (i2 < 0) return src.Substring(i1);
            return src.Substring(i1, i2 - i1);
        }
        private string GetPriceValue(string src)
        {
            string val = GetValue(src, "\"price\":", ",");
            if (val.EndsWith("}")) val = val.Substring(0, val.Length - 1);
            return val.Trim();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SearchPage));
        }
        
    }
}
