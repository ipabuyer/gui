using IPAbuyer.Data;
using IPAbuyer.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace IPAbuyer
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            // 初始化数据库
            PurchasedAppDb.InitDb();
            KeychainConfig.InitializeDatabase();
            
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new Window
            {
                Title = "IPAbuyer"
            };

            // 创建 Frame 用于页面导航
            Frame rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;

            // 导航到登录页
            rootFrame.Navigate(typeof(MainPage));

            // 将 Frame 设置为窗口内容
            _window.Content = rootFrame;

            // 激活窗口
            _window.Activate();
        }

        /// <summary>
        /// 导航失败时的处理
        /// </summary>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception($"Failed to load Page {e.SourcePageType.FullName}");
        }
    }
}
