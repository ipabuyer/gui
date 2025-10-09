using IPAbuyer.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class LoginPage : Page
    {
        private static readonly string? LastLoginUsername = KeychainConfig.GetLastLoginUsername();
        private readonly CancellationTokenSource _loginCts = new();
        private string _account = string.IsNullOrEmpty(LastLoginUsername) ? "example@icloud.com" : LastLoginUsername;
        private string _password = string.Empty;

        public LoginPage()
        {
            this.InitializeComponent();
            LoadAccountHistory();
            this.Unloaded += LoginPage_Unloaded;
        }

        private void LoginPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_loginCts.IsCancellationRequested)
            {
                _loginCts.Cancel();
            }

            _loginCts.Dispose();
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await LoginAsync();
        }

        private void LoadAccountHistory()
        {
            try
            {
                if (!string.IsNullOrEmpty(LastLoginUsername) && EmailTextBox != null)
                {
                    EmailTextBox.Text = LastLoginUsername;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载账号历史失败: {ex.Message}");
            }
        }

        private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Visibility = Visibility.Collapsed;
            }

            if (sender is TextBox textBox)
            {
                string filtered = new string(textBox.Text.Where(char.IsDigit).ToArray());
                if (!string.Equals(filtered, textBox.Text, StringComparison.Ordinal))
                {
                    int selectionStart = textBox.SelectionStart;
                    textBox.Text = filtered;
                    textBox.SelectionStart = Math.Min(selectionStart, filtered.Length);
                }
            }
        }

        private void AuthCodeDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            SetInputControlsEnabled(true);
            if (NextButtonControl != null)
            {
                NextButtonControl.IsEnabled = true;
            }

            if (CodeTextBox != null)
            {
                CodeTextBox.Text = string.Empty;
            }

            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Visibility = Visibility.Collapsed;
            }

            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = "登录已取消";
                ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            }
        }

        private bool ValidateInput(string account, string password)
        {
            return !string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password);
        }

        private void SetInputControlsEnabled(bool enabled)
        {
            if (EmailTextBox != null)
            {
                EmailTextBox.IsEnabled = enabled;
            }

            if (PasswordInput != null)
            {
                PasswordInput.IsEnabled = enabled;
            }
        }

        private async Task HandleLoginResultAsync(string result, bool isWithAuthCode)
        {
            if (IsLoginSuccess(result))
            {
                await OnLoginSuccess();
                return;
            }

            if (!isWithAuthCode && RequiresAuthCode(result))
            {
                await ShowAuthCodeDialogAsync();
                return;
            }

            OnLoginFailed(result);
        }

        private async Task ShowAuthCodeDialogAsync()
        {
            if (CodeTextBox != null)
            {
                CodeTextBox.Text = string.Empty;
                CodeTextBox.Focus(FocusState.Programmatic);
            }

            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Visibility = Visibility.Collapsed;
                CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }

            if (AuthCodeDialogControl != null)
            {
                AuthCodeDialogControl.XamlRoot = this.XamlRoot;
                await AuthCodeDialogControl.ShowAsync();
            }
        }

        private bool IsLoginSuccess(string result)
        {
            return result.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase)
                || (result.Contains("\"success\"", StringComparison.OrdinalIgnoreCase)
                    && result.Contains("true", StringComparison.OrdinalIgnoreCase));
        }

        private bool RequiresAuthCode(string result)
        {
            return result.Contains("请输入验证码", StringComparison.OrdinalIgnoreCase)
                || result.Contains("auth code", StringComparison.OrdinalIgnoreCase)
                || result.Contains("2FA", StringComparison.OrdinalIgnoreCase)
                || result.Contains("authentication code", StringComparison.OrdinalIgnoreCase);
        }

        private async Task OnLoginSuccess()
        {
            try
            {
                KeychainConfig.GenerateAndSaveSecretKey(_account);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存账户信息失败: {ex.Message}");
            }

            SessionState.SetLoginState(_account, true);

            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = "登录成功,正在跳转...";
                ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
            }

            await Task.Delay(500);
            Frame.Navigate(typeof(MainPage), true);
        }

        private void OnLoginFailed(string result)
        {
            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = ParseErrorMessage(result);
                ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }

            SetInputControlsEnabled(true);
            if (NextButtonControl != null)
            {
                NextButtonControl.IsEnabled = true;
            }

            SessionState.Reset();
        }

        private string ParseErrorMessage(string result)
        {
            if (result.Contains("invalid credentials", StringComparison.OrdinalIgnoreCase)
                || result.Contains("incorrect", StringComparison.OrdinalIgnoreCase))
            {
                return "用户名或密码错误";
            }

            if (result.Contains("network", StringComparison.OrdinalIgnoreCase)
                || result.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return "网络连接失败，请检查网络";
            }

            if (result.Contains("invalid auth code", StringComparison.OrdinalIgnoreCase)
                || result.Contains("验证码错误", StringComparison.OrdinalIgnoreCase))
            {
                return "验证码错误，请重新输入";
            }

            return result.Length > 200 ? result[..200] + "..." : result;
        }

        private void BackToMainpage(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        private async void Input_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            if (sender is TextBox && PasswordInput != null)
            {
                PasswordInput.Focus(FocusState.Programmatic);
            }
            else if (sender is PasswordBox)
            {
                await LoginAsync();
            }
        }

        private async Task LoginAsync()
        {
            _account = EmailTextBox?.Text.Trim() ?? string.Empty;
            _password = PasswordInput?.Password ?? string.Empty;

            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = string.Empty;
            }

            if (!ValidateInput(_account, _password))
            {
                if (ResultTextBlock != null)
                {
                    ResultTextBlock.Text = "邮箱和密码不能为空";
                    ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                }

                if (string.IsNullOrWhiteSpace(_account))
                {
                    EmailTextBox?.Focus(FocusState.Programmatic);
                }
                else if (string.IsNullOrWhiteSpace(_password))
                {
                    PasswordInput?.Focus(FocusState.Programmatic);
                }
                return;
            }

            SetInputControlsEnabled(false);
            if (NextButtonControl != null)
            {
                NextButtonControl.IsEnabled = false;
            }

            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = "正在登录...";
                ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            }

            try
            {
                var response = await ipatoolExecution.AuthLoginAsync(_account, _password, "000000", _loginCts.Token);

                if (response.TimedOut)
                {
                    if (ResultTextBlock != null)
                    {
                        ResultTextBlock.Text = "登录请求超时，请稍后再试";
                        ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                    }

                    SetInputControlsEnabled(true);
                    if (NextButtonControl != null)
                    {
                        NextButtonControl.IsEnabled = true;
                    }
                    return;
                }

                string result = response.OutputOrError;

                if (RequiresAuthCode(result))
                {
                    await ShowAuthCodeDialogAsync();
                    return;
                }

                if (IsLoginSuccess(result))
                {
                    await OnLoginSuccess();
                    return;
                }

                await HandleLoginResultAsync(result, false);
            }
            catch (OperationCanceledException)
            {
                // 页面卸载时取消，不需要额外提示
            }
            catch (Exception ex)
            {
                if (ResultTextBlock != null)
                {
                    ResultTextBlock.Text = $"登录失败: {ex.Message}";
                    ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                }

                SetInputControlsEnabled(true);
                if (NextButtonControl != null)
                {
                    NextButtonControl.IsEnabled = true;
                }
            }
        }

        private async void CodeBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;

            bool isSuccess = await ValidateAuthCodeAsync();
            if (isSuccess)
            {
                AuthCodeDialogControl?.Hide();
            }
        }

        private async Task<bool> ValidateAuthCodeAsync()
        {
            string authCode = CodeTextBox?.Text.Trim() ?? string.Empty;

            if (authCode.Length != 6)
            {
                if (CodeErrorTextBlock != null)
                {
                    CodeErrorTextBlock.Text = "验证码必须为6位数字";
                    CodeErrorTextBlock.Visibility = Visibility.Visible;
                }

                return false;
            }

            try
            {
                if (AuthCodeDialogControl != null)
                {
                    AuthCodeDialogControl.IsPrimaryButtonEnabled = false;
                }

                if (CodeErrorTextBlock != null)
                {
                    CodeErrorTextBlock.Text = "正在验证...";
                    CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                    CodeErrorTextBlock.Visibility = Visibility.Visible;
                }

                var response = await ipatoolExecution.AuthLoginAsync(_account, _password, authCode, _loginCts.Token);

                if (response.TimedOut)
                {
                    if (CodeErrorTextBlock != null)
                    {
                        CodeErrorTextBlock.Text = "验证码验证超时，请重试";
                        CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                        CodeErrorTextBlock.Visibility = Visibility.Visible;
                    }

                    return false;
                }

                string result = response.OutputOrError;

                if (IsLoginSuccess(result))
                {
                    await OnLoginSuccess();
                    return true;
                }

                if ((result.Contains("error", StringComparison.OrdinalIgnoreCase) && result.Contains("something went wrong", StringComparison.OrdinalIgnoreCase))
                    || result.Contains("invalid credentials", StringComparison.OrdinalIgnoreCase)
                    || result.Contains("incorrect", StringComparison.OrdinalIgnoreCase))
                {
                    var dialog = new ContentDialog
                    {
                        Title = "登录失败",
                        Content = "Apple ID或密码错误，请检查后重试。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot,
                    };

                    await dialog.ShowAsync();

                    if (ResultTextBlock != null)
                    {
                        ResultTextBlock.Text = "登录失败，Apple ID或密码错误";
                        ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                    }

                    SetInputControlsEnabled(true);
                    if (NextButtonControl != null)
                    {
                        NextButtonControl.IsEnabled = true;
                    }

                    return false;
                }

                if (result.Contains("invalid auth code", StringComparison.OrdinalIgnoreCase)
                    || result.Contains("验证码错误", StringComparison.OrdinalIgnoreCase))
                {
                    if (CodeErrorTextBlock != null)
                    {
                        CodeErrorTextBlock.Text = "验证码错误，请重新输入。";
                        CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                        CodeErrorTextBlock.Visibility = Visibility.Visible;
                    }

                    if (CodeTextBox != null)
                    {
                        CodeTextBox.Text = string.Empty;
                        CodeTextBox.Focus(FocusState.Programmatic);
                    }

                    return false;
                }

                if (CodeErrorTextBlock != null)
                {
                    CodeErrorTextBlock.Text = ParseErrorMessage(result);
                    CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                    CodeErrorTextBlock.Visibility = Visibility.Visible;
                }

                if (CodeTextBox != null)
                {
                    CodeTextBox.Text = string.Empty;
                    CodeTextBox.Focus(FocusState.Programmatic);
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                if (CodeErrorTextBlock != null)
                {
                    CodeErrorTextBlock.Text = $"验证失败: {ex.Message}";
                    CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                    CodeErrorTextBlock.Visibility = Visibility.Visible;
                }

                return false;
            }
            finally
            {
                if (AuthCodeDialogControl != null)
                {
                    AuthCodeDialogControl.IsPrimaryButtonEnabled = true;
                }
            }
        }

        private async void AuthCodeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;

            bool isSuccess = await ValidateAuthCodeAsync();
            if (isSuccess)
            {
                AuthCodeDialogControl?.Hide();
            }

            deferral.Complete();
        }

        private TextBox? EmailTextBox => GetControl<TextBox>("EmailComboBox");
        private PasswordBox? PasswordInput => GetControl<PasswordBox>("PasswordBox");
        private Button? NextButtonControl => GetControl<Button>("NextButton");
        private TextBlock? ResultTextBlock => GetControl<TextBlock>("ResultText");
        private ContentDialog? AuthCodeDialogControl => GetControl<ContentDialog>("AuthCodeDialog");
        private TextBox? CodeTextBox => GetControl<TextBox>("CodeBox");
        private TextBlock? CodeErrorTextBlock => GetControl<TextBlock>("CodeErrorText");

        private T? GetControl<T>(string name)
            where T : class
        {
            return FindName(name) as T;
        }
    }
}