namespace WeChatLikeMaui.Services;

public readonly record struct OverlayPosition(int X,int Y);
public static class OverlayGeometry
{
    public static OverlayPosition Clamp(int x,int y,int width,int height,int screenWidth,int screenHeight,int margin)
    {
        var left=Math.Min(margin,Math.Max(0,screenWidth-width));
        var top=Math.Min(margin,Math.Max(0,screenHeight-height));
        return new(Math.Clamp(x,left,Math.Max(left,screenWidth-width-margin)),Math.Clamp(y,top,Math.Max(top,screenHeight-height-margin)));
    }
}

// Owned by the overlay UI thread. Navigation cancels its previous generation;
// stale completions must never replace the current panel or enable sending.
public sealed class OverlaySession : IDisposable
{
    private long _revision;
    private CancellationTokenSource? _generation;
    public bool IsPaused { get; private set; }
    public bool IsStopped { get; private set; }
    public long Navigate() { _generation?.Cancel();_generation?.Dispose();_generation=null;return ++_revision; }
    public (long Revision,CancellationToken Token) BeginGeneration()
    {
        if(IsPaused||IsStopped)throw new InvalidOperationException("助手已暂停或关闭。");
        Navigate();_generation=new();return(_revision,_generation.Token);
    }
    public bool Accepts(long revision)=>revision==_revision&&!IsPaused&&!IsStopped;
    public void Pause(){IsPaused=true;Navigate();}
    public void Resume(){if(IsStopped)throw new InvalidOperationException("助手已关闭。");IsPaused=false;Navigate();}
    public void Dispose(){IsStopped=true;Navigate();}
}
