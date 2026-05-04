using System.Globalization;
using System.Text.Json;

namespace IPAbuyer.Common
{
    internal static class JsonPayload
    {
        public static IEnumerable<JsonElement> EnumerateTokens(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                yield break;
            }

            string trimmedPayload = payload.Trim();
            if (LooksLikeJson(trimmedPayload) && TryParseToken(trimmedPayload, out JsonElement fullToken))
            {
                yield return fullToken;
                yield break;
            }

            string normalized = payload.Replace("}{", "}\n{");
            string[] lines = normalized.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (TryParseToken(trimmed, out JsonElement token))
                {
                    yield return token;
                    continue;
                }

                string? jsonCandidate = FindEmbeddedJsonCandidate(trimmed);
                if (jsonCandidate != null && TryParseToken(jsonCandidate, out JsonElement embeddedToken))
                {
                    yield return embeddedToken;
                }
            }

            if (lines.Length == 0)
            {
                string trimmed = trimmedPayload;
                if (LooksLikeJson(trimmed) && TryParseToken(trimmed, out JsonElement token))
                {
                    yield return token;
                }
            }
        }

        public static bool TryParseToken(string? json, out JsonElement token)
        {
            token = default;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                token = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static bool TryReadBoolean(JsonElement token, string name, out bool value)
        {
            value = false;
            if (token.ValueKind != JsonValueKind.Object || !TryGetProperty(token, name, out JsonElement child))
            {
                return false;
            }

            if (child.ValueKind == JsonValueKind.Null || child.ValueKind == JsonValueKind.Undefined)
            {
                return false;
            }

            if (child.ValueKind == JsonValueKind.True || child.ValueKind == JsonValueKind.False)
            {
                value = child.GetBoolean();
                return true;
            }

            string? text = child.ValueKind == JsonValueKind.String
                ? child.GetString()
                : child.GetRawText();
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

        public static bool TryReadString(JsonElement token, out string? value, params string[] names)
        {
            if (token.ValueKind != JsonValueKind.Object)
            {
                value = null;
                return false;
            }

            foreach (string name in names)
            {
                if (!TryGetProperty(token, name, out JsonElement child)
                    || child.ValueKind == JsonValueKind.Null
                    || child.ValueKind == JsonValueKind.Undefined)
                {
                    continue;
                }

                value = child.ValueKind == JsonValueKind.String
                    ? child.GetString()
                    : child.GetRawText();
                return true;
            }

            value = null;
            return false;
        }

        public static string? ReadScalarAsString(JsonElement token)
        {
            return token.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => token.GetString(),
                JsonValueKind.Number when token.TryGetDecimal(out decimal value) && value <= 0m => "0.00",
                JsonValueKind.Number when token.TryGetDecimal(out decimal value) => value.ToString(CultureInfo.InvariantCulture),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => token.GetRawText()
            };
        }

        public static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
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
