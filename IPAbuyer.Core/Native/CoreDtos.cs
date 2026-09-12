using System.Text.Json.Serialization;

namespace IPAbuyer.Core.Native
{
    // Core FFI JSON 契约 DTO（字段名 snake_case 见 IPAbuyer.Core 仓库 DEVELOPMENT.md 5.4）。

    public sealed class CorePurchasedAppEntry
    {
        [JsonPropertyName("app_id")]
        public string AppId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>待宿主本地化的消息：键名 + 参数，或原文。</summary>
    public sealed class CoreMessage
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "raw";

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("args")]
        public List<string>? Args { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public sealed class CoreLogEntry
    {
        [JsonPropertyName("level")]
        public string Level { get; set; } = "info";

        [JsonPropertyName("message")]
        public CoreMessage Message { get; set; } = new();
    }

    public sealed class CoreLoginResult
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "UnknownError";

        [JsonPropertyName("message")]
        public CoreMessage Message { get; set; } = new();

        [JsonPropertyName("raw_payload")]
        public string? RawPayload { get; set; }

        [JsonPropertyName("logs")]
        public List<CoreLogEntry> Logs { get; set; } = new();
    }

    public sealed class CoreLogoutResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("logs")]
        public List<CoreLogEntry> Logs { get; set; } = new();
    }

    public sealed class CoreAuthInfo
    {
        [JsonPropertyName("payload")]
        public string Payload { get; set; } = string.Empty;

        [JsonPropertyName("is_success")]
        public bool IsSuccess { get; set; }

        [JsonPropertyName("has_explicit_failure")]
        public bool HasExplicitFailure { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("is_account_missing")]
        public bool IsAccountMissing { get; set; }

        [JsonPropertyName("logs")]
        public List<CoreLogEntry> Logs { get; set; } = new();
    }

    /// <summary>搜索结果（price/purchased 为 Core 规范值：free / 6.00、purchased / not_purchased / blocked）。</summary>
    public sealed class CoreSearchResult
    {
        [JsonPropertyName("bundle_id")]
        public string BundleId { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("developer")]
        public string? Developer { get; set; }

        [JsonPropertyName("artwork_url")]
        public string? ArtworkUrl { get; set; }

        [JsonPropertyName("price")]
        public string Price { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("purchased")]
        public string Purchased { get; set; } = string.Empty;
    }

    public sealed class CorePurchaseResult
    {
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = "Failed";

        [JsonPropertyName("raw_payload")]
        public string? RawPayload { get; set; }

        [JsonPropertyName("logs")]
        public List<CoreLogEntry> Logs { get; set; } = new();
    }

    public sealed class CoreSyncProgress
    {
        [JsonPropertyName("synced")]
        public long Synced { get; set; }

        [JsonPropertyName("total")]
        public long Total { get; set; }
    }

    public sealed class CoreSyncOutcome
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "completed";

        [JsonPropertyName("synced")]
        public long Synced { get; set; }

        [JsonPropertyName("total")]
        public long Total { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public sealed class CoreSyncStatus
    {
        [JsonPropertyName("running")]
        public bool Running { get; set; }

        [JsonPropertyName("progress")]
        public CoreSyncProgress Progress { get; set; } = new();

        [JsonPropertyName("outcome")]
        public CoreSyncOutcome? Outcome { get; set; }

        [JsonPropertyName("logs")]
        public List<CoreLogEntry> Logs { get; set; } = new();
    }

    public sealed class CoreQueueItem
    {
        [JsonPropertyName("bundle_id")]
        public string BundleId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "Pending";

        [JsonPropertyName("last_message")]
        public string? LastMessage { get; set; }
    }

    public sealed class CoreQueueStatus
    {
        [JsonPropertyName("running")]
        public bool Running { get; set; }

        [JsonPropertyName("completed")]
        public int? Completed { get; set; }

        [JsonPropertyName("items")]
        public List<CoreQueueItem> Items { get; set; } = new();

        [JsonPropertyName("logs")]
        public List<CoreLogEntry> Logs { get; set; } = new();
    }

    /// <summary>队列入参条目（与搜索结果字段对应，snake_case 为 Core 契约）。</summary>
    public sealed class CoreQueueItemInput
    {
        [JsonPropertyName("bundle_id")]
        public string BundleId { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("developer")]
        public string? Developer { get; set; }

        [JsonPropertyName("artwork_url")]
        public string? ArtworkUrl { get; set; }

        [JsonPropertyName("price")]
        public string Price { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    /// <summary>
    /// System.Text.Json 源生成上下文：主工程发布启用 full trim，
    /// Core JSON 契约全部经此序列化/反序列化，避免反射序列化被裁剪。
    /// </summary>
    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(CorePurchasedAppEntry))]
    [JsonSerializable(typeof(List<CorePurchasedAppEntry>))]
    [JsonSerializable(typeof(CoreLoginResult))]
    [JsonSerializable(typeof(CoreLogoutResult))]
    [JsonSerializable(typeof(CoreAuthInfo))]
    [JsonSerializable(typeof(List<CoreSearchResult>))]
    [JsonSerializable(typeof(CorePurchaseResult))]
    [JsonSerializable(typeof(CoreSyncStatus))]
    [JsonSerializable(typeof(CoreQueueStatus))]
    [JsonSerializable(typeof(CoreQueueItemInput))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    internal sealed partial class CoreJsonContext : JsonSerializerContext
    {
    }
}
