using System.ComponentModel;
using System.Runtime.CompilerServices;
using WeChatLikeMaui.Services;

namespace WeChatLikeMaui.Models;

public sealed class TaskItem : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; private set; } = "";
    public string Desc { get; private set; } = "";
    public bool Running { get; private set; }
    public int Elapsed { get; private set; }
    public int? ExitCode { get; private set; }
    public int? Pid { get; private set; }
    public bool CanAct { get; private set; }
    public bool Busy { get; private set; }
    public Color DotColor => !CanAct && !Busy ? Color.FromArgb("#9AA9AC") : Running ? Color.FromArgb("#11856B") :
        ExitCode is not null and not 0 ? Color.FromArgb("#B44D41") : Color.FromArgb("#9AA9AC");
    public Color ActionColor => Running ? Color.FromArgb("#A6443B") : Color.FromArgb("#11856B");
    public string ActionText => Busy ? "处理中" : Running ? "停止" : "启动";
    public string StatusText => !CanAct && !Busy ? "离线 · 请先连接电脑端" : Running ? $"运行中 · {Elapsed} 秒 · PID {Pid}" :
        ExitCode.HasValue ? ExitCode == 0 ? "已结束 · 退出码 0" : $"运行失败 · 退出码 {ExitCode}" : "就绪 · 尚未启动";
    public void Apply(TaskDto dto, bool connected)
    {
        Name = dto.Name; Desc = dto.Desc; Running = dto.Running; Elapsed = dto.Elapsed;
        ExitCode = dto.ExitCode; Pid = dto.Pid; CanAct = connected && !Busy;
        Changed();
    }
    public void SetAvailable(bool value) { CanAct = value && !Busy; Changed(); }
    public void SetBusy(bool value, bool connected) { Busy = value; CanAct = connected && !value; Changed(); }
    private void Changed()
    {
        foreach (var property in new[] { nameof(Name), nameof(Desc), nameof(Running), nameof(CanAct), nameof(Busy), nameof(DotColor), nameof(ActionColor), nameof(ActionText), nameof(StatusText) })
            OnPropertyChanged(property);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

