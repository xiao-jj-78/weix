using System.Collections.Generic;
using WeChatAuto.Models;

namespace WeChatAuto.Models;

/// <summary>
/// 预设音色列表（Qwen-Audio-TTS 系列 qwen-audio-3.1-tts-flash 系统音色）
/// 音色必须与请求模型匹配，不能跨模型混用。
/// </summary>
public static class VoicePresetList
{
    public static List<VoicePreset> AllVoicePresets
    {
        get
        {
            return new List<VoicePreset>()
            {
              new VoicePreset {Id="longanhuan_v3.1",Description="龙安欢（女性），支持多方言多语言",MainLanguage="普通话"},
              new VoicePreset {Id="longanlingxin_v3.1",Description="龙安灵心（女性），支持多方言多语言",MainLanguage="普通话"},
              new VoicePreset {Id="longanfengyue_v3.1",Description="龙安风悦（女性），支持多方言多语言",MainLanguage="普通话"},
              new VoicePreset {Id="yuxiaoyun_v3.1",Description="于小云（女性），元气、亲切、自然",MainLanguage="普通话"},
              new VoicePreset {Id="qiaoxiaojiao_v3.1",Description="乔小娇（女性），俏丽、可爱",MainLanguage="普通话"},
              new VoicePreset {Id="xiaxiaochen_v3.1",Description="夏小晨（女性），元气、明亮",MainLanguage="普通话"},
              new VoicePreset {Id="wenhuaiqing_v3.1",Description="温怀清（女性），清亮、柔和",MainLanguage="普通话"},
              new VoicePreset {Id="anxiaolan_v3.1",Description="安小岚（女性），清甜、纯净",MainLanguage="普通话"},
              new VoicePreset {Id="xieshurou_v3.1",Description="谢舒柔（女性），柔和、自然、知性",MainLanguage="普通话"},
              new VoicePreset {Id="baiqinglan_v3.1",Description="白清岚（女性），明亮、清纯",MainLanguage="普通话"},
              new VoicePreset {Id="xuyuyuan_v3.1",Description="许玉远（女性），知性、成熟、质感",MainLanguage="普通话"},
              new VoicePreset {Id="anruorou_v3.1",Description="安若柔（女性），气声、知性",MainLanguage="普通话"},
              new VoicePreset {Id="wenhuaizhi_v3.1",Description="闻怀之（女性），稳重、成熟",MainLanguage="普通话"},
              new VoicePreset {Id="xiaoxingzhi_v3.1",Description="萧行之（女性），端庄、贵气",MainLanguage="普通话"},
              new VoicePreset {Id="guyunshu_v3.1",Description="顾云舒（女性），成熟、稳重",MainLanguage="普通话"},
              new VoicePreset {Id="yeqinghe_v3.1",Description="叶清禾（女性），亲切、温柔",MainLanguage="普通话"},
              new VoicePreset {Id="yunhuanhuan_v3.1",Description="云欢欢（女性），高亢、热情",MainLanguage="普通话"},
              new VoicePreset {Id="xuxiaoqiao_v3.1",Description="徐小俏（女性），自然、俏皮",MainLanguage="普通话"},
              new VoicePreset {Id="baianran_v3.1",Description="白安然（女性），低沉、浑厚、气声",MainLanguage="普通话"},
              new VoicePreset {Id="xuyanchu_v3.1",Description="许言初（女性），沉稳、磁性",MainLanguage="普通话"},
              new VoicePreset {Id="yezhiqing_v3.1",Description="叶知晴（女性），轻快、自然",MainLanguage="普通话"},
              new VoicePreset {Id="anyuqing_v3.1",Description="安语晴（女性），甜妹",MainLanguage="普通话"},
              new VoicePreset {Id="Emily_v3.1",Description="Emily（女性），英式女声",MainLanguage="English"},
              new VoicePreset {Id="Luna_v3.1",Description="Luna（女性），英式女声",MainLanguage="English"},
              new VoicePreset {Id="Abby_v3.1",Description="Abby（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="Annie_v3.1",Description="Annie（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="Ava_v3.1",Description="Ava（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="Beth_v3.1",Description="Beth（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="Betty_v3.1",Description="Betty（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="Cally_v3.1",Description="Cally（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="Cindy_v3.1",Description="Cindy（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="Donna_v3.1",Description="Donna（女性），美式女声",MainLanguage="English"},
              new VoicePreset {Id="longanyuanfei_v3.1",Description="龙安元妃（女性），高傲妃子音",MainLanguage="普通话"},
              new VoicePreset {Id="longanlingxi_v3.1",Description="龙安灵希（女性），可爱甜美音",MainLanguage="普通话"},
              new VoicePreset {Id="longyingtao_v3.1",Description="龙应桃（女性），温柔淡定女",MainLanguage="普通话"},
              new VoicePreset {Id="longanya_v3.1",Description="龙安雅（女性），高雅气质女",MainLanguage="普通话"},
              new VoicePreset {Id="longwan_v3.1",Description="龙婉（女性），细腻柔声女",MainLanguage="普通话"},
              new VoicePreset {Id="longxing_v3.1",Description="龙星（女性），温婉邻家女",MainLanguage="普通话"},
              new VoicePreset {Id="longhua_v3.1",Description="龙华（女性），元气甜美女",MainLanguage="普通话"},
              new VoicePreset {Id="longling_v3.1",Description="龙铃（女性），稚气呆板女",MainLanguage="普通话"},
              new VoicePreset {Id="longpaopao_v3.1",Description="龙泡泡（女性），飞天泡泡音",MainLanguage="普通话"},
              new VoicePreset {Id="loongstella_v3.1",Description="loongstella（女性），飒爽利落女",MainLanguage="普通话"},
              new VoicePreset {Id="longyuan_v3.1",Description="龙媛（女性），温暖治愈女",MainLanguage="普通话"},
              new VoicePreset {Id="longmiao_v3.1",Description="龙妙（女性），抑扬顿挫女",MainLanguage="普通话"},
              new VoicePreset {Id="longanli_v3.1",Description="龙安莉（女性），利落从容女",MainLanguage="普通话"},
              new VoicePreset {Id="longanwen_v3.1",Description="龙安温（女性），优雅知性女",MainLanguage="普通话"},
              new VoicePreset {Id="longxiaoxia_v3.1",Description="龙小夏（女性），沉稳权威女",MainLanguage="普通话"},
              new VoicePreset {Id="xunanchuan_v3.1",Description="许南川（男性），支持多方言多语言",MainLanguage="普通话"},
              new VoicePreset {Id="anmingyuan_v3.1",Description="安明远（男性），清亮、自然",MainLanguage="普通话"},
              new VoicePreset {Id="huozhuoshi_v3.1",Description="霍拙石（男性），清亮",MainLanguage="普通话"},
              new VoicePreset {Id="andi_v3.1",Description="安迪（男性），ABC口音",MainLanguage="普通话"},
              new VoicePreset {Id="Eric_v3.1",Description="Eric（男性），英式男声",MainLanguage="English"},
              new VoicePreset {Id="Luca_v3.1",Description="Luca（男性），英式男声",MainLanguage="English"},
              new VoicePreset {Id="Andy_v3.1",Description="Andy（男性），美式男声",MainLanguage="English"},
              new VoicePreset {Id="Brian_v3.1",Description="Brian（男性），美式男声",MainLanguage="English"},
              new VoicePreset {Id="David_v3.1",Description="David（男性），美式男声",MainLanguage="English"},
              new VoicePreset {Id="longjielidou_v3.1",Description="龙杰力豆（男性），天真男童音",MainLanguage="普通话"},
              new VoicePreset {Id="longhuohuo_v3.1",Description="龙火火（男性），顽皮少年音",MainLanguage="普通话"},
              new VoicePreset {Id="longhan_v3.1",Description="龙寒（男性），温暖痴情男",MainLanguage="普通话"},
              new VoicePreset {Id="longanzhi_v3.1",Description="龙安智（男性），睿智轻熟男",MainLanguage="普通话"},
              new VoicePreset {Id="longzhe_v3.1",Description="龙哲（男性），呆板大暖男",MainLanguage="普通话"},
              new VoicePreset {Id="longanyang_v3.1",Description="龙安洋（男性），阳光大男孩",MainLanguage="普通话"},
              new VoicePreset {Id="libai_v3.1",Description="李白（男性），古代诗仙男",MainLanguage="普通话"},
              new VoicePreset {Id="longniuniu_v3.1",Description="龙牛牛（男性），阳光男童声",MainLanguage="普通话"},
              new VoicePreset {Id="longshanshan_v3.1",Description="龙闪闪（男性），戏剧化童声",MainLanguage="普通话"},
              new VoicePreset {Id="longsanshu_v3.1",Description="龙三叔（男性），沉稳质感男",MainLanguage="普通话"},
              new VoicePreset {Id="longanlang_v3.1",Description="龙安朗（男性），清爽利落男",MainLanguage="普通话"},
              new VoicePreset {Id="longanchong_v3.1",Description="龙安冲（男性），激情推销男",MainLanguage="普通话"},
            };
        }
    }
}
