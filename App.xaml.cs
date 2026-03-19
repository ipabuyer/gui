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
using System.Threading.Tasks;
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
                _ = WarmupAuthInfoAsync();
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

        private static async Task WarmupAuthInfoAsync()
        {
            try
            {
                var result = await IpatoolExecution.AuthInfoAsync(
                    passphrase: null,
                    silent: true).ConfigureAwait(false);
                string account = IpatoolExecution.ExtractEmailFromPayload(result.OutputOrError);
                bool isAuthSuccess = result.IsSuccessResponse
                    && (IpatoolExecution.IsPayloadSuccess(result.OutputOrError) || !string.IsNullOrWhiteSpace(account));
                if (!isAuthSuccess)
                {
                    return;
                }

                SessionState.SetLoginState(account, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"静默查询登录状态失败: {ex.Message}");
            }
        }
    }
}
