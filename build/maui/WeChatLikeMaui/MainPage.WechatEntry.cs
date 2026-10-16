using System.Text.Json;
using WeChatLikeMaui.Services;

namespace WeChatLikeMaui;

public partial class MainPage
{
    private bool _quickWechatBusy;
    private void OnQuickConnectionClicked(object? sender,EventArgs e)
    {
        ConnectionPanel.IsVisible=!ConnectionPanel.IsVisible;
        QuickConnectionBtn.Text=ConnectionPanel.IsVisible?"收起高级连接设置":"高级连接设置";
    }
    private async void OnQuickWechatClicked(object? sender,EventArgs e)=>await OpenWechatSimplyAsync();
    private async Task OpenWechatSimplyAsync()
    {
        if(_quickWechatBusy||_busy||_sdkBusy||_clientsBusy)return;
        _quickWechatBusy=true;QuickWechatBtn.IsEnabled=false;
        QuickWechatMessage.Text="正在准备本机服务并查找已有微信窗口…";
        try
        {
            if(!_connected)
            {
                IpEntry.Text="127.0.0.1";PortEntry.Text="8765";
                await ConnectAsync(true);
                if(!_connected)throw new InvalidOperationException("本机服务未能连接。请从完整交付文件夹启动，并查看下方错误提示。");
            }
            await _api.SdkActionAsync("start",new{},Token);
            var result=await _api.SdkReadAsync("clients",Token);
            var clients=result.TryGetProperty("clients",out var list)&&list.ValueKind==JsonValueKind.Array?list.EnumerateArray().Select(item=>item.Clone()).ToArray():[];
            JsonElement opened;
            if(clients.Length==0)opened=await _api.SdkActionAsync("clients/open",new{},Token);
            else
            {
                var selected=clients[0];
                if(clients.Length>1)
                {
                    var choices=clients.Select(item=>{
                        var alias=Text(item,"alias","");
                        return (string.IsNullOrWhiteSpace(alias)?"微信窗口":alias)+" · PID "+item.GetProperty("pid").ToString();
                    }).ToArray();
                    var choice=await DisplayActionSheetAsync("继续使用哪个微信窗口？","取消",null,choices);
                    var index=Array.IndexOf(choices,choice);
                    if(index<0){QuickWechatMessage.Text="已取消选择，没有切换微信窗口。";return;}
                    selected=clients[index];
                }
                opened=await _api.SdkActionAsync("clients/focus",new{instanceId=selected.GetProperty("instanceId").GetString()},Token);
            }
            QuickWechatMessage.Text=Text(opened,"message","已请求打开官方微信。")+" 若官方已保留登录状态，直接继续使用；只有官方要求时才由本人扫码或确认。";
            await RefreshSdkAsync(Token);
        }
        catch(OperationCanceledException){QuickWechatMessage.Text="操作已取消，可再次点击继续。";}
        catch(Exception ex){QuickWechatMessage.Text=ex.Message;}
        finally{_quickWechatBusy=false;QuickWechatBtn.IsEnabled=true;}
    }
}
