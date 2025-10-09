using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        public static Task<LoginResult> LoginAsync(string account, string password, CancellationToken cancellationToken)
        {
            return ExecuteLoginAsync(account, password, "000000", cancellationToken, isTwoFactor: false);
        }

        public static Task<LoginResult> VerifyAuthCodeAsync(string account, string password, string authCode, CancellationToken cancellationToken)
        {
            return ExecuteLoginAsync(account, password, authCode, cancellationToken, isTwoFactor: true);
        }

        private static async Task<LoginResult> ExecuteLoginAsync(string account, string password, string authCode, CancellationToken cancellationToken, bool isTwoFactor)
        {
            try
            {
                var response = await ipatoolExecution.AuthLoginAsync(account, password, authCode, cancellationToken).ConfigureAwait(false);

                if (response.TimedOut)
                {
                    return new LoginResult(LoginStatus.Timeout, "登录请求超时，请稍后重试。");
                }

                string payload = response.OutputOrError ?? string.Empty;
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return new LoginResult(LoginStatus.UnknownError, "未收到登录响应，请稍后重试。", payload);
                }

                return InterpretPayload(payload, isTwoFactor);
            }
            catch (OperationCanceledException)
            {
                return new LoginResult(LoginStatus.UnknownError, "登录已取消。");
            }
            catch (Exception ex)
            {
                return new LoginResult(LoginStatus.UnknownError, $"登录失败: {ex.Message}");
            }
        }

        private static LoginResult InterpretPayload(string payload, bool isTwoFactor)
        {
            foreach (string segment in EnumerateJsonSegments(payload))
            {
                var result = InterpretJsonSegment(segment, isTwoFactor);
                if (result != null)
                {
                    return result;
                }
            }

            if (DetectTwoFactorRequirement(payload))
            {
                return new LoginResult(LoginStatus.RequiresTwoFactor, "需要输入两步验证码。", payload);
            }

            return ClassifyFailure(payload, payload, isTwoFactor);
        }

        private static LoginResult? InterpretJsonSegment(string segment, bool isTwoFactor)
        {
            try
            {
                using var document = JsonDocument.Parse(segment);
                var root = document.RootElement;

                if (root.TryGetProperty("success", out var successElement))
                {
                    bool success = successElement.ValueKind == JsonValueKind.True && successElement.GetBoolean();
                    if (success)
                    {
                        return new LoginResult(LoginStatus.Success, "登录成功。", segment);
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
                    return new LoginResult(LoginStatus.RequiresTwoFactor, "需要输入两步验证码。", segment);
                }
            }
            catch (JsonException)
            {
                // 忽略，尝试下一个片段
            }

            return null;
        }

        private static IEnumerable<string> EnumerateJsonSegments(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                yield break;
            }

            string normalized = payload.Replace("}{", "}\n{");
            string[] lines = normalized.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                {
                    yield return trimmed;
                }
            }

            if (lines.Length == 0)
            {
                string trimmed = payload.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    yield return trimmed;
                }
            }
        }

        private static string ExtractErrorMessage(JsonElement root)
        {
            if (root.TryGetProperty("error", out var errorElement))
            {
                return errorElement.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("reason", out var reason))
            {
                return reason.GetString() ?? string.Empty;
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
                return new LoginResult(LoginStatus.RequiresTwoFactor, "需要输入两步验证码。", payload);
            }

            if (DetectInvalidCredential(message))
            {
                return new LoginResult(LoginStatus.InvalidCredential, "用户名或密码不正确。", payload);
            }

            if (DetectAuthCodeInvalid(message) && isTwoFactor)
            {
                return new LoginResult(LoginStatus.AuthCodeInvalid, "验证码错误，请重新输入。", payload);
            }

            if (DetectNetworkIssue(message))
            {
                return new LoginResult(LoginStatus.NetworkError, "网络异常，请稍后重试。", payload);
            }

            return new LoginResult(LoginStatus.UnknownError, string.IsNullOrWhiteSpace(message) ? "登录失败，请稍后重试。" : message, payload);
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
                || message.Contains("authentication code");
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
    }
}
