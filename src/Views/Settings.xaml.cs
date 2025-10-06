using IPAbuyer.Views;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Window
    {
        public Settings()
        {
            this.InitializeComponent();
        }

        // 明暗模式 ComboBox 改变时触发
        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        // 跳转到开发者官网
        private async void GithubButton(object sender, RoutedEventArgs e)
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

        }
    }
}
