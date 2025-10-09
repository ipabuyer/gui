using IPAbuyer.Common;
using IPAbuyer.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class LoginPage : Page
    {
        public static string LastLoginUsername = KeychainConfig.GetLastLoginUsername();
        public static string _account = string.IsNullOrEmpty(LastLoginUsername) ? "example@icloud.com" : LastLoginUsername;
        public static string _password = "examplePassword";
        public static string _authcode = "000000";

        public LoginPage()
        {
            this.InitializeComponent();
            LoadAccountHistory();
        }

        /// <summary>
        /// 加载账号历史记录
        /// </summary>
        private void LoadAccountHistory()
        {
            try
            {
                if (!string.IsNullOrEmpty(LastLoginUsername))
                {
                    EmailComboBox.Text = LastLoginUsername;
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
            await LoginAsync();
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
        /// 验证码弹窗取消按钮点击事件
        /// </summary>
        private void AuthCodeDialog_CloseButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args
        )
        {
            // 重置UI状态
            SetInputControlsEnabled(true);
            NextButton.IsEnabled = true;
            CodeBox.Text = "";
            CodeErrorText.Visibility = Visibility.Collapsed;
            ResultText.Text = "登录已取消";
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray
            );
        }

        /// <summary>
        /// 验证输入
        /// </summary>
        private bool ValidateInput(string account, string password)
        {
            return !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password);
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
                Microsoft.UI.Colors.Red
            );

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
            return result.Contains("success") && result.Contains("true");
        }

        /// <summary>
        /// 判断是否需要验证码
        /// </summary>
        private bool RequiresAuthCode(string result)
        {
            return result.Contains("请输入验证码")
                || result.Contains("auth code")
                || result.Contains("2FA code is required")
                || result.Contains("authentication code");
        }

        /// <summary>
        /// 登录成功处理
        /// </summary>
        private async Task OnLoginSuccess()
        {
            // 显示成功消息
            ResultText.Text = "登录成功，正在跳转...";
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Green
            );

            // 延迟跳转，让用户看到成功提示
            await Task.Delay(500);

            // 导航到搜索页面，并传递登录状态
            Frame.Navigate(typeof(MainPage), true); // 传递 true 表示登录成功
        }

        /// <summary>
        /// 登录失败处理
        /// </summary>
        private void OnLoginFailed(string result)
        {
            ResultText.Text = ParseErrorMessage(result);
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Red
            );
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

        private void BackToMainpage(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        /// <summary>
        /// 回车键功能设置
        /// </summary>
        private async void Input_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (sender is TextBox tb && tb == EmailComboBox)
                {
                    PasswordBox.Focus(FocusState.Programmatic);
                }
                else if (sender is PasswordBox pb && pb == PasswordBox)
                {
                    await LoginAsync();
                }
            }
        }

        private async Task LoginAsync()
        {
            // 获取输入的邮箱和密码
            _account = EmailComboBox.Text.Trim();
            _password = PasswordBox.Password;

            // 清空结果提示
            ResultText.Text = "";

            // 验证输入
            if (!ValidateInput(_account, _password))
            {
                ResultText.Text = "邮箱和密码不能为空";
                ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red
                );

                // 聚焦到空的输入框
                if (string.IsNullOrWhiteSpace(_account))
                {
                    EmailComboBox.Focus(FocusState.Programmatic);
                }
                else if (string.IsNullOrWhiteSpace(_password))
                {
                    PasswordBox.Focus(FocusState.Programmatic);
                }
                return;
            }

            // 禁用输入控件
            SetInputControlsEnabled(false);
            NextButton.IsEnabled = false;
            ResultText.Text = "正在登录...";
            ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray
            );

            try
            {
                // 执行登录命令，初始验证码000000
                _account = _account ?? string.Empty;
                _password = _password ?? string.Empty;
                var result = ipatoolExecution.authLogin(_account, _password, "000000");

                // 判断是否需要2FA
                if (
                    result.Contains("2FA required")
                    || result.Contains("auth code")
                    || result.Contains("authentication code")
                    || result.Contains("error") && result.Contains("something went wrong")
                    || result.Contains("\"success\":false")
                )
                {
                    await ShowAuthCodeDialog();
                    return;
                }
                // 登录成功
                if (result.Contains("\"success\":true"))
                {
                    await OnLoginSuccess();
                    return;
                }
                // 其它情况
                await HandleLoginResult(result, false);
            }
            catch (Exception ex)
            {
                ResultText.Text = $"登录失败: {ex.Message}";
                ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red
                );
                SetInputControlsEnabled(true);
                NextButton.IsEnabled = true;
            }
        }

        private async void CodeBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                // 阻止默认行为
                e.Handled = true;

                bool isSuccess = await ValidateAuthCodeAsync();
                if (isSuccess)
                {
                    AuthCodeDialog.Hide();
                }
            }
        }

        private async Task<bool> ValidateAuthCodeAsync()
        {
            string _authcode = CodeBox.Text.Trim();

            // 验证验证码格式
            if (_authcode.Length != 6)
            {
                CodeErrorText.Text = "验证码必须为6位数字";
                CodeErrorText.Visibility = Visibility.Visible;
                return false;
            }

            try
            {
                // 显示加载状态
                AuthCodeDialog.IsPrimaryButtonEnabled = false;
                CodeErrorText.Text = "正在验证...";
                CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray
                );
                CodeErrorText.Visibility = Visibility.Visible;

                // 执行带验证码的登录命令
                _account = _account ?? string.Empty;
                _password = _password ?? string.Empty;
                var result = ipatoolExecution.authLogin(_account, _password, _authcode);

                // 检查是否登录成功
                if (result.Contains("\"success\":true"))
                {
                    await OnLoginSuccess();
                    return true;
                }
                // 账号或密码错误（json结果）
                if (
                    (result.Contains("error") && result.Contains("something went wrong"))
                    || (result.Contains("invalid credentials"))
                    || (result.Contains("incorrect"))
                )
                {
                    var dialog = new ContentDialog
                    {
                        Title = "登录失败",
                        Content = "Apple ID或密码错误，请检查后重试。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot,
                    };
                    await dialog.ShowAsync();
                    ResultText.Text = "登录失败，Apple ID或密码错误";
                    ResultText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.Red
                    );
                    SetInputControlsEnabled(true);
                    NextButton.IsEnabled = true;
                    return false;
                }
                // 验证码错误
                if (result.Contains("invalid auth code") || result.Contains("验证码错误"))
                {
                    CodeErrorText.Text = "验证码错误，请重新输入。";
                    CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.Red
                    );
                    CodeErrorText.Visibility = Visibility.Visible;
                    CodeBox.Text = "";
                    CodeBox.Focus(FocusState.Programmatic);
                    return false;
                }
                // 其它情况
                CodeErrorText.Text = ParseErrorMessage(result);
                CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red
                );
                CodeErrorText.Visibility = Visibility.Visible;
                CodeBox.Text = "";
                CodeBox.Focus(FocusState.Programmatic);
                return false;
            }
            catch (Exception ex)
            {
                CodeErrorText.Text = $"验证失败: {ex.Message}";
                CodeErrorText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Red
                );
                CodeErrorText.Visibility = Visibility.Visible;
                return false;
            }
            finally
            {
                AuthCodeDialog.IsPrimaryButtonEnabled = true;
            }
        }

        private async void AuthCodeDialog_PrimaryButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args
        )
        {
            // 获取延迟对象，以便异步操作
            var deferral = args.GetDeferral();

            // 取消默认关闭行为，我们根据验证结果决定是否关闭
            args.Cancel = true;

            bool isSuccess = await ValidateAuthCodeAsync();
            if (isSuccess)
            {
                // 验证成功，隐藏对话框
                AuthCodeDialog.Hide();
            }

            deferral.Complete();
        }
    }
}