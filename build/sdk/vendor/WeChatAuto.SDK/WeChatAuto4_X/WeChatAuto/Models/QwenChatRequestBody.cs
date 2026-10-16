
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WeChatAuto.Models;

/// <summary>
/// 通义千问 Chat Completion 请求体
/// </summary>
public sealed class QwenChatRequestBody
{
    /// <summary>
    /// 模型名称，例如：qwen3.7-plus
    /// </summary>
    [JsonProperty("model")]
    public string Model { get; set; } = "qwen3.7-plus";

    /// <summary>
    /// 消息列表
    /// </summary>
    [JsonProperty("messages")]
    public List<QwenChatMessage> Messages { get; set; } = new List<QwenChatMessage>();
    [JsonProperty("enable_thinking")]
    public bool EnableThinking { get; set; } = false;
}

/// <summary>
/// 聊天消息
/// </summary>
public sealed class QwenChatMessage
{
    /// <summary>
    /// 角色：system、user、assistant、tool
    /// </summary>
    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;
}