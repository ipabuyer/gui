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

            // 让内容延伸到标题栏（WinUI 3/UWP）
            if (this is Microsoft.UI.Xaml.Window win)
            {
                // WinUI 3
                win.ExtendsContentIntoTitleBar = true;
            }

            // 设置窗口标题和图标（自定义区域）
            WindowTitle.Text = "IPAbuyer - 快速购买 AppStore 中的应用";
            WindowIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Icon.ico"));
            // 仍设置系统窗口标题和图标，兼容任务栏等
            this.Title = "IPAbuyer - 快速购买 AppStore 中的应用";
            SetWindowIcon(this);

            // 默认显示主页
            ContentFrame.Navigate(typeof(MainPage));
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
        // 展开按钮点击事件
        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

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
