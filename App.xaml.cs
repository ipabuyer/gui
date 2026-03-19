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
        public Window? MainWindowInstance => _window;

        // 构造函数
        public App()
        {
            try
            {
                // 初始化数据库
                PurchasedAppDb.InitDb();
                // KeychainConfig 改为文件配置（无 KeychainConfig.db），保留初始化入口用于创建默认配置文件。
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
                _window = new MainWindow();
                // 激活窗口
                _window.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动错误: {ex.Message}");
                throw;
            }
        }
    }
}
