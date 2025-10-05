using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace IPAbuyer.Views
{
    public sealed partial class LoginPage : Page
    {
        private string email;
        private string password;
        private const string keychainPassphrase = "12345678";

        public LoginPage()
        {
            this.InitializeComponent();
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            email = EmailBox.Text.Trim();
            password = PasswordBox.Password;
            ResultText.Text = "";

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ResultText.Text = "邮箱和密码不能为空";
                return;
            }

            NextButton.IsEnabled = false;
            EmailBox.IsEnabled = false;
            PasswordBox.IsEnabled = false;

            // 第一次调用命令
            string cmd = $"./ipatool.exe auth login --email {email} --password {password} --keychain-passphrase {keychainPassphrase} --non-interactive";
            var result = await RunCommandAsync(cmd);

            if (result.Contains("success=true") || result.Contains("[36msuccess=[0mtrue")||(result.Contains("success")&&result.Contains("success") ))
            {
                // 登录成功，跳转到 MainPage
                    Frame.Navigate(typeof(SearchPage));
                return;
            }
            if (result.Contains("请输入验证码") || result.Contains("auth code") || result.Contains("2FA code is required"))
            {
                CodePanel.Visibility = Visibility.Visible;
            }
            else
            {
                ResultText.Text = result;
                NextButton.IsEnabled = true;
                EmailBox.IsEnabled = true;
                PasswordBox.IsEnabled = true;
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string code = CodeBox.Text.Trim();
            if (code.Length != 6)
            {
                ResultText.Text = "验证码必须为6位";
                return;
            }

            LoginButton.IsEnabled = false;

            string cmd = $"./ipatool.exe auth login --email {email} --password {password} --keychain-passphrase {keychainPassphrase} --non-interactive --auth-code {code}";
            var result = await RunCommandAsync(cmd);

            ResultText.Text = result;
            LoginButton.IsEnabled = true;
            // 判断登录成功
            if (result.Contains("success=true") || result.Contains("[36msuccess=[0mtrue")||(result.Contains("success")&&result.Contains("success") ))
            {
                // 跳转到 MainPage
                    Frame.Navigate(typeof(SearchPage));
            }
        }

        private async Task<string> RunCommandAsync(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(psi))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    return string.IsNullOrEmpty(error) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }
    }
}