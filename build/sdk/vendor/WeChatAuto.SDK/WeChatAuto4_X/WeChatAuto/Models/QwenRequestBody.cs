using Newtonsoft.Json;

namespace WeChatAuto.Models;

public class QwenRequestBody
{
    /// <summary>
    /// 模型名称
    /// </summary>
    [JsonProperty("model")]
    public string Model { get; set; }
    /// <summary>
    /// 语音合成的输入参数。
    /// </summary>
    [JsonProperty("input")]
    public RequestInput Input { get; set; }

}

public class RequestInput
{
    /// <summary>
    /// 待合成的文本内容，支持多语言混合输入。
    /// </summary>
    [JsonProperty("text")]
    public string Text { get; set; }

    /// <summary>
    /// 音色，必须属于当前模型支持的音色列表，不能跨模型混用。
    /// </summary>
    [JsonProperty("voice")]
    public string Voice { get; set; }

    /// <summary>
    /// 音频编码格式。可选值：mp3、pcm、wav、opus。默认 wav。
    /// </summary>
    [JsonProperty("format")]
    public string Format { get; set; } = "wav";

    /// <summary>
    /// 音频采样率（Hz）。24000 为常用值。
    /// </summary>
    [JsonProperty("sample_rate")]
    public int SampleRate { get; set; } = 24000;

    /// <summary>
    /// 指令控制文本。Qwen-Audio-TTS 使用 instruction（单数），
    /// 用于控制方言、情感、语速、音色等合成效果。为空时不发送。
    /// </summary>
    [JsonProperty("instruction", NullValueHandling = NullValueHandling.Ignore)]
    public string Instruction { get; set; } = null;
}