using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IPAbuyer.Common
{
    internal static class JsonPayload
    {
        public static IEnumerable<JToken> EnumerateTokens(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                yield break;
            }

            string trimmedPayload = payload.Trim();
            if (LooksLikeJson(trimmedPayload) && TryParseToken(trimmedPayload, out JToken? fullToken) && fullToken != null)
            {
                yield return fullToken;
                yield break;
            }

            string normalized = payload.Replace("}{", "}\n{");
            string[] lines = normalized.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (TryParseToken(trimmed, out JToken? token) && token != null)
                {
                    yield return token;
                    continue;
                }

                string? jsonCandidate = FindEmbeddedJsonCandidate(trimmed);
                if (jsonCandidate != null && TryParseToken(jsonCandidate, out JToken? embeddedToken) && embeddedToken != null)
                {
                    yield return embeddedToken;
                }
            }

            if (lines.Length == 0)
            {
                string trimmed = trimmedPayload;
                if (LooksLikeJson(trimmed) && TryParseToken(trimmed, out JToken? token) && token != null)
                {
                    yield return token;
                }
            }
        }

        public static bool TryParseToken(string? json, out JToken? token)
        {
            token = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                token = JToken.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static bool TryReadBoolean(JToken token, string name, out bool value)
        {
            value = false;
            if (token is not JObject obj)
            {
                return false;
            }

            JToken? child = obj[name];
            if (child == null || child.Type == JTokenType.Null)
            {
                return false;
            }

            if (child.Type == JTokenType.Boolean)
            {
                value = child.Value<bool>();
                return true;
            }

            string? text = child.Type == JTokenType.String
                ? child.Value<string>()
                : child.ToString();
            if (bool.TryParse(text, out bool parsed))
            {
                value = parsed;
                return true;
            }

            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
            {
                value = number != 0m;
                return true;
            }

            return false;
        }

        public static bool TryReadString(JToken token, out string? value, params string[] names)
        {
            if (token is not JObject obj)
            {
                value = null;
                return false;
            }

            foreach (string name in names)
            {
                JToken? child = obj[name];
                if (child == null || child.Type == JTokenType.Null)
                {
                    continue;
                }

                value = child.Type == JTokenType.String
                    ? child.Value<string>()
                    : child.ToString(Formatting.None);
                return true;
            }

            value = null;
            return false;
        }

        public static string? ReadScalarAsString(JToken token)
        {
            return token.Type switch
            {
                JTokenType.Null => null,
                JTokenType.String => token.Value<string>(),
                JTokenType.Float when token.Value<decimal>() <= 0m => "0.00",
                JTokenType.Float => token.Value<decimal>().ToString("0.00", CultureInfo.InvariantCulture),
                JTokenType.Integer => token.ToString(Formatting.None),
                JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
                _ => token.ToString(Formatting.None)
            };
        }

        private static bool LooksLikeJson(string value)
        {
            return value.StartsWith("{", StringComparison.Ordinal) || value.StartsWith("[", StringComparison.Ordinal);
        }

        private static string? FindEmbeddedJsonCandidate(string value)
        {
            int objectIndex = value.IndexOf('{');
            if (objectIndex >= 0)
            {
                return value.Substring(objectIndex).Trim();
            }

            int arrayIndex = value.IndexOf('[');
            return arrayIndex >= 0 ? value.Substring(arrayIndex).Trim() : null;
        }
    }
}
