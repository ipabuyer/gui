using IPAbuyer.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IPAbuyer.Views
{
    public sealed partial class LoginPage : Page
    {
        private static readonly ResourceLoader Loader = new();
        private CancellationTokenSource _pageCts = new();
        private readonly StringBuilder _loginLogBuilder = new();
        private readonly System.Collections.Generic.List<UiLogEntry> _loginLogEntries = new();
        private CancellationTokenSource? _currentOperationCts;

        private string _account = "example@icloud.com";
        private string _password = string.Empty;
        private string _passphrase = string.Empty;
        private bool _isTwoFactorPending;
        private bool _operationLocked;
        private bool _isLoginLogDialogOpen;
        private const int MaxLogLines = 1000;

        public LoginPage()
        {
            InitializeComponent();

            string defaultPassphrase = KeychainConfig.GetPassphrase(null) ?? KeychainConfig.GetDefaultPassphrase();
            if (PassphraseInput != null)
            {
                PassphraseInput.Text = defaultPassphrase;
                _passphrase = defaultPassphrase;
            }

            Unloaded += LoginPage_Unloaded;
            Loaded += LoginPage_Loaded;
        }

        private void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            IpatoolExecution.CommandExecuting -= OnIpatoolCommandExecuting;
            IpatoolExecution.CommandExecuting += OnIpatoolCommandExecuting;
            IpatoolExecution.CommandOutputReceived -= OnIpatoolCommandOutputReceived;
            IpatoolExecution.CommandOutputReceived += OnIpatoolCommandOutputReceived;
            SessionState.LoginStateChanged -= OnSessionStateChanged;
            SessionState.LoginStateChanged += OnSessionStateChanged;

            if (_pageCts.IsCancellationRequested)
            {
                _pageCts.Dispose();
                _pageCts = new CancellationTokenSource();
            }

            RefreshFromSessionState();
        }

        private void LoginPage_Unloaded(object sender, RoutedEventArgs e)
        {
            IpatoolExecution.CommandExecuting -= OnIpatoolCommandExecuting;
            IpatoolExecution.CommandOutputReceived -= OnIpatoolCommandOutputReceived;
            SessionState.LoginStateChanged -= OnSessionStateChanged;
            CancelCurrentOperation();
            if (!_pageCts.IsCancellationRequested)
            {
                _pageCts.Cancel();
            }
        }

        private void OnSessionStateChanged()
        {
            DispatcherQueue.TryEnqueue(RefreshFromSessionState);
        }

        private void RefreshFromSessionState()
        {
            string account = SessionState.CurrentAccount;
            if (EmailTextBox != null)
            {
                EmailTextBox.Text = account ?? string.Empty;
            }

            ApplyOperationLock(SessionState.IsLoggedIn);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isTwoFactorPending)
                {
                    AppendLoginLog(L("LoginPage/Log/StartVerifyTwoFactor"), UiLogLevel.Info);
                    await ValidateAuthCodeAsync();
                    return;
                }

                AppendLoginLog(L("LoginPage/Log/StartLogin"), UiLogLevel.Info);
                await TriggerLoginAsync();
            }
            catch (Exception ex)
            {
                ShowError(LF("LoginPage/Status/LoginFlowException", ex.Message));
                AppendLoginLog(LF("LoginPage/Log/LoginFlowException", ex.Message), UiLogLevel.Error);
                RestoreIdleState();
            }
        }

        private async void AuthInfoButton_Click(object sender, RoutedEventArgs e)
        {
            await QueryAuthInfoAsync();
        }

        private async Task QueryAuthInfoAsync()
        {
            string account = SessionState.CurrentAccount;
            bool wasLoggedIn = SessionState.IsLoggedIn;

            try
            {
                CancelCurrentOperation();
                _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
                SetBusyState(true, L("LoginPage/Status/QueryingAuthInfo"));

                string inputPassphrase = PassphraseInput?.Text?.Trim() ?? string.Empty;
                var result = await IpatoolExecution.AuthInfoAsync(
                    passphrase: string.IsNullOrWhiteSpace(inputPassphrase) ? null : inputPassphrase,
                    cancellationToken: _currentOperationCts.Token);
                DisposeCurrentOperation();

                string payloadEmail = IpatoolExecution.ExtractEmailFromPayload(result.OutputOrError);
                bool isAuthSuccess = result.IsSuccessResponse
                    && !IpatoolExecution.HasExplicitFailureFlag(result.OutputOrError)
                    && (IpatoolExecution.IsPayloadSuccess(result.OutputOrError) || !string.IsNullOrWhiteSpace(payloadEmail));

                if (isAuthSuccess)
                {
                    if (!string.IsNullOrWhiteSpace(payloadEmail))
                    {
                        account = payloadEmail;
                        if (EmailTextBox != null)
                        {
                            EmailTextBox.Text = payloadEmail;
                        }
                    }

                    string activeAccount = string.IsNullOrWhiteSpace(payloadEmail) ? account : payloadEmail;
                    if (!string.IsNullOrWhiteSpace(activeAccount))
                    {
                        SessionState.SetLoginState(activeAccount, true);
                        ApplyOperationLock(true);
                        ShowSuccess(L("LoginPage/Status/AuthInfoSuccess"));
                        AppendLoginLog(LF("LoginPage/Log/AuthInfoSuccess", activeAccount), UiLogLevel.Success);
                    }
                    else
                    {
                        SessionState.Reset();
                        ApplyOperationLock(false);
                        ShowError(L("LoginPage/Status/AuthInfoEmailMissing"));
                        AppendLoginLog(L("LoginPage/Log/AuthInfoEmailMissing"), UiLogLevel.Error);
                    }
                }
                else
                {
                    ApplyOperationLock(wasLoggedIn);
                    ShowError(string.IsNullOrWhiteSpace(result.OutputOrError) ? L("LoginPage/Status/AuthInfoFailed") : result.OutputOrError);
                    AppendLoginLog(L("LoginPage/Log/AuthInfoFailed"), UiLogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                ApplyOperationLock(wasLoggedIn);
                ShowError(LF("LoginPage/Status/AuthInfoException", ex.Message));
                AppendLoginLog(LF("LoginPage/Log/AuthInfoException", ex.Message), UiLogLevel.Error);
            }
            finally
            {
                DisposeCurrentOperation();
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

            try
            {
                CancelCurrentOperation();
                _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
                SetBusyState(true, L("LoginPage/Status/LoggingOut"));

                var result = await IpatoolExecution.AuthLogoutAsync(_currentOperationCts.Token);
                DisposeCurrentOperation();

                if (result.IsSuccessResponse)
                {
                    SessionState.Reset();
                    if (KeychainConfig.GetKeychainPassphraseRotationEnabled())
                    {
                        _passphrase = KeychainConfig.RotateDefaultPassphrase();
                        if (PassphraseInput != null)
                        {
                            PassphraseInput.Text = _passphrase;
                        }

                        AppendLoginLog(L("LoginPage/Log/PassphraseRotated"), UiLogLevel.Success);
                    }

                    HideInlineTwoFactor();
                    ApplyOperationLock(false);
                    ShowSuccess(L("LoginPage/Status/LogoutSuccess"));
                    AppendLoginLog(LF("LoginPage/Log/LogoutSuccess", account), UiLogLevel.Success);
                }
                else
                {
                    ShowError(string.IsNullOrWhiteSpace(result.OutputOrError) ? L("LoginPage/Status/LogoutFailed") : result.OutputOrError);
                    AppendLoginLog(LF("LoginPage/Log/LogoutFailed", account), UiLogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                ShowError(LF("LoginPage/Status/LogoutException", ex.Message));
                AppendLoginLog(LF("LoginPage/Log/LogoutException", ex.Message), UiLogLevel.Error);
            }
            finally
            {
                DisposeCurrentOperation();
                RestoreIdleState();
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
                    ShowError(L("LoginPage/Status/EmailRequired"));
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
                ShowError(L("LoginPage/Status/RequiredFieldsEmpty"));
                AppendLoginLog(L("LoginPage/Log/RequiredFieldsEmpty"), UiLogLevel.Error);

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

            if (hasLocalValidationIssue)
            {
                RestoreIdleState();
                DisposeCurrentOperation();
                return;
            }

            SetInputControlsEnabled(false);
            SetBusyState(true, hasLocalValidationIssue ? string.Empty : L("LoginPage/Status/LoggingIn"));

            try
            {
                var result = await LoginService.LoginAsync(_account, _password, _passphrase, _currentOperationCts.Token);
                DisposeCurrentOperation();
                await HandleLoginResultAsync(result, isTwoFactorStep: false);
            }
            catch (OperationCanceledException)
            {
                DisposeCurrentOperation();
                AppendLoginLog(L("LoginPage/Log/LoginCanceled"), UiLogLevel.Tip);
                RestoreIdleState();
            }
            catch (Exception ex)
            {
                DisposeCurrentOperation();
                ShowError(LF("LoginPage/Status/LoginException", ex.Message));
                AppendLoginLog(LF("LoginPage/Log/LoginException", ex.Message), UiLogLevel.Error);
                RestoreIdleState();
            }
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
                ShowError(L("LoginPage/Status/RequiredFieldsEmpty"));
                return false;
            }

            string authCode = CodeTextBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(authCode))
            {
                authCode = "000000";
                AppendLoginLog(L("LoginPage/Log/EmptyAuthCodeUsesDefault"), UiLogLevel.Tip);
            }

            HideAuthMessage();

            CancelCurrentOperation();
            _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);

            SetInputControlsEnabled(false);
            SetBusyState(true, L("LoginPage/Status/Verifying"));

            LoginResult result;
            try
            {
                result = await LoginService.VerifyAuthCodeAsync(_account, _password, _passphrase, authCode, _currentOperationCts.Token);
                DisposeCurrentOperation();
            }
            catch (OperationCanceledException)
            {
                DisposeCurrentOperation();
                AppendLoginLog(L("LoginPage/Log/CodeValidationCanceled"), UiLogLevel.Tip);
                RestoreIdleState();
                return false;
            }
            catch (Exception ex)
            {
                DisposeCurrentOperation();
                ShowAuthError(LF("LoginPage/Status/VerifyException", ex.Message));
                AppendLoginLog(LF("LoginPage/Log/CodeValidationException", ex.Message), UiLogLevel.Error);
                RestoreIdleState();
                return false;
            }

            if (result.Status == LoginStatus.AuthCodeInvalid)
            {
                ShowAuthError(L("LoginPage/Status/AuthCodeInvalid"));
                AppendLoginLog(L("LoginPage/Log/CodeValidationInvalid"), UiLogLevel.Error);
                CodeTextBox.Password = string.Empty;
                CodeTextBox.Focus(FocusState.Programmatic);
                RestoreIdleState();
                return false;
            }

            if (result.Status == LoginStatus.Timeout)
            {
                ShowAuthError(result.Message);
                AppendLoginLog(L("LoginPage/Log/CodeValidationTimeout"), UiLogLevel.Error);
                CodeTextBox.Focus(FocusState.Programmatic);
                RestoreIdleState();
                return false;
            }

            if (!result.IsSuccess)
            {
                ShowAuthError(string.IsNullOrWhiteSpace(result.Message) ? L("LoginPage/Status/VerifyFailedRetry") : result.Message);
                AppendLoginLog(L("LoginPage/Log/CodeValidationFailed"), UiLogLevel.Error);
                RestoreIdleState();
                return false;
            }

            await OnLoginSuccessAsync();
            AppendLoginLog(L("LoginPage/Log/CodeValidationSuccess"), UiLogLevel.Success);
            return true;
        }

        private async Task HandleLoginResultAsync(LoginResult result, bool isTwoFactorStep)
        {
            switch (result.Status)
            {
                case LoginStatus.Success:
                    AppendLoginLog(L("LoginPage/Log/LoginSuccess"), UiLogLevel.Success);
                    await OnLoginSuccessAsync();
                    break;

                case LoginStatus.RequiresTwoFactor:
                    AppendLoginLog(L("LoginPage/Log/TwoFactorRequired"), UiLogLevel.Tip);
                    ShowInlineTwoFactor(result.Message);
                    RestoreIdleState();
                    break;

                case LoginStatus.InvalidCredential:
                case LoginStatus.NetworkError:
                case LoginStatus.UnknownError:
                case LoginStatus.Timeout:
                    ShowError(result.Message);
                    AppendLoginLog(LF("LoginPage/Log/LoginFailed", result.Message), UiLogLevel.Error);
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
                    AppendLoginLog(LF("LoginPage/Log/LoginFailed", result.Message), UiLogLevel.Error);
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

            string fallbackMessage = L("LoginPage/Status/TwoFactorPromptFallback");
            string finalMessage = string.IsNullOrWhiteSpace(message)
                ? fallbackMessage
                : LF("LoginPage/Status/TwoFactorPromptWithMessage", message);
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
            ShowStatus(message, InfoBarSeverity.Informational);
            AppendLoginLog(message, UiLogLevel.Info);
        }

        private void ShowError(string message)
        {
            ShowStatus(message, InfoBarSeverity.Error);
            AppendLoginLog(LF("LoginPage/Log/ErrorPrefixFormat", message), UiLogLevel.Error);
        }

        private void ShowSuccess(string message)
        {
            ShowStatus(message, InfoBarSeverity.Success);
            AppendLoginLog(LF("LoginPage/Log/SuccessPrefixFormat", message), UiLogLevel.Success);
        }

        private void ShowAuthError(string message)
        {
            ShowStatus(message, InfoBarSeverity.Error);
            AppendLoginLog(LF("LoginPage/Log/AuthErrorPrefixFormat", message), UiLogLevel.Error);
        }

        private void ShowAuthWarning(string message)
        {
            ShowStatus(message, InfoBarSeverity.Warning);
            AppendLoginLog(LF("LoginPage/Log/AuthWarningPrefixFormat", message), UiLogLevel.Tip);
        }

        private void HideAuthMessage()
        {
            if (LoginStatusInfoBar != null)
            {
                LoginStatusInfoBar.IsOpen = false;
            }
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            if (LoginStatusInfoBar == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            LoginStatusInfoBar.Message = message;
            LoginStatusInfoBar.Severity = severity;
            LoginStatusInfoBar.IsOpen = true;
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
                KeychainConfig.SavePassphrase(_passphrase);
                if (EmailTextBox != null)
                {
                    EmailTextBox.Text = _account;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LF("LoginPage/Debug/SaveAccountInfoFailed", ex.Message));
            }

            HideInlineTwoFactor();
            bool isMockAccount = KeychainConfig.IsMockAccount(_account, _password);
            SessionState.SetLoginState(_account, true, isMockAccount);
            ApplyOperationLock(true);
            DisposeCurrentOperation();
            ShowSuccess(L("LoginPage/Status/LoginSuccess"));
            AppendLoginLog(LF("LoginPage/Log/LoginAccount", _account), UiLogLevel.Success);
            return Task.CompletedTask;
        }

        private void ApplyOperationLock(bool isLocked)
        {
            _operationLocked = isLocked;
            SetInputControlsEnabled(true);
        }

        private void OperationLockOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ShowInfo(L("LoginPage/Status/OperationLocked"));
            e.Handled = true;
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
                AppendLoginLog(L("LoginPage/Log/AppleAccountSiteOpened"), UiLogLevel.Success);
            }
            catch (Exception ex)
            {
                ShowError(LF("LoginPage/Status/OpenAppleAccountSiteFailed", ex.Message));
            }
        }

        private async void ShowLoginLogDialog_Click(object sender, RoutedEventArgs e)
        {
            await TryShowLoginLogDialogAsync();
        }

        private async Task TryShowLoginLogDialogAsync()
        {
            if (_isLoginLogDialogOpen || XamlRoot == null)
            {
                return;
            }

            _isLoginLogDialogOpen = true;
            try
            {
                var dialog = new LogViewerDialog(
                    _loginLogEntries,
                    GetLogColor,
                    CopyLoginLog,
                    ClearLoginLog,
                    XamlRoot);

                await dialog.ShowAsync();
            }
            finally
            {
                _isLoginLogDialogOpen = false;
            }
        }

        private void CopyLoginLog()
        {
            string text = _loginLogBuilder.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendLoginLog(L("LoginPage/Log/CopyEmptyLog"), UiLogLevel.Tip);
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            AppendLoginLog(L("LoginPage/Log/CopiedToClipboard"), UiLogLevel.Success);
        }

        private void ClearLoginLog()
        {
            _loginLogBuilder.Clear();
            _loginLogEntries.Clear();
            AppendLoginLog(L("LoginPage/Log/Cleared"), UiLogLevel.Info);
        }

        private void AppendLoginLog(string message, UiLogLevel level, UiLogSource source = UiLogSource.App)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            UiLogEntry entry = UiLogFormatter.Build(message, level);
            _loginLogEntries.Add(entry);
            if (_loginLogEntries.Count > MaxLogLines)
            {
                _loginLogEntries.RemoveAt(0);
            }

            RebuildLoginLogText();
        }

        private void RebuildLoginLogText()
        {
            _loginLogBuilder.Clear();
            foreach (UiLogEntry entry in _loginLogEntries)
            {
                _loginLogBuilder.AppendLine(entry.FormattedText);
            }
        }

        private Windows.UI.Color GetLogColor(UiLogLevel level)
        {
            return level switch
            {
                UiLogLevel.Tip => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xD5, 0x8A),
                UiLogLevel.Success => Windows.UI.Color.FromArgb(0xFF, 0x8D, 0xE6, 0x9A),
                UiLogLevel.Error => Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x99, 0x99),
                UiLogLevel.Ipatool => Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xC8, 0xFF),
                _ => Windows.UI.Color.FromArgb(0xFF, 0xE6, 0xE6, 0xE6)
            };
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

        private void OnIpatoolCommandExecuting(string command)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendLoginLog(command, UiLogLevel.Ipatool, UiLogSource.Ipatool);
            });
        }

        private void OnIpatoolCommandOutputReceived(string line)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppendLoginLog(line, UiLogLevel.Ipatool, UiLogSource.Ipatool);
            });
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

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }

        private T? GetControl<T>(string name)
            where T : class
        {
            return FindName(name) as T;
        }
    }
}
