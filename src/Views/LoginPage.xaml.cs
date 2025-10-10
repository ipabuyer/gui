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

        private readonly CancellationTokenSource _pageCts = new();
        private CancellationTokenSource? _currentOperationCts;

        private string _account = string.IsNullOrEmpty(LastLoginUsername) ? "example@icloud.com" : LastLoginUsername;
        private string _password = string.Empty;

        public LoginPage()
        {
            this.InitializeComponent();
            LoadAccountHistory();
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
            await TriggerLoginAsync();
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
            CancelCurrentOperation();
            ResetAuthDialogUI();
            SetInputControlsEnabled(true);
            SetBusyState(false, string.Empty);
            ShowInfo("登录已取消");
        }

        private async void AuthCodeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;

            bool success = await ValidateAuthCodeAsync();
            if (success)
            {
                AuthCodeDialogControl?.Hide();
            }

            deferral.Complete();
        }

        private async void CodeBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            bool success = await ValidateAuthCodeAsync();
            if (success)
            {
                AuthCodeDialogControl?.Hide();
            }
        }

        private async void Input_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            if (sender is TextBox && PasswordInput != null)
            {
                var accountText = EmailTextBox?.Text.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(accountText))
                {
                    ShowError("邮箱不能为空");
                    EmailTextBox?.Focus(FocusState.Programmatic);
                    return;
                }

                PasswordInput.Focus(FocusState.Programmatic);
            }
            else if (sender is PasswordBox)
            {
                await TriggerLoginAsync();
            }
        }

        private async Task TriggerLoginAsync()
        {
            CancelCurrentOperation();
            _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);

            _account = EmailTextBox?.Text.Trim() ?? string.Empty;
            _password = PasswordInput?.Password ?? string.Empty;

            bool hasAccount = !string.IsNullOrWhiteSpace(_account);
            bool hasPassword = !string.IsNullOrWhiteSpace(_password);

            bool hasLocalValidationIssue = false;
            if (!hasAccount || !hasPassword)
            {
                hasLocalValidationIssue = true;
                ShowError("邮箱和密码不能为空");

                if (!hasAccount)
                {
                    EmailTextBox?.Focus(FocusState.Programmatic);
                }
                else if (!hasPassword)
                {
                    PasswordInput?.Focus(FocusState.Programmatic);
                }
            }

            SetInputControlsEnabled(false);
            SetBusyState(true, hasLocalValidationIssue ? string.Empty : "正在登录...");

            var result = await LoginService.LoginAsync(_account, _password, _currentOperationCts.Token);
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
                ShowAuthError("请输入六位验证码");
                CodeTextBox.Focus(FocusState.Programmatic);
                return false;
            }

            HideAuthMessage();

            CancelCurrentOperation();
            _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);

            if (AuthCodeDialogControl != null)
            {
                AuthCodeDialogControl.IsPrimaryButtonEnabled = false;
            }

            ShowAuthWarning("正在验证...");

            var result = await LoginService.VerifyAuthCodeAsync(_account, _password, authCode, _currentOperationCts.Token);
            DisposeCurrentOperation();

            if (AuthCodeDialogControl != null)
            {
                AuthCodeDialogControl.IsPrimaryButtonEnabled = true;
            }

            if (result.Status == LoginStatus.AuthCodeInvalid)
            {
                ShowAuthError("验证码错误，请重新输入。");
                CodeTextBox.Text = string.Empty;
                CodeTextBox.Focus(FocusState.Programmatic);
                return false;
            }

            if (result.Status == LoginStatus.Timeout)
            {
                CodeTextBox.Focus(FocusState.Programmatic);
                return false;
            }

            if (!result.IsSuccess)
            {
                ShowAuthError(result.Message);
                return false;
            }

            ShowAuthSuccess("验证成功，正在完成登录...");
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
                    await ShowAuthCodeDialogAsync(result.Message);
                    break;

                case LoginStatus.InvalidCredential:
                    ShowError(result.Message);
                    RestoreIdleState();
                    break;

                case LoginStatus.NetworkError:
                case LoginStatus.UnknownError:
                    ShowError(result.Message);
                    RestoreIdleState();
                    break;

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

        private async Task ShowAuthCodeDialogAsync(string message)
        {
            ResetAuthDialogUI();

            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Text = message;
                CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                CodeErrorTextBlock.Visibility = Visibility.Visible;
            }

            CodeTextBox?.Focus(FocusState.Programmatic);

            if (AuthCodeDialogControl != null)
            {
                AuthCodeDialogControl.XamlRoot = this.XamlRoot;
                await AuthCodeDialogControl.ShowAsync();
            }
        }

        private void ResetAuthDialogUI()
        {
            if (CodeTextBox != null)
            {
                CodeTextBox.Text = string.Empty;
            }

            HideAuthMessage();
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

        private void ShowAuthSuccess(string message)
        {
            if (CodeErrorTextBlock != null)
            {
                CodeErrorTextBlock.Text = message;
                CodeErrorTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
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

        private async Task OnLoginSuccessAsync()
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
            DisposeCurrentOperation();
            ShowSuccess("登录成功，正在跳转...");

            await Task.Delay(400);
            Frame.Navigate(typeof(MainPage), _account);
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

        private void BackToMainpage(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
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