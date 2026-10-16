namespace WeChatLikeMaui.Services;

// A deliberately small, read-only projection of a visible accessibility tree.
// No wxid is available: these are display-name candidates, not account identities.
public sealed record FriendNode(int Id,int Parent,string Text,string Description,string ClassName,
    bool Clickable,int Left,int Top,int Right,int Bottom);
public sealed record FriendCandidate(string Name,string Source,bool Ambiguous=false);
public sealed record FriendFrame(string Source,IReadOnlyList<FriendCandidate> Candidates);

public static class FriendPickerRules
{
    private static readonly HashSet<string> Reserved=new(StringComparer.Ordinal){"新的朋友","群聊","标签","公众号","企业微信联系人","服务号","微信团队","文件传输助手","朋友权限","我的企业及企业联系人","Contacts","New Friends","Group Chats","Tags","Official Accounts","微信","通讯录","发现","我","朋友圈","Moments","赞","评论","Like","Comment"};
    public static bool ValidName(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=100&&!value.Contains('\n')&&!Reserved.Contains(value);
    public static FriendFrame Extract(string package,bool unlocked,IReadOnlyList<FriendNode> tree,int width,int height,float density)
    {
        if(package!="com.tencent.mm"||!unlocked||width<=0||height<=0||density<=0)return new("未识别",[]);
        var map=tree.ToDictionary(n=>n.Id);
        bool Descends(FriendNode node,FriendNode ancestor){for(int i=0;i<10&&map.TryGetValue(node.Parent,out var parent);i++){if(parent.Id==ancestor.Id)return true;node=parent;}return false;}
        FriendNode[] Contents(FriendNode node)=>tree.Where(n=>n.Id==node.Id||Descends(n,node)).ToArray();
        bool Image(FriendNode n)=>n.ClassName.EndsWith("ImageView",StringComparison.Ordinal)&&n.Right-n.Left>=24*density&&n.Right-n.Left<=100*density;
        bool LeafText(FriendNode n)=>n.Text.Length>0&&!tree.Any(c=>c.Parent==n.Id)&&!n.ClassName.Contains("EditText",StringComparison.Ordinal);
        bool Clickable(FriendNode n){for(int i=0;i<3;i++){if(n.Clickable)return true;if(!map.TryGetValue(n.Parent,out var parent))break;n=parent;}return false;}
        bool Contacts(FriendNode n)=>n.Text is "通讯录" or "Contacts";
        bool TopTitle(FriendNode n)=>n.Top>=0&&n.Bottom<height*.18;
        var contacts=tree.Any(n=>Contacts(n)&&TopTitle(n));
        var moments=tree.Any(n=>(n.Text is "朋友圈" or "Moments")&&TopTitle(n));
        if(!contacts&&!moments)return new("未识别",[]);
        var found=new Dictionary<int,string>();
        if(contacts){
            foreach(var row in tree.Where(n=>n.Clickable&&n.Right-n.Left>=width*.65&&n.Bottom-n.Top>=32*density&&n.Bottom-n.Top<=120*density)){
                var ancestor=row;var inList=false;
                for(int i=0;i<6&&map.TryGetValue(ancestor.Parent,out var parent);i++){if(parent.ClassName.Contains("RecyclerView",StringComparison.Ordinal)||parent.ClassName.Contains("ListView",StringComparison.Ordinal)){inList=true;break;}ancestor=parent;}
                if(!inList)continue;
                var contents=Contents(row);var labels=contents.Where(LeafText).ToArray();
                if(labels.Length!=1||!ValidName(labels[0].Text)||!contents.Any(Image))continue;
                var label=labels[0];if(label.Top<0||label.Bottom>height||label.Left<width*.12)continue;
                found[label.Id]=label.Text;
            }
        }else{
            foreach(var menu in tree.Where(n=>n.Description is "评论" or "Comment")){
                var card=menu;
                for(int depth=0;depth<6&&map.TryGetValue(card.Parent,out var parent);depth++){
                    card=parent;if(card.Bottom-card.Top>height*.85)break;
                    var contents=Contents(card);
                    if(contents.Count(n=>n.Description is "评论" or "Comment")!=1)break;
                    var avatars=contents.Where(n=>Image(n)&&n.Left<width*.25&&n.Bottom<menu.Top).ToArray();
                    if(avatars.Length!=1)continue;
                    var avatar=avatars[0];
                    var labels=contents.Where(n=>LeafText(n)&&ValidName(n.Text)&&Clickable(n)&&n.Left>=avatar.Right&&n.Top>=avatar.Top-8*density&&n.Top<=avatar.Top+24*density&&n.Bottom<menu.Top).ToArray();
                    if(labels.Length==1)found[labels[0].Id]=labels[0].Text;
                    break;
                }
            }
        }
        // Two distinct visible rows with the same label cannot be resolved to one friend.
        var candidates=found.Values.GroupBy(n=>n,StringComparer.Ordinal).Select(g=>new FriendCandidate(g.Key,contacts?"微信通讯录":"朋友圈可见作者",g.Count()>1)).ToArray();
        return new(contacts?"微信通讯录":"朋友圈可见作者",candidates);
    }
    public static string[] ValidateSelection(IEnumerable<string> selection,IReadOnlyList<FriendCandidate> candidates)
    {
        var names=selection.Distinct(StringComparer.Ordinal).ToArray();
        if(names.Length>20)throw new InvalidOperationException("最多选择 20 位好友。");
        if(names.Any(n=>!ValidName(n)||!candidates.Any(c=>c.Name==n&&!c.Ambiguous)))throw new InvalidOperationException("有未识别或同名的好友，请重新选择；同名时请先在微信设置不同备注。");
        return names;
    }
}
