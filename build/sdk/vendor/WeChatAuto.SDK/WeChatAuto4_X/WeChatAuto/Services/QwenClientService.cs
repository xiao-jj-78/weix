using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WeChatAuto.Models;
using WeChatAuto.Options;
using System.Text.Json;
using Newtonsoft.Json;

namespace WeChatAuto.Services;

/// <summary>
/// 千问http客户端
/// </summary>
public partial class QwenClientService
{
    private readonly HttpClient _http;

    public QwenClientService(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(180);
        _http.DefaultRequestHeaders.Add("User-Agent", "WeChatAuto.SDK");

    }


    /// <summary>
    /// 执行通义千问OpenAI 兼容的 Chat API
    /// 语音朗读文本优化器
    /// 将 书面化 的文字 转换成 口语化的形式的文字
    /// </summary>
    /// <param name="apiKey">api key</param>
    /// <param name="message">书面化的文字,也就是微信消息</param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="HttpRequestException"></exception>
    public async Task<string> HumenText(string apiKey, string message)
    {
        _ = string.IsNullOrWhiteSpace(apiKey) ? throw new Exception("错误：apiKey不能为空！") : "";
        var url = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        var inputJson = _GetChatInputJson(message);
        httpRequest.Content = new StringContent(inputJson, Encoding.UTF8, "application/json");
        var response = await _http.SendAsync(httpRequest);
        if (response.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<QwenChatResponseBody>(await response.Content.ReadAsStringAsync());
            return result.Choices[0].Message.Content;
        }
        else
        {
            throw new HttpRequestException($"请求发生错误，错误代码:{response.StatusCode}");
        }
    }

    private string _GetChatInputJson(string message)
    {
        var prompt = $@"
        你是一名专业的中文 TTS 文本预处理器（Text Normalization + Prosody Formatter）。
        你的唯一职责不是改写文章，而是把""给人看的文字""转换成""给人听的文字""。
        最终目标：
        让 TTS 朗读出来，听起来像真人自然讲话，而不是机器逐字念文本。
        --------------------------------------------------
        【最高原则】
        --------------------------------------------------
        1. 保持原文含义完全一致。
        2. 不允许改变任何事实。
        3. 不允许增加任何新的信息。
        4. 不允许删除任何已有信息。
        5. 可以调整表达方式，只为了更符合人类朗读习惯。
        6. 不追求书面美观，只追求朗读自然。
        --------------------------------------------------
        【朗读原则】
        --------------------------------------------------
        你的输出应符合真人讲话习惯。
        优先模拟：
        - 正常人与人交流
        - 客服
        - 老师讲课
        而不是照着文字逐字朗读。

        输出应该：
        ✔ 自然
        ✔ 流畅
        ✔ 有节奏
        ✔ 有停顿
        ✔ 容易理解
        ✔ 没有机器感

        --------------------------------------------------
        【文本规范化（TN）】
        --------------------------------------------------

        下面内容必须转换为适合朗读的形式。
        【金额】

        13.83元
        → 十三块八毛三

        100.05元
        → 一百块零五分

        2000元
        → 两千块

        ￥88.00
        → 八十八块

        --------------------------------------------------

        【日期】

        2026年7月28日

        → 二零二六年七月二十八日

        2026-07-28

        → 二零二六年七月二十八日

        --------------------------------------------------

        【时间】

        09:00

        → 上午九点

        10:30

        → 上午十点半

        14:35

        → 下午两点三十五分

        18:00

        → 下午六点

        --------------------------------------------------

        【时长】

        1.5小时

        → 一个半小时

        90分钟

        → 一个半小时

        120分钟

        → 两个小时

        --------------------------------------------------

        【百分比】

        68%

        → 百分之六十八

        15%

        → 百分之十五

        --------------------------------------------------

        【温度】

        32℃

        → 三十二度

        -5℃

        → 零下五度

        --------------------------------------------------

        【数量】

        123人

        → 一百二十三人

        10000

        → 一万

        120000

        → 十二万

        --------------------------------------------------

        【电话号码】

        手机号、固定电话、客服电话，应转换成适合朗读的方式。

        例如：

        13800138000

        → 幺三八，零零一三，八零零零

        4008001234

        → 四零零，八零零，一二三四

        0755-12345678

        → 零七五五，一二三四，五六七八

        --------------------------------------------------

        【版本号】

        V3.2.15

        → 三点二点一五版本

        Version 2.0

        → 二点零版本

        --------------------------------------------------

        【英文缩写】

        AI

        → A I

        SDK

        → S D K

        API

        → A P I

        CPU

        → C P U

        PDF

        → P D F

        HTTP

        → H T T P

        IP

        → I P

        --------------------------------------------------

        【单位】

        5km

        → 五公里

        2GB

        → 两 G B

        512MB

        → 五百一十二 M B

        3m

        → 三米

        --------------------------------------------------
        【韵律优化（Prosody）】
        --------------------------------------------------

        允许：

        ✔ 调整标点

        ✔ 增加逗号

        ✔ 增加句号

        ✔ 增加顿号

        ✔ 增加换行

        ✔ 拆分长句

        ✔ 合理停顿

        使语音更加自然。

        例如：

        原文：

        今天我要告诉大家一个非常重要的消息希望大家认真听。

        输出：

        今天，我要告诉大家一个非常重要的消息。

        希望大家认真听。

        --------------------------------------------------
        【可朗读性优化】
        --------------------------------------------------

        把适合阅读的表达，转换成真人更容易说出口的表达。

        例如：

        预计10:30开始发布。

        ↓

        预计上午十点半开始发布。

        ————————————

        持续1.5小时。

        ↓

        持续一个半小时。

        ————————————

        优惠15%。

        ↓

        优惠百分之十五。

        ————————————

        支付13.83元。

        ↓

        支付十三块八毛三。

        --------------------------------------------------
        【特殊规则】
        --------------------------------------------------

        如果原文已经非常适合朗读：

        保持原样。

        不要为了优化而优化。

        不要故意改写。

        --------------------------------------------------
        【禁止事项】
        --------------------------------------------------

        禁止：

        ❌ 输出解释
        ❌ 输出分析
        ❌ 输出理由
        ❌ 输出 Markdown
        ❌ 输出""优化后""
        ❌ 输出编号
        ❌ 输出任何提示信息
        ❌ 输出与最终文本无关的内容

        --------------------------------------------------
        【输出要求】
        --------------------------------------------------

        直接输出最终适合 TTS 朗读的文本。

        除此之外，不输出任何其它内容。

        在输出之前，请按以下顺序完成处理：
        第一步：理解全文含义。
        第二步：识别所有需要进行文本规范化（TN）的内容，例如数字、金额、日期、时间、单位、百分比、版本号、英文缩写、电话号码等。
        第三步：将上述内容转换为符合真人朗读习惯的形式。
        第四步：优化整篇文本的停顿、断句、标点和韵律，使其更适合自然语音朗读。
        第五步：检查最终文本：
        - 是否改变了原意？
        - 是否遗漏了信息？
        - 是否增加了信息？
        - 是否仍存在机器朗读感？

        确认无误后，仅输出最终文本。
        --------------------------------------------------
        【要求转换文本】
        --------------------------------------------------
        {message}
        ";
        QwenChatRequestBody request = new QwenChatRequestBody();
        request.Model = "qwen3.7-plus";
        request.Messages = new System.Collections.Generic.List<QwenChatMessage>()
        {
          new QwenChatMessage()
          {
              Role = "user",
              Content = prompt,
          }
        };
        request.EnableThinking = false;
        return JsonConvert.SerializeObject(request);
    }

    /// <summary>
    /// 封装千问python api的MultiModalConversation.Call方法
    /// </summary>
    /// <param name="apiKey">千问的api key</param>
    /// <param name="message">转语音的文字内容</param>
    /// <param name="options">文字转语音的控制选项，详情请参见<see cref="VoiceOptions"/></param>
    /// <param name="isOptimize">文字message是否在进行tts前用LLM进行优化</param>
    /// <returns>文字转语音结束后的本地文件</returns>
    public async Task<string> MultiModalConversationCall(string apiKey, string message, VoiceOptions options = null, bool isOptimize = false)
    {
        _ = string.IsNullOrWhiteSpace(apiKey) ? throw new Exception("错误：apiKey不能为空！") : "";
        options = options == null ? new VoiceOptions() : options;
        // Qwen-Audio-TTS 的系统音色与声音复刻/设计音色均通过同一端点调用，因此仅校验音色非空。
        if (string.IsNullOrWhiteSpace(options.Voice))
            throw new Exception("错误：音色(voice)不能为空！");
        var url = "https://maas.qianwenaiapi.com/api/v1/services/audio/tts/SpeechSynthesizer";
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        //获取发送json字符串
        var inputJson = _GetInputJson(options, message);
        StringContent content = new StringContent(inputJson, Encoding.UTF8, "application/json");
        httpRequest.Content = content;
        var response = await _http.SendAsync(httpRequest);
        if (response.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<QwenResponseBody>(await response.Content.ReadAsStringAsync());
            var remoteUrl = result.Output.Audio.Url;
            //下载文件
            var path = Path.Join(AppContext.BaseDirectory, "Temp");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            var localFileName = Path.Join(path, Guid.NewGuid().ToString("N").ToString() + ".wav");
            using var fileResponse = await _http.GetAsync(remoteUrl, HttpCompletionOption.ResponseHeadersRead);
            fileResponse.EnsureSuccessStatusCode();
            await using var httpStream = await fileResponse.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(localFileName);
            await httpStream.CopyToAsync(fileStream);
            await Task.Delay(1000);
            return localFileName;
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync();
            var err = JsonConvert.DeserializeObject<QwenResponseBody>(body);
            throw new HttpRequestException($"请求发生错误，错误代码:{response.StatusCode}，code:{err?.Code}，message:{err?.Message}");
        }

    }

    private string _GetInputJson(VoiceOptions options, string message)
    {
        var result = new QwenRequestBody();
        result.Model = string.IsNullOrWhiteSpace(options.Model) ? "qwen-audio-3.1-tts-flash" : options.Model;
        var input = new RequestInput();
        input.Text = message;
        input.Voice = options.Voice;
        input.Format = "wav";
        input.SampleRate = 24000;
        input.Instruction = string.IsNullOrWhiteSpace(options.Instructions) ? null : options.Instructions;
        result.Input = input;
        return JsonConvert.SerializeObject(result);
    }
}