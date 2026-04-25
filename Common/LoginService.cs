using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.ApplicationModel.Resources;
using Newtonsoft.Json.Linq;

namespace IPAbuyer.Common
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

    public static class LoginService
    {
        private static readonly ResourceLoader Loader = new();
        private static readonly TimeSpan TestLoginDelay = TimeSpan.FromMilliseconds(1000);

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
            if (KeychainConfig.IsMockAccount(account, password))
            {
                try
                {
                    await Task.Delay(TestLoginDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new LoginResult(LoginStatus.UnknownError, L("LoginService/Status/Canceled"));
                }

                return new LoginResult(LoginStatus.Success, L("LoginService/Status/Success"));
            }

            try
            {
                var response = await IpatoolExecution.AuthLoginAsync(account, password, authCode, passphrase, cancellationToken).ConfigureAwait(false);

                if (response.TimedOut)
                {
                    return new LoginResult(LoginStatus.Timeout, L("LoginService/Status/Timeout"));
                }

                string payload = response.OutputOrError ?? string.Empty;
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return new LoginResult(LoginStatus.UnknownError, L("LoginService/Status/EmptyResponse"), payload);
                }

                return InterpretPayload(payload, isTwoFactor);
            }
            catch (OperationCanceledException)
            {
                return new LoginResult(LoginStatus.UnknownError, L("LoginService/Status/Canceled"));
            }
            catch (Exception ex)
            {
                return new LoginResult(LoginStatus.UnknownError, LF("LoginService/Status/Exception", ex.Message));
            }
        }

        private static LoginResult InterpretPayload(string payload, bool isTwoFactor)
        {
            foreach (JToken token in JsonPayload.EnumerateTokens(payload))
            {
                string segment = token.ToString(Newtonsoft.Json.Formatting.None);
                var result = InterpretJsonSegment(token, segment, isTwoFactor);
                if (result != null)
                {
                    return result;
                }
            }

            if (DetectTwoFactorRequirement(payload))
            {
                return new LoginResult(LoginStatus.RequiresTwoFactor, L("LoginService/Status/RequiresTwoFactor"), payload);
            }

            return ClassifyFailure(payload, payload, isTwoFactor);
        }

        private static LoginResult? InterpretJsonSegment(JToken root, string segment, bool isTwoFactor)
        {
            if (JsonPayload.TryReadBoolean(root, "success", out bool success))
            {
                if (success)
                {
                    return new LoginResult(LoginStatus.Success, L("LoginService/Status/Success"), segment);
                }

                string error = ExtractErrorMessage(root);
                return ClassifyFailure(error, segment, isTwoFactor);
            }

            string message = ExtractErrorMessage(root);
            if (!string.IsNullOrWhiteSpace(message))
            {
                var failure = ClassifyFailure(message, segment, isTwoFactor);
                if (failure.Status != LoginStatus.UnknownError)
                {
                    return failure;
                }
            }

            if (DetectTwoFactorRequirement(segment))
            {
                return new LoginResult(LoginStatus.RequiresTwoFactor, L("LoginService/Status/RequiresTwoFactor"), segment);
            }

            return null;
        }

        private static string ExtractErrorMessage(JToken root)
        {
            if (JsonPayload.TryReadString(root, out string? message, "error", "message", "reason"))
            {
                return message ?? string.Empty;
            }

            return string.Empty;
        }

        private static LoginResult ClassifyFailure(string message, string payload, bool isTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                message = payload;
            }

            if (DetectTwoFactorRequirement(message))
            {
                return new LoginResult(LoginStatus.RequiresTwoFactor, L("LoginService/Status/RequiresTwoFactor"), payload);
            }

            if (DetectInvalidCredential(message))
            {
                return new LoginResult(LoginStatus.InvalidCredential, L("LoginService/Status/InvalidCredential"), payload);
            }

            if (DetectAuthCodeInvalid(message) && isTwoFactor)
            {
                return new LoginResult(LoginStatus.AuthCodeInvalid, L("LoginService/Status/AuthCodeInvalid"), payload);
            }

            if (DetectNetworkIssue(message))
            {
                return new LoginResult(LoginStatus.NetworkError, L("LoginService/Status/NetworkError"), payload);
            }

            return new LoginResult(LoginStatus.UnknownError, string.IsNullOrWhiteSpace(message) ? L("LoginService/Status/FailedRetry") : message, payload);
        }

        private static bool DetectTwoFactorRequirement(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            message = message.ToLowerInvariant();
            return message.Contains("auth code")
                || message.Contains("two factor")
                || message.Contains("2fa")
                || message.Contains("请输入验证码")
                || message.Contains("authentication code")
                || message.Contains("something went wrong");
        }

        private static bool DetectInvalidCredential(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            message = message.ToLowerInvariant();
            return message.Contains("invalid credentials")
                || message.Contains("incorrect")
                || message.Contains("username or password")
                || message.Contains("bad credentials");
        }

        private static bool DetectAuthCodeInvalid(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            message = message.ToLowerInvariant();
            return message.Contains("invalid auth code")
                || message.Contains("auth code is incorrect")
                || message.Contains("验证码错误");
        }

        private static bool DetectNetworkIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            message = message.ToLowerInvariant();
            return message.Contains("network")
                || message.Contains("timeout")
                || message.Contains("timed out")
                || message.Contains("connection")
                || message.Contains("ssl");
        }

        private static string L(string key)
        {
            return Loader.GetString(key);
        }

        private static string LF(string key, params object[] args)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, L(key), args);
        }

    }
}
