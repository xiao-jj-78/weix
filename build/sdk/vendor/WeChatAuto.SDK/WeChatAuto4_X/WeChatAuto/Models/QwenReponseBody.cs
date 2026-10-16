using System;
using Newtonsoft.Json;

namespace WeChatAuto.Models;

/// <summary>
/// 千问 TTS 响应结果
/// </summary>
public sealed class QwenResponseBody
{
    [JsonProperty("status_code")]
    public int StatusCode { get; set; }

    [JsonProperty("request_id")]
    public string RequestId { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("output")]
    public OutputInfo Output { get; set; }

    [JsonProperty("usage")]
    public UsageInfo Usage { get; set; }
}

public sealed class OutputInfo
{
    [JsonProperty("text")]
    public string Text { get; set; } = null;

    [JsonProperty("choices")]
    public object Choices { get; set; } = null;

    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }

    [JsonProperty("audio")]
    public AudioInfo Audio { get; set; }
}

public sealed class AudioInfo
{
    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("data")]
    public string Data { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }

    /// <summary>
    /// Unix 时间戳（秒）
    /// </summary>
    [JsonProperty("expires_at")]
    public long ExpiresAt { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset ExpireTime =>
        DateTimeOffset.FromUnixTimeSeconds(ExpiresAt);
}

public sealed class UsageInfo
{
    [JsonProperty("input_tokens")]
    public int InputTokens { get; set; }

    [JsonProperty("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonProperty("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonProperty("characters")]
    public int Characters { get; set; }

    [JsonProperty("input_tokens_details")]
    public InputTokensDetails InputTokensDetails { get; set; }

    [JsonProperty("output_tokens_details")]
    public OutputTokensDetails OutputTokensDetails { get; set; }
}

public sealed class InputTokensDetails
{
    [JsonProperty("text_tokens")]
    public int TextTokens { get; set; }
}

public sealed class OutputTokensDetails
{
    [JsonProperty("audio_tokens")]
    public int AudioTokens { get; set; }

    [JsonProperty("text_tokens")]
    public int TextTokens { get; set; }
}