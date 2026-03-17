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
        private readonly CancellationTokenSource _pageCts = new();
        private CancellationTokenSource? _currentOperationCts;

        private string? _lastLoginUsername;
        private string _account = "example@icloud.com";
        private string _password = string.Empty;
        private string _passphrase = string.Empty;
        private bool _isTwoFactorPending;

        private const string TestCredential = "test";

        public LoginPage()
        {
            InitializeComponent();

            _lastLoginUsername = KeychainConfig.GetLastLoginUsername();
            if (!string.IsNullOrWhiteSpace(_lastLoginUsername))
            {
                _account = _lastLoginUsername;
            }

            LoadAccountHistory();
            string? savedPassphrase = KeychainConfig.GetPassphrase(_account);
            if (!string.IsNullOrWhiteSpace(savedPassphrase) && PassphraseInput != null)
            {
                PassphraseInput.Text = savedPassphrase;
                _passphrase = savedPassphrase;
            }

            Unloaded += LoginPage_Unloaded;
        }

        private void LoginPage_Unloaded(object sender, RoutedEventArgs e)
        {
            CancelCurrentOperation();
            if (!_pageCts.IsCancellationRequested)
            {
                _pageCts.Cancel();
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isTwoFactorPending)
            {
                await ValidateAuthCodeAsync();
                return;
            }

            await TriggerLoginAsync();
        }

        private void LoadAccountHistory()
        {
            try
            {
                _lastLoginUsername = KeychainConfig.GetLastLoginUsername();

                if (!string.IsNullOrEmpty(_lastLoginUsername) && EmailTextBox != null)
                {
                    EmailTextBox.Text = _lastLoginUsername;
                    _account = _lastLoginUsername;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载账户历史失败: {ex.Message}");
            }
        }

        private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            HideAuthMessage();

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

        private async void CodeBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter || !_isTwoFactorPending)
            {
                return;
            }

            e.Handled = true;
            await ValidateAuthCodeAsync();
        }

        private async void Input_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            if (sender is TextBox && PasswordInput != null)
            {
                string accountText = EmailTextBox?.Text.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(accountText))
                {
                    ShowError("邮箱不能为空");
                    EmailTextBox?.Focus(FocusState.Programmatic);
                    return;
                }

                PasswordInput.Focus(FocusState.Programmatic);
                return;
            }

            if (ReferenceEquals(sender, PasswordInput))
            {
                if (_isTwoFactorPending && CodeTextBox != null && AuthCodeInlinePanelControl?.Visibility == Visibility.Visible)
                {
                    CodeTextBox.Focus(FocusState.Programmatic);
                    return;
                }

                PassphraseInput?.Focus(FocusState.Programmatic);
                return;
            }

            if (ReferenceEquals(sender, PassphraseInput))
            {
                if (_isTwoFactorPending)
                {
                    await ValidateAuthCodeAsync();
                }
                else
                {
                    await TriggerLoginAsync();
                }
            }
        }

        private async Task TriggerLoginAsync()
        {
            CancelCurrentOperation();
            _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);

            _account = EmailTextBox?.Text.Trim() ?? string.Empty;
            _password = PasswordInput?.Password?.Trim() ?? string.Empty;
            _passphrase = PassphraseInput?.Text.Trim() ?? string.Empty;

            if (IsTestCredential(_account, _password))
            {
                _account = TestCredential;
                _password = TestCredential;
                if (string.IsNullOrWhiteSpace(_passphrase))
                {
                    _passphrase = TestCredential;
                }
            }

            bool hasAccount = !string.IsNullOrWhiteSpace(_account);
            bool hasPassword = !string.IsNullOrWhiteSpace(_password);
            bool hasPassphrase = !string.IsNullOrWhiteSpace(_passphrase);

            bool hasLocalValidationIssue = false;
            if (!hasAccount || !hasPassword || !hasPassphrase)
            {
                hasLocalValidationIssue = true;
                ShowError("邮箱、密码和加密密钥不能为空");

                if (!hasAccount)
                {
                    EmailTextBox?.Focus(FocusState.Programmatic);
                }
                else if (!hasPassword)
                {
                    PasswordInput?.Focus(FocusState.Programmatic);
                }
                else if (!hasPassphrase)
                {
                    PassphraseInput?.Focus(FocusState.Programmatic);
                }
            }

            SetInputControlsEnabled(false);
            SetBusyState(true, hasLocalValidationIssue ? string.Empty : "正在登录...");

            var result = await LoginService.LoginAsync(_account, _password, _passphrase, _currentOperationCts.Token);
            DisposeCurrentOperation();
            await HandleLoginResultAsync(result, isTwoFactorStep: false);
        }

        private async Task<bool> ValidateAuthCodeAsync()
        {
            if (CodeTextBox == null)
            {
                return false;
            }

            string authCode = CodeTextBox.Text.Trim();
            if (authCode.Length != 6)
            {
                ShowAuthError("请输入 6 位验证码");
                CodeTextBox.Focus(FocusState.Programmatic);
                return false;
            }

            HideAuthMessage();

            CancelCurrentOperation();
            _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);

            SetInputControlsEnabled(false);
            SetBusyState(true, "正在验证...");

            var result = await LoginService.VerifyAuthCodeAsync(_account, _password, _passphrase, authCode, _currentOperationCts.Token);
            DisposeCurrentOperation();

            if (result.Status == LoginStatus.AuthCodeInvalid)
            {
                ShowAuthError("验证码错误，请重新输入。");
                CodeTextBox.Text = string.Empty;
                CodeTextBox.Focus(FocusState.Programmatic);
                RestoreIdleState();
                return false;
            }

            if (result.Status == LoginStatus.Timeout)
            {
                ShowAuthError(result.Message);
                CodeTextBox.Focus(FocusState.Programmatic);
                RestoreIdleState();
                return false;
            }

            if (!result.IsSuccess)
            {
                ShowAuthError(string.IsNullOrWhiteSpace(result.Message) ? "验证失败，请重试。" : result.Message);
                RestoreIdleState();
                return false;
            }

            await OnLoginSuccessAsync();
            return true;
        }

        private async Task HandleLoginResultAsync(LoginResult result, bool isTwoFactorStep)
        {
            switch (result.Status)
            {
                case LoginStatus.Success:
                    await OnLoginSuccessAsync();
                    break;

                case LoginStatus.RequiresTwoFactor:
                    ShowInlineTwoFactor(result.Message);
                    RestoreIdleState();
                    break;

                case LoginStatus.InvalidCredential:
                case LoginStatus.NetworkError:
                case LoginStatus.UnknownError:
                case LoginStatus.Timeout:
                    ShowError(result.Message);
                    RestoreIdleState();
                    break;

                case LoginStatus.AuthCodeInvalid:
                    if (isTwoFactorStep)
                    {
                        ShowAuthError(result.Message);
                    }
                    else
                    {
                        ShowError(result.Message);
                    }
                    RestoreIdleState();
                    break;
            }
        }

        private void ShowInlineTwoFactor(string message)
        {
            _isTwoFactorPending = true;

            if (AuthCodeInlinePanelControl != null)
            {
                AuthCodeInlinePanelControl.Visibility = Visibility.Visible;
            }

            if (NextButtonControl != null)
            {
                NextButtonControl.Content = "验证并登录";
            }

            ShowAuthWarning(string.IsNullOrWhiteSpace(message) ? "请输入两步验证码继续登录。" : message);
            CodeTextBox?.Focus(FocusState.Programmatic);
        }

        private void HideInlineTwoFactor()
        {
            _isTwoFactorPending = false;

            if (AuthCodeInlinePanelControl != null)
            {
                AuthCodeInlinePanelControl.Visibility = Visibility.Collapsed;
            }

            if (CodeTextBox != null)
            {
                CodeTextBox.Text = string.Empty;
            }

            HideAuthMessage();

            if (NextButtonControl != null)
            {
                NextButtonControl.Content = "登录";
            }
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

            if (PassphraseInput != null)
            {
                PassphraseInput.IsEnabled = enabled;
            }

            if (CodeTextBox != null)
            {
                CodeTextBox.IsEnabled = enabled;
            }
        }

        private void SetBusyState(bool isBusy, string message)
        {
            if (NextButtonControl != null)
            {
                NextButtonControl.IsEnabled = !isBusy;
            }

            if (!string.IsNullOrEmpty(message))
            {
                ShowInfo(message);
            }
        }

        private void ShowInfo(string message)
        {
            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = message;
                ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            }
        }

        private void ShowError(string message)
        {
            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = message;
                ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
        }

        private void ShowSuccess(string message)
        {
            if (ResultTextBlock != null)
            {
                ResultTextBlock.Text = message;
                ResultTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
            }
        }

        private void ShowAuthError(string message)
        {
            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Text = message;
                CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                CodeErrorTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void ShowAuthWarning(string message)
        {
            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Text = message;
                CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                CodeErrorTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void HideAuthMessage()
        {
            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Visibility = Visibility.Collapsed;
                CodeErrorTextBlock.Text = string.Empty;
            }
        }

        private void RestoreIdleState()
        {
            SetInputControlsEnabled(true);
            SetBusyState(false, string.Empty);
        }

        private Task OnLoginSuccessAsync()
        {
            try
            {
                KeychainConfig.GenerateAndSaveSecretKey(_account);
                KeychainConfig.SavePassphrase(_account, _passphrase);
                _lastLoginUsername = _account;
                if (EmailTextBox != null)
                {
                    EmailTextBox.Text = _account;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存账户信息失败: {ex.Message}");
            }

            HideInlineTwoFactor();
            SessionState.SetLoginState(_account, true);
            DisposeCurrentOperation();
            ShowSuccess("登录成功");
            return Task.CompletedTask;
        }

        private void CancelCurrentOperation()
        {
            if (_currentOperationCts != null)
            {
                if (!_currentOperationCts.IsCancellationRequested)
                {
                    _currentOperationCts.Cancel();
                }

                DisposeCurrentOperation();
            }
        }

        private void DisposeCurrentOperation()
        {
            if (_currentOperationCts != null)
            {
                _currentOperationCts.Dispose();
                _currentOperationCts = null;
            }
        }

        private static bool IsTestCredential(string account, string password)
        {
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            return account.Equals(TestCredential, StringComparison.OrdinalIgnoreCase)
                && password.Equals(TestCredential, StringComparison.OrdinalIgnoreCase);
        }

        private TextBox? EmailTextBox => GetControl<TextBox>("EmailComboBox");
        private PasswordBox? PasswordInput => GetControl<PasswordBox>("PasswordBox");
        private TextBox? PassphraseInput => GetControl<TextBox>("PassphraseBox");
        private Button? NextButtonControl => GetControl<Button>("NextButton");
        private TextBlock? ResultTextBlock => GetControl<TextBlock>("ResultText");
        private TextBox? CodeTextBox => GetControl<TextBox>("CodeBox");
        private TextBlock? CodeErrorTextBlock => GetControl<TextBlock>("CodeErrorText");
        private StackPanel? AuthCodeInlinePanelControl => GetControl<StackPanel>("AuthCodeInlinePanel");

        private T? GetControl<T>(string name)
            where T : class
        {
            return FindName(name) as T;
        }
    }
}
