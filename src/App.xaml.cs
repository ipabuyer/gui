using IPAbuyer.Common;
using IPAbuyer.Data;
using IPAbuyer.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.IO;
using WinRT.Interop;

namespace IPAbuyer
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            try
            {
                // 初始化数据库
                PurchasedAppDb.InitDb();
                KeychainConfig.InitializeDatabase();

                this.InitializeComponent();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动错误: {ex.Message}");
                throw;
            }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _window = new Window
                {
                    Title = "IPAbuyer - 快速购买AppStore中的应用",
                };

                SetWindowIcon(_window);

                // 创建 Frame 用于页面导航
                Frame rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;

                // 导航到主页
                rootFrame.Navigate(typeof(MainPage));

                // 将 Frame 设置为窗口内容
                _window.Content = rootFrame;

                // 激活窗口
                _window.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动错误: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 导航失败时的处理
        /// </summary>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception($"Failed to load Page {e.SourcePageType.FullName}");
        }

        private static void SetWindowIcon(Window window)
        {
            try
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon.ico");
                if (File.Exists(iconPath))
                {
                    appWindow?.SetIcon(iconPath);
                }
                else
                {
                    Debug.WriteLine($"App icon not found at {iconPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set window icon: {ex.Message}");
            }
        }
    }
}
