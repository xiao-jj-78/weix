
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WeChatAuto.Models;

/// <summary>
/// 通义千问 Chat Completion 响应体
/// </summary>
public sealed class QwenChatResponseBody
{
    /// <summary>
    /// 唯一请求ID
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 对象类型，例如：chat.completion
    /// </summary>
    [JsonProperty("object")]
    public string Object { get; set; } = "chat.completion";

    /// <summary>
    /// 创建时间（Unix 时间戳）
    /// </summary>
    [JsonProperty("created")]
    public long Created { get; set; }

    /// <summary>
    /// 模型名称
    /// </summary>
    [JsonProperty("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 系统指纹
    /// </summary>
    [JsonProperty("system_fingerprint")]
    public string SystemFingerprint { get; set; }

    /// <summary>
    /// 回复列表
    /// </summary>
    [JsonProperty("choices")]
    public List<QwenChatChoice> Choices { get; set; } = new List<QwenChatChoice>();

    /// <summary>
    /// Token 使用情况
    /// </summary>
    [JsonProperty("usage")]
    public QwenChatUsage Usage { get; set; }
}

/// <summary>
/// 回复选项
/// </summary>
public sealed class QwenChatChoice
{
    /// <summary>
    /// 序号
    /// </summary>
    [JsonProperty("index")]
    public int Index { get; set; }

    /// <summary>
    /// 回复消息
    /// </summary>
    [JsonProperty("message")]
    public QwenChatMessage Message { get; set; }

    /// <summary>
    /// 结束原因
    /// stop、length、tool_calls 等
    /// </summary>
    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }

    /// <summary>
    /// LogProbs（目前通常为 null）
    /// </summary>
    [JsonProperty("logprobs")]
    public object Logprobs { get; set; } = null;
}



/// <summary>
/// Token 使用统计
/// </summary>
public sealed class QwenChatUsage
{
    /// <summary>
    /// Prompt Token 数
    /// </summary>
    [JsonProperty("prompt_tokens")]
    public int PromptTokens { get; set; }

    /// <summary>
    /// Completion Token 数
    /// </summary>
    [JsonProperty("completion_tokens")]
    public int CompletionTokens { get; set; }

    /// <summary>
    /// 总 Token 数
    /// </summary>
    [JsonProperty("total_tokens")]
    public int TotalTokens { get; set; }

    /// <summary>
    /// Prompt Token 详情
    /// </summary>
    [JsonProperty("prompt_tokens_details")]
    public QwenPromptTokenDetails PromptTokensDetails { get; set; }
}

/// <summary>
/// Prompt Token 详情
/// </summary>
public sealed class QwenPromptTokenDetails
{
    /// <summary>
    /// 命中缓存的 Token 数
    /// </summary>
    [JsonProperty("cached_tokens")]
    public int CachedTokens { get; set; }
}