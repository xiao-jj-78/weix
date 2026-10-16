using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WeChatLikeMaui.Services;

public sealed record WatchedMoment(string Id,string Author,bool Recent);

/// <summary>
/// 巡检决策审计条目：每一次基线/跳过/拟点赞/已点赞都记录，便于本人回看与止损。
/// </summary>
public sealed record WatchAuditEntry(
    DateTimeOffset At,
    string Decision,   // Baseline | Cooldown | CapReached | WouldLike | Liked
    string? Author,
    string? MomentId,
    string Detail);

public sealed class MomentWatchPolicy
{
    private readonly HashSet<string> _seen=new(StringComparer.Ordinal);
    private bool _baselineNeeded=true;
    private string _context="";
    private DateTimeOffset _lastLikeAt=DateTimeOffset.MinValue;
    private int _likedThisRun=0;

    // —— 护栏配置（建议后续在设置页暴露；默认值偏保守）——
    /// <summary>两次成功点赞之间的最小间隔（秒）。防“出现即点”的批量节奏。</summary>
    public int MinIntervalSeconds { get; set; } = 60;
    /// <summary>单次启用后的点赞上限；0 表示不限制。</summary>
    public int MaxPerRun { get; set; } = 20;
    /// <summary>每做一次决策都会上报，用于 UI 审计面板。</summary>
    public Action<WatchAuditEntry>? OnAudit { get; set; }

    public static string Identity(string author,string body)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{author,body}))));

    public void Suspend()=>_baselineNeeded=true;
    /// <summary>显式开关打开时调用，重置本轮计数。</summary>
    public void ResetRunCounters(){_likedThisRun=0;}

    public WatchedMoment? Observe(IReadOnlyList<WatchedMoment> posts,bool eligible,string context,IEnumerable<string> allowed)
    {
        if(!eligible){Suspend();return null;}
        var names=allowed.ToHashSet(StringComparer.Ordinal);
        if(_baselineNeeded||_context!=context||_seen.Count>=2048){
            if(_seen.Count>=2048)_seen.Clear();
            foreach(var post in posts)_seen.Add(post.Id);
            _baselineNeeded=false;_context=context;
            Audit("Baseline",null,null,"已记录基线，本轮不点赞");
            return null;
        }
        // 冷却：距上次成功点赞不足最小间隔则本轮跳过候选
        if(MinIntervalSeconds>0 && (DateTimeOffset.UtcNow-_lastLikeAt).TotalSeconds < MinIntervalSeconds){
            Audit("Cooldown",null,null,$"距上次点赞不足 {MinIntervalSeconds}s，本轮跳过");
            return null;
        }
        // 上限：达到单次上限则停止
        if(MaxPerRun>0 && _likedThisRun>=MaxPerRun){
            Audit("CapReached",null,null,$"已达本轮上限 {MaxPerRun}，停止点赞");
            return null;
        }
        var next=posts.FirstOrDefault(p=>p.Recent&&names.Contains(p.Author)&&!_seen.Contains(p.Id));
        if(next!=null)_seen.Add(next.Id);
        foreach(var post in posts.Where(p=>!p.Recent||!names.Contains(p.Author)))_seen.Add(post.Id);
        if(next==null){Audit("Skipped",null,null,"无符合条件的未点赞动态");return null;}
        // Reserve before the UI attempt. An uncertain click is never retried.
        Audit("WouldLike",next.Author,next.Id,"拟点赞（待界面确认执行）");
        return next;
    }

    /// <summary>界面层实际执行完一次点赞后调用，推进冷却与计数。</summary>
    public void MarkLiked(WatchedMoment moment)
    {
        _lastLikeAt=DateTimeOffset.UtcNow;
        _likedThisRun++;
        Audit("Liked",moment.Author,moment.Id,"已执行点赞");
    }

    private void Audit(string decision,string? author,string? id,string detail)=>
        OnAudit?.Invoke(new WatchAuditEntry(DateTimeOffset.UtcNow,decision,author,id,detail));
}
