using IPAbuyer.Views;

namespace IPAbuyer.Views
{
    public sealed partial class Settings : Window
    {
        public Settings()
        {
            this.InitializeComponent();
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
