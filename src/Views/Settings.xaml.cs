using IPAbuyer.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Page
    {
        public Settings()
        {
            this.InitializeComponent();
        }

        // ������ҳ
        private void BackToMainpage(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        // ����ģʽ ComboBox �ı�ʱ����
        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        // ��ת�������߹���
        private async void GithubButton(object sender, RoutedEventArgs e)
        {
            var url = "https://github.com/ipabuyer/";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        // ����������ݿ�
        private async void DeleteDataBase(object sender, RoutedEventArgs e)
        {

        }
    }
}
