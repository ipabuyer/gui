using IPAbuyer.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class LoginPage : Page
    {
        private string _email;
        private string _password;
        private const string KeychainPassphrase = "12345678";

        public LoginPage()
        {
            this.InitializeComponent();
            InitializePage();
        }

        /// <summary>
        /// 初始化页面
        /// </summary>
        private void InitializePage()
        {
            // 加载账号历史
            LoadAccountHistory();
        }

        /// <summary>
        /// 加载账号历史记录
        /// </summary>
        private void LoadAccountHistory()
        {
            try
            {
                var accounts = AccountHistoryDb.GetAccounts();
                if (accounts != null && accounts.Any())
                {
                    // 获取最后一个账号（最近使用的）
                    var lastAccount = accounts.LastOrDefault();
                    if (!string.IsNullOrEmpty(lastAccount.email))
                    {
                        EmailComboBox.Text = lastAccount.email;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载账号历史失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 登录按钮点击事件
        /// </summary>
        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // 获取输入的邮箱和密码
            _email = EmailComboBox.Text.Trim();
            _password = PasswordBox.Password;

            // 清空结果提示
            ResultText.Text = "";

            // 验证输入
            if (!ValidateInput(_email, _password))
            {
                ResultText.Text = "邮箱和密码不能为空";
                ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red);
                return;
            }

            // 禁用输入控件
            SetInputControlsEnabled(false);
            NextButton.IsEnabled = false;
            ResultText.Text = "正在登录...";
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray);

            try
            {
                // 执行登录命令
                string cmd = $"./ipatool.exe auth login --email {_email} --password \"{_password}\" --keychain-passphrase {KeychainPassphrase} --non-interactive";
                var result = await RunCommandAsync(cmd);

                // 处理登录结果
                await HandleLoginResult(result, false);
            }
            catch (Exception ex)
            {
                ResultText.Text = $"登录失败: {ex.Message}";
                ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red);
                SetInputControlsEnabled(true);
                NextButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 验证码输入框文本变化事件
        /// </summary>
        private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 隐藏错误提示
            CodeErrorText.Visibility = Visibility.Collapsed;

            // 只允许输入数字
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                string text = textBox.Text;
                string filteredText = new string(text.Where(c => char.IsDigit(c)).ToArray());
                if (text != filteredText)
                {
                    int selectionStart = textBox.SelectionStart;
                    textBox.Text = filteredText;
                    textBox.SelectionStart = Math.Min(selectionStart, filteredText.Length);
                }
            }
        }

        /// <summary>
        /// 验证码弹窗主按钮点击事件
        /// </summary>
        private async void AuthCodeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 获取延迟对象，以便异步操作
            var deferral = args.GetDeferral();

            string code = CodeBox.Text.Trim();

            // 验证验证码格式
            if (code.Length != 6)
            {
                CodeErrorText.Text = "验证码必须为6位数字";
                CodeErrorText.Visibility = Visibility.Visible;
                args.Cancel = true; // 取消关闭对话框
                deferral.Complete();
                return;
            }

            try
            {
                // 显示加载状态
                sender.IsPrimaryButtonEnabled = false;
                CodeErrorText.Text = "正在验证...";
                CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray);
                CodeErrorText.Visibility = Visibility.Visible;

                // 执行带验证码的登录命令
                string cmd = $"./ipatool.exe auth login --email {_email} --password \"{_password}\" --keychain-passphrase {KeychainPassphrase} --non-interactive --auth-code {code}";
                var result = await RunCommandAsync(cmd);

                // 检查是否登录成功
                if (IsLoginSuccess(result))
                {
                    await OnLoginSuccess();
                    // 成功后关闭对话框
                    deferral.Complete();
                    return;
                }
                else
                {
                    // 验证失败
                    CodeErrorText.Text = ParseErrorMessage(result);
                    CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.Red);
                    CodeErrorText.Visibility = Visibility.Visible;
                    CodeBox.Text = "";
                    args.Cancel = true; // 取消关闭对话框
                }
            }
            catch (Exception ex)
            {
                CodeErrorText.Text = $"验证失败: {ex.Message}";
                CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red);
                CodeErrorText.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
            finally
            {
                sender.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        }

        /// <summary>
        /// 验证码弹窗取消按钮点击事件
        /// </summary>
        private void AuthCodeDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 重置UI状态
            SetInputControlsEnabled(true);
            NextButton.IsEnabled = true;
            CodeBox.Text = "";
            CodeErrorText.Visibility = Visibility.Collapsed;
            ResultText.Text = "登录已取消";
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray);
        }

        /// <summary>
        /// 验证输入
        /// </summary>
        private bool ValidateInput(string email, string password)
        {
            return !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password);
        }

        /// <summary>
        /// 设置输入控件的启用状态
        /// </summary>
        private void SetInputControlsEnabled(bool enabled)
        {
            EmailComboBox.IsEnabled = enabled;
            PasswordBox.IsEnabled = enabled;
        }

        /// <summary>
        /// 处理登录结果
        /// </summary>
        private async Task HandleLoginResult(string result, bool isWithAuthCode)
        {
            // 检查是否登录成功
            if (IsLoginSuccess(result))
            {
                await OnLoginSuccess();
                return;
            }

            // 检查是否需要验证码
            if (!isWithAuthCode && RequiresAuthCode(result))
            {
                // 显示验证码弹窗
                await ShowAuthCodeDialog();
                return;
            }

            // 登录失败
            OnLoginFailed(result);
        }

        /// <summary>
        /// 显示验证码弹窗
        /// </summary>
        private async Task ShowAuthCodeDialog()
        {
            // 重置弹窗状态
            CodeBox.Text = "";
            CodeErrorText.Visibility = Visibility.Collapsed;
            CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Red);

            // 设置弹窗的 XamlRoot
            AuthCodeDialog.XamlRoot = this.XamlRoot;

            // 显示弹窗
            await AuthCodeDialog.ShowAsync();
        }

        /// <summary>
        /// 判断是否登录成功
        /// </summary>
        private bool IsLoginSuccess(string result)
        {
            return result.Contains("success=true") ||
                   result.Contains("[36msuccess=[0mtrue") ||
                   (result.Contains("success") && result.Contains("true"));
        }

        /// <summary>
        /// 判断是否需要验证码
        /// </summary>
        private bool RequiresAuthCode(string result)
        {
            return result.Contains("请输入验证码") ||
                   result.Contains("auth code") ||
                   result.Contains("2FA code is required") ||
                   result.Contains("authentication code");
        }

        /// <summary>
        /// 登录成功处理
        /// </summary>
        private async Task OnLoginSuccess()
        {
            // 保存账号信息
            SaveAccountToHistory();

            // 清除退出标记
            AccountHistoryDb.ClearLogoutFlag();

            // 显示成功消息
            ResultText.Text = "登录成功，正在跳转...";
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Green);

            // 延迟跳转，让用户看到成功提示
            await Task.Delay(500);

            // 导航到搜索页面
            Frame.Navigate(typeof(MainPage));
        }

        /// <summary>
        /// 登录失败处理
        /// </summary>
        private void OnLoginFailed(string result)
        {
            ResultText.Text = ParseErrorMessage(result);
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Red);
            SetInputControlsEnabled(true);
            NextButton.IsEnabled = true;
        }

        /// <summary>
        /// 解析错误消息
        /// </summary>
        private string ParseErrorMessage(string result)
        {
            if (result.Contains("invalid credentials") || result.Contains("incorrect"))
                return "用户名或密码错误";
            if (result.Contains("network") || result.Contains("timeout"))
                return "网络连接失败，请检查网络";
            if (result.Contains("invalid auth code") || result.Contains("验证码错误"))
                return "验证码错误，请重新输入";

            return result.Length > 200 ? result.Substring(0, 200) + "..." : result;
        }

        /// <summary>
        /// 保存账号到历史记录
        /// </summary>
        private void SaveAccountToHistory()
        {
            try
            {
                // 直接保存，数据库会自动处理重复情况
                AccountHistoryDb.SaveAccount(_email, _password);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存账号历史失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行命令行命令
        /// </summary>
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
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        return "无法启动进程";
                    }

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    return string.IsNullOrWhiteSpace(error) ? output : error;
                }
            }
            catch (Exception ex)
            {
                return $"命令执行失败: {ex.Message}";
            }
        }
    }
}
