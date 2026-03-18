using IPAbuyer.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPAbuyer.Views
{
    public sealed partial class LoginPage : Page
    {
        private readonly CancellationTokenSource _pageCts = new();
        private readonly StringBuilder _loginLogBuilder = new();
        private CancellationTokenSource? _currentOperationCts;

        private string? _lastLoginUsername;
        private string _account = "example@icloud.com";
        private string _password = string.Empty;
        private string _passphrase = string.Empty;
        private bool _isTwoFactorPending;
        private bool _operationLocked;

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
            else if (PassphraseInput != null)
            {
                PassphraseInput.Text = "12345678";
                _passphrase = "12345678";
            }

            Unloaded += LoginPage_Unloaded;
            Loaded += LoginPage_Loaded;
        }

        private async void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            await QueryAuthInfoAsync(isAutoCheck: true);
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
                AppendLoginLog("Start verifying two-factor code.");
                await ValidateAuthCodeAsync();
                return;
            }

            AppendLoginLog("Start login.");
            await TriggerLoginAsync();
        }

        private async void AuthInfoButton_Click(object sender, RoutedEventArgs e)
        {
            await QueryAuthInfoAsync(isAutoCheck: false);
        }

        private async Task QueryAuthInfoAsync(bool isAutoCheck)
        {
            string account = (EmailTextBox?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(account))
            {
                account = SessionState.CurrentAccount;
            }

            if (string.IsNullOrWhiteSpace(account))
            {
                if (!isAutoCheck)
                {
                    ShowError("请先输入邮箱或登录账户");
                }

                AppendLoginLog("Auth info skipped: account is empty.");
                return;
            }

            try
            {
                CancelCurrentOperation();
                _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
                SetBusyState(true, "正在查询登录状态...");

                var result = await IpatoolExecution.AuthInfoAsync(account, _currentOperationCts.Token);
                DisposeCurrentOperation();

                if (result.IsSuccessResponse)
                {
                    SessionState.SetLoginState(account, true);
                    ApplyOperationLock(true);
                    ShowSuccess("登录状态正常");
                    AppendLoginLog($"Auth info success: {account}");
                }
                else
                {
                    ApplyOperationLock(false);
                    ShowError(string.IsNullOrWhiteSpace(result.OutputOrError) ? "登录状态异常" : result.OutputOrError);
                    AppendLoginLog($"Auth info failed: {account}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"查询登录状态失败: {ex.Message}");
                AppendLoginLog($"Auth info exception: {ex.Message}");
            }
            finally
            {
                RestoreIdleState();
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            string account = SessionState.CurrentAccount;
            if (string.IsNullOrWhiteSpace(account))
            {
                account = (EmailTextBox?.Text ?? string.Empty).Trim();
            }

            if (string.IsNullOrWhiteSpace(account))
            {
                ShowError("未找到可退出的账户");
                AppendLoginLog("Logout failed: account is empty.");
                return;
            }

            try
            {
                CancelCurrentOperation();
                _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
                SetBusyState(true, "正在退出登录...");

                var result = await IpatoolExecution.AuthLogoutAsync(account, _currentOperationCts.Token);
                DisposeCurrentOperation();

                if (result.IsSuccessResponse)
                {
                    SessionState.Reset();
                    HideInlineTwoFactor();
                    ApplyOperationLock(false);
                    ShowSuccess("已退出登录");
                    AppendLoginLog($"Logout success: {account}");
                }
                else
                {
                    ShowError(string.IsNullOrWhiteSpace(result.OutputOrError) ? "退出登录失败" : result.OutputOrError);
                    AppendLoginLog($"Logout failed: {account}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"退出登录失败: {ex.Message}");
                AppendLoginLog($"Logout exception: {ex.Message}");
            }
            finally
            {
                RestoreIdleState();
            }
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
                Debug.WriteLine($"Load account history failed: {ex.Message}");
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

            if (ReferenceEquals(sender, EmailTextBox))
            {
                string accountText = EmailTextBox?.Text.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(accountText))
                {
                    ShowError("邮箱不能为空");
                    EmailTextBox?.Focus(FocusState.Programmatic);
                    return;
                }

                PasswordInput?.Focus(FocusState.Programmatic);
                return;
            }

            if (ReferenceEquals(sender, CodeTextBox))
            {
                if (_isTwoFactorPending)
                {
                    await ValidateAuthCodeAsync();
                }
                else
                {
                    PassphraseInput?.Focus(FocusState.Programmatic);
                }
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

            bool hasAccount = !string.IsNullOrWhiteSpace(_account);
            bool hasPassword = !string.IsNullOrWhiteSpace(_password);
            bool hasPassphrase = !string.IsNullOrWhiteSpace(_passphrase);

            bool hasLocalValidationIssue = false;
            if (!hasAccount || !hasPassword || !hasPassphrase)
            {
                hasLocalValidationIssue = true;
                ShowError("邮箱、密码和加密密钥不能为空");
                AppendLoginLog("Login blocked: required fields are empty.");

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

            _account = EmailTextBox?.Text.Trim() ?? string.Empty;
            _password = PasswordInput?.Password?.Trim() ?? string.Empty;
            _passphrase = PassphraseInput?.Text.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_account) || string.IsNullOrWhiteSpace(_password) || string.IsNullOrWhiteSpace(_passphrase))
            {
                ShowError("邮箱、密码和加密密钥不能为空");
                return false;
            }

            string authCode = CodeTextBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(authCode))
            {
                ShowAuthError("请输入双重验证码");
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
                AppendLoginLog("Code validation failed: invalid code.");
                CodeTextBox.Password = string.Empty;
                CodeTextBox.Focus(FocusState.Programmatic);
                RestoreIdleState();
                return false;
            }

            if (result.Status == LoginStatus.Timeout)
            {
                ShowAuthError(result.Message);
                AppendLoginLog("Code validation failed: timeout.");
                CodeTextBox.Focus(FocusState.Programmatic);
                RestoreIdleState();
                return false;
            }

            if (!result.IsSuccess)
            {
                ShowAuthError(string.IsNullOrWhiteSpace(result.Message) ? "验证失败，请重试。" : result.Message);
                AppendLoginLog("Code validation failed.");
                RestoreIdleState();
                return false;
            }

            await OnLoginSuccessAsync();
            AppendLoginLog("Two-factor code verified.");
            return true;
        }

        private async Task HandleLoginResultAsync(LoginResult result, bool isTwoFactorStep)
        {
            switch (result.Status)
            {
                case LoginStatus.Success:
                    AppendLoginLog("Login success.");
                    await OnLoginSuccessAsync();
                    break;

                case LoginStatus.RequiresTwoFactor:
                    AppendLoginLog("Two-factor code required.");
                    ShowInlineTwoFactor(result.Message);
                    RestoreIdleState();
                    break;

                case LoginStatus.InvalidCredential:
                case LoginStatus.NetworkError:
                case LoginStatus.UnknownError:
                case LoginStatus.Timeout:
                    ShowError(result.Message);
                    AppendLoginLog($"Login failed: {result.Message}");
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
                    AppendLoginLog($"Login failed: {result.Message}");
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

            string fallbackMessage = "请输入两步验证码继续登录。如果未收到验证码，请打开 https://account.apple.com/ 获取后再输入。";
            string finalMessage = string.IsNullOrWhiteSpace(message)
                ? fallbackMessage
                : $"{message} 如未收到验证码，请打开 https://account.apple.com/ 获取后再输入。";
            ShowAuthWarning(finalMessage);
            CodeTextBox?.Focus(FocusState.Programmatic);
        }

        private void HideInlineTwoFactor()
        {
            _isTwoFactorPending = false;

            if (CodeTextBox != null)
            {
                CodeTextBox.Password = string.Empty;
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
                EmailTextBox.IsEnabled = enabled && !_operationLocked;
            }

            if (PasswordInput != null)
            {
                PasswordInput.IsEnabled = enabled && !_operationLocked;
            }

            if (PassphraseInput != null)
            {
                PassphraseInput.IsEnabled = enabled && !_operationLocked;
            }

            if (CodeTextBox != null)
            {
                CodeTextBox.IsEnabled = enabled && !_operationLocked;
            }

            if (AuthInfoButtonControl != null)
            {
                AuthInfoButtonControl.IsEnabled = enabled;
            }

            if (LogoutButtonControl != null)
            {
                LogoutButtonControl.IsEnabled = enabled;
            }

            if (NextButtonControl != null)
            {
                NextButtonControl.IsEnabled = enabled && !_operationLocked;
            }

            if (OpenAppleAccountButtonControl != null)
            {
                OpenAppleAccountButtonControl.IsEnabled = enabled && !_operationLocked;
            }

            if (OperationLockOverlayControl != null)
            {
                OperationLockOverlayControl.Visibility = _operationLocked ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SetBusyState(bool isBusy, string message)
        {
            if (NextButtonControl != null)
            {
                NextButtonControl.IsEnabled = !isBusy && !_operationLocked;
            }

            if (!string.IsNullOrEmpty(message))
            {
                ShowInfo(message);
            }
        }

        private void ShowInfo(string message)
        {
            AppendLoginLog(message);
        }

        private void ShowError(string message)
        {
            AppendLoginLog($"[错误] {message}");
        }

        private void ShowSuccess(string message)
        {
            AppendLoginLog($"[成功] {message}");
        }

        private void ShowAuthError(string message)
        {
            AppendLoginLog($"[验证码错误] {message}");
        }

        private void ShowAuthWarning(string message)
        {
            AppendLoginLog($"[验证码提示] {message}");
        }

        private void HideAuthMessage()
        {
            // 操作区不再显示错误提示，保留方法用于兼容现有调用点。
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
                Debug.WriteLine($"Save account info failed: {ex.Message}");
            }

            HideInlineTwoFactor();
            bool isMockAccount = KeychainConfig.IsMockAccount(_account, _password);
            SessionState.SetLoginState(_account, true, isMockAccount);
            ApplyOperationLock(true);
            DisposeCurrentOperation();
            ShowSuccess("Login successful");
            AppendLoginLog($"Login account: {_account}");
            return Task.CompletedTask;
        }

        private void ApplyOperationLock(bool isLocked)
        {
            _operationLocked = isLocked;
            SetInputControlsEnabled(true);
        }

        private void OpenAppleAccountButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://account.apple.com/",
                    UseShellExecute = true
                });
                AppendLoginLog("已打开苹果账户官网。");
            }
            catch (Exception ex)
            {
                ShowError($"打开苹果账户官网失败: {ex.Message}");
            }
        }

        private void CopyLoginLog_Click(object sender, RoutedEventArgs e)
        {
            string text = LoginLogOutput?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendLoginLog("Log is empty; nothing to copy.");
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendLoginLog("Log copied to clipboard.");
        }

        private void ClearLoginLog_Click(object sender, RoutedEventArgs e)
        {
            _loginLogBuilder.Clear();
            if (LoginLogOutput != null)
            {
                LoginLogOutput.Text = string.Empty;
            }

            AppendLoginLog("Log cleared.");
        }

        private void AppendLoginLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || LoginLogOutput == null)
            {
                return;
            }

            _loginLogBuilder.Append('[')
                .Append(DateTime.Now.ToString("HH:mm:ss"))
                .Append("] ")
                .AppendLine(message);

            LoginLogOutput.Text = _loginLogBuilder.ToString();
            LoginLogOutput.SelectionStart = LoginLogOutput.Text.Length;
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

        private TextBox? EmailTextBox => GetControl<TextBox>("EmailComboBox");
        private PasswordBox? PasswordInput => GetControl<PasswordBox>("PasswordBox");
        private TextBox? PassphraseInput => GetControl<TextBox>("PassphraseBox");
        private Button? NextButtonControl => GetControl<Button>("NextButton");
        private Button? AuthInfoButtonControl => GetControl<Button>("AuthInfoButton");
        private Button? LogoutButtonControl => GetControl<Button>("LogoutButton");
        private Button? OpenAppleAccountButtonControl => GetControl<Button>("OpenAppleAccountButton");
        private PasswordBox? CodeTextBox => GetControl<PasswordBox>("CodeBox");
        private StackPanel? AuthCodeInlinePanelControl => GetControl<StackPanel>("AuthCodeInlinePanel");
        private Border? OperationLockOverlayControl => GetControl<Border>("OperationLockOverlay");

        private T? GetControl<T>(string name)
            where T : class
        {
            return FindName(name) as T;
        }
    }
}
