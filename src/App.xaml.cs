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

        // 构造函数
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

        // 应用启动时调用
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _window = new MainWindow()
                {
                    Title = "IPAbuyer - 快速购买 AppStore 中的应用",
                };

                SetWindowIcon(_window);

                // 激活窗口
                _window.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动错误: {ex.Message}");
                throw;
            }
        }

        // 导航失败时的处理
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception($"Failed to load Page {e.SourcePageType.FullName}");
        }

        // 设置窗口图标
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
                    Debug.WriteLine($"应用图标未找到，路径为 {iconPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载图标失败 {ex.Message}");
            }
        }
    }
}
