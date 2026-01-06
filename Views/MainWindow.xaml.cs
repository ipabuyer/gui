using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using IPAbuyer;
using System;

namespace IPAbuyer.Views
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            // 恢复系统标题栏，设置窗口标题和图标
            this.Title = "IPAbuyer - 快速购买 AppStore 中的应用";
            SetWindowIcon(this);

            // 默认显示主页
            ContentFrame.Navigate(typeof(MainPage));
            // 选中主页菜单项
            foreach (var menuItem in NavView.MenuItems)
            {
                if (menuItem is NavigationViewItem nvi && nvi.Tag?.ToString() == "MainPage")
                {
                    NavView.SelectedItem = nvi;
                    break;
                }
            }
        }

        // 设置窗口图标
        private static void SetWindowIcon(Window window)
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    appWindow?.SetIcon(iconPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"应用图标未找到，路径为 {iconPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载图标失败 {ex.Message}");
            }
        }
        // 移除自定义折叠按钮相关事件

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item)
            {
                switch (item.Tag)
                {
                    case "MainPage":
                        ContentFrame.Navigate(typeof(MainPage));
                        break;
                    case "LoginPage":
                        ContentFrame.Navigate(typeof(LoginPage));
                        break;
                    case "SettingsPage":
                        ContentFrame.Navigate(typeof(Settings));
                        break;
                }
            }
        }
    }
}
