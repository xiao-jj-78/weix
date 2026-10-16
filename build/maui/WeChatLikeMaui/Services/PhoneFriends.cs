namespace WeChatLikeMaui.Services;

public static class PhoneFriends
{
    private static readonly Dictionary<string,FriendCandidate> _candidates=new(StringComparer.Ordinal);
    public static event Action? Changed;
    public static string[] Selected=>Preferences.Get("phone.moments.names","").Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
    public static IReadOnlyList<FriendCandidate> Candidates {
        get{
            // Preserve previous choices as removable selections when upgrading.
            foreach(var name in Selected)if(!_candidates.ContainsKey(name))_candidates[name]=new(name,"已保存的选择");
            return _candidates.Values.OrderBy(c=>c.Name,StringComparer.CurrentCulture).ToArray();
        }
    }
    public static string Capture()
    {
#if ANDROID
        var frame=Platforms.Android.PhoneMomentsService.CaptureFriends();
        foreach(var friend in frame.Candidates.Take(200)){
            if(_candidates.Count>=500&&!_candidates.ContainsKey(friend.Name))continue;
            _candidates[friend.Name]=friend with{Ambiguous=friend.Ambiguous||(_candidates.TryGetValue(friend.Name,out var old)&&old.Ambiguous)};
        }
        if(Selected.Any(n=>_candidates.TryGetValue(n,out var c)&&c.Ambiguous)){PhoneMoments.StopAuto();PhoneMoments.Cancel();}
        Changed?.Invoke();
        return frame.Candidates.Count==0?"未识别到好友。请打开微信通讯录或朋友圈，收起后调整页面，再点识别；不会把聊天正文当好友。":$"本屏识别 {frame.Candidates.Count} 个候选（{frame.Source}）。只扫描可见内容，收起后手动翻页可继续添加。";
#else
        throw new InvalidOperationException("识别微信好友需要在安卓手机使用；电脑预览不会读取手机。");
#endif
    }
    public static void Save(IEnumerable<string> names){var chosen=FriendPickerRules.ValidateSelection(names,Candidates);PhoneMoments.StopAuto();PhoneMoments.Cancel();Preferences.Set("phone.moments.names",string.Join('\n',chosen));Changed?.Invoke();PhoneWorkspace.SetStatus($"已保存 {chosen.Length} 位好友，自动巡检保持关闭。");}
    public static void ClearRecognized(){_candidates.Clear();Changed?.Invoke();}
    public static bool IsAmbiguous(string name)=>_candidates.TryGetValue(name,out var candidate)&&candidate.Ambiguous;
}
