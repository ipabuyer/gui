using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Page
    {
        private const string FaqFileName = "Data" + "/" + "SettingsFaq.json";

        public ObservableCollection<FaqItem> FaqItems { get; } = new();

        public Settings()
        {
            this.InitializeComponent();
            DataContext = this;
            Loaded += Settings_Loaded;
        }

        private void OpenAppleAccountLink(object sender, RoutedEventArgs e)
        {
            const string url = "https://account.apple.com";
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        // 返回首页
        private void BackToMainpage(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        // 明暗模式 ComboBox 改变时触发(已删除)
        // 跳转到开发者官网
        private void GithubButton(object sender, RoutedEventArgs e)
        {
            var url = "https://github.com/ipabuyer/";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        // 清除本地数据库
        private async void DeleteDataBase(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "确认操作",
                Content = "确定要删除本地所有已购买记录吗？此操作不可恢复！",
                PrimaryButtonText = "确认",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    IPAbuyer.Data.PurchasedAppDb.ClearPurchasedApps();
                    var successDialog = new ContentDialog
                    {
                        Title = "操作成功",
                        Content = "本地已购记录已清除。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "错误",
                        Content = $"清除失败：{ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }

        private async void Settings_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= Settings_Loaded;
            await LoadFaqItemsAsync();
        }

        private async Task LoadFaqItemsAsync()
        {
            try
            {
                string faqPath = Path.Combine(AppContext.BaseDirectory, FaqFileName);

                if (!File.Exists(faqPath))
                {
                    ShowFaqStatus($"未找到问答文件: {FaqFileName}");
                    return;
                }

                await using FileStream stream = File.OpenRead(faqPath);
                var items = await JsonSerializer.DeserializeAsync<List<FaqItem>>(stream, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                FaqItems.Clear();

                if (items is { Count: > 0 })
                {
                    foreach (var item in items)
                    {
                        if (item is { Question: { Length: > 0 }, Answer: { Length: > 0 } })
                        {
                            FaqItems.Add(item);
                        }
                    }

                    if (FaqItems.Count == 0)
                    {
                        ShowFaqStatus("问答文件中没有有效的条目。");
                    }
                    else
                    {
                        HideFaqStatus();
                    }
                }
                else
                {
                    ShowFaqStatus("问答文件为空。");
                }
            }
            catch (Exception ex)
            {
                ShowFaqStatus($"无法加载问答: {ex.Message}");
            }
        }

        private void ShowFaqStatus(string message)
        {
            if (FaqStatusTextBlock != null)
            {
                FaqStatusTextBlock.Text = message;
                FaqStatusTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void HideFaqStatus()
        {
            if (FaqStatusTextBlock != null)
            {
                FaqStatusTextBlock.Text = string.Empty;
                FaqStatusTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        public sealed class FaqItem
        {
            public string Question { get; set; } = string.Empty;
            public string Answer { get; set; } = string.Empty;
        }

        private TextBlock? FaqStatusTextBlock => GetControl<TextBlock>("FaqStatusText");

        private T? GetControl<T>(string name)
            where T : class
        {
            return FindName(name) as T;
        }
    }
}
