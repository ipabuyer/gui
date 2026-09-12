using IPAbuyer.Core.Configuration;
using IPAbuyer.Core.Integration.Ipatool;
using IPAbuyer.Core.Native;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace IPAbuyer.Core.Services.Authentication
{
    public enum LoginStatus
    {
        Success,
        RequiresTwoFactor,
        InvalidCredential,
        AuthCodeInvalid,
        NetworkError,
        Timeout,
        UnknownError
    }

    public sealed record LoginResult(LoginStatus Status, string Message, string? RawPayload = null)
    {
        public bool IsSuccess => Status == LoginStatus.Success;
        public bool RequiresTwoFactor => Status == LoginStatus.RequiresTwoFactor;
        public bool IsTimeout => Status == LoginStatus.Timeout;
    }

    /// <summary>
    /// 登录流程门面：占位验证码触发双重验证、验证码完成登录。
    /// 命令执行与结果分类在 Rust core（ipabuyer_core_auth_login / auth_verify_code）；
    /// 模拟账户（test/test）由 Core 侧识别并直接成功。
    /// </summary>
    public static class LoginService
    {
        private static readonly ResourceLoader Loader = new();

        public static Task<LoginResult> LoginAsync(string account, string password, string passphrase, CancellationToken cancellationToken)
        {
            return ExecuteLoginAsync(account, password, passphrase, "000000", cancellationToken, isTwoFactor: false);
        }

        public static Task<LoginResult> VerifyAuthCodeAsync(string account, string password, string passphrase, string authCode, CancellationToken cancellationToken)
        {
            return ExecuteLoginAsync(account, password, passphrase, authCode, cancellationToken, isTwoFactor: true);
        }

        private static async Task<LoginResult> ExecuteLoginAsync(string account, string password, string passphrase, string authCode, CancellationToken cancellationToken, bool isTwoFactor)
        {
            string executablePath = IpatoolPathResolver.ResolveExecutablePath();
            string resolvedPassphrase = IpatoolClient.ResolvePassphrase(passphrase);
            bool detailedLog = ApplicationSettings.GetDetailedIpatoolLogEnabled();

            try
            {
                CoreLoginResult result = await Task.Run(() =>
                {
                    using var cancel = new CoreCancelFlag();
                    using CancellationTokenRegistration registration = cancellationToken.Register(cancel.Cancel);
                    return isTwoFactor
                        ? CoreNative.AuthVerifyCode(executablePath, account, password, authCode, resolvedPassphrase, detailedLog, cancel.Handle)
                        : CoreNative.AuthLogin(executablePath, account, password, authCode, resolvedPassphrase, detailedLog, cancel.Handle);
                }).ConfigureAwait(false);

                IpatoolClient.EmitCoreLogs(result.Logs);
                return MapResult(result);
            }
            catch (OperationCanceledException)
            {
                return new LoginResult(LoginStatus.UnknownError, L("LoginService/Status/Canceled"));
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return new LoginResult(LoginStatus.UnknownError, L("LoginService/Status/Canceled"));
            }
            catch (Exception ex)
            {
                return new LoginResult(LoginStatus.UnknownError, LF("LoginService/Status/Exception", ex.Message));
            }
        }

        private static LoginResult MapResult(CoreLoginResult result)
        {
            LoginStatus status = result.Status switch
            {
                "Success" => LoginStatus.Success,
                "RequiresTwoFactor" => LoginStatus.RequiresTwoFactor,
                "InvalidCredential" => LoginStatus.InvalidCredential,
                "AuthCodeInvalid" => LoginStatus.AuthCodeInvalid,
                "NetworkError" => LoginStatus.NetworkError,
                "Timeout" => LoginStatus.Timeout,
                _ => LoginStatus.UnknownError,
            };

            return new LoginResult(status, CoreMessages.Render(result.Message), result.RawPayload);
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key), args);
        }
    }
}
