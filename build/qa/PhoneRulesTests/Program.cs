using System.Net;
using System.Text;
using System.Text.Json;
using WeChatLikeMaui.Services;

int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;}
void Reject(Action action,string name){try{action();throw new Exception(name);}catch(InvalidOperationException){passed++;}}
async Task RejectAsync(Func<Task> action,string name){try{await action();throw new Exception(name);}catch(InvalidOperationException){passed++;}}
Check(PhoneRules.CompletionUri("https://example.invalid/v1").AbsoluteUri=="https://example.invalid/v1/chat/completions","append path");
Check(PhoneRules.CompletionUri("https://example.invalid/custom/v1/").AbsolutePath=="/custom/v1/chat/completions","keep provider prefix");
foreach(var input in new[]{"http://example.invalid/v1","https://user:password@example.invalid/v1","https://example.invalid/v1?key=value","https://example.invalid/v1#key","https://example.invalid/v1/chat/completions"})Reject(()=>PhoneRules.CompletionUri(input),"reject unsafe base");
var id=PhoneRules.Fingerprint("key","Alice","hello",1);
var message=new PhoneMessage(id,"key","Alice","hello",1,true);
Check(PhoneRules.Same(message,message),"same notification");
Check(!PhoneRules.Same(message,message with{Text="new message"}),"reject changed content");
Check(!PhoneRules.Same(message,message with{PostedAt=2}),"reject updated timestamp");
Check(!PhoneRules.Same(message,message with{NotificationKey="other"}),"reject different conversation");
Check(!PhoneRules.Same(message,message with{CanReply=false}),"reject missing reply action");
Check(PhoneRules.Allowed("Alice",new[]{"Alice"})&&!PhoneRules.Allowed("Alice 2",new[]{"Alice"})&&!PhoneRules.Allowed("alice",new[]{"Alice"}),"exact allowlist");
Check(PhoneRules.Fingerprint("key","Alice","hello",2)!=id,"fingerprint freshness");
var profile=new PhoneAiProfile("https://example.invalid/v1","model-test","Friendly reply");
using(var handler=new FixtureHandler(async request=>{
    Check(request.RequestUri!.AbsolutePath=="/v1/chat/completions" && request.Headers.Authorization?.Scheme=="Bearer" && request.Headers.Authorization.Parameter=="fixture-secret","authenticated direct API");
    using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    Check(json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()=="incoming fixture message" && json.RootElement.GetProperty("stream").GetBoolean()==false,"send only selected input");
    return new(HttpStatusCode.OK){Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"preview only\"}}]}",Encoding.UTF8,"application/json")};
})){
    Check(await PhoneRules.GenerateAsync(profile,"fixture-secret","incoming fixture message",default,handler)=="preview only","parse compatible response");
}
await RejectAsync(()=>PhoneRules.GenerateAsync(profile,"","message",default),"missing key");
await RejectAsync(()=>PhoneRules.GenerateAsync(profile,"fixture-secret",new string('a',8001),default),"oversized input");
using(var handler=new FixtureHandler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=new StringContent("fixture secret from provider must never surface")})))
    await RejectAsync(()=>PhoneRules.GenerateAsync(profile,"fixture-secret","message",default,handler),"provider failure");
using(var handler=new FixtureHandler(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',1_048_577))})))
    await RejectAsync(()=>PhoneRules.GenerateAsync(profile,"fixture-secret","message",default,handler),"oversized response");
Check(OverlayGeometry.Clamp(-500,-200,64,64,390,844,12)==new OverlayPosition(12,12),"drag remains on screen at top left");
Check(OverlayGeometry.Clamp(1000,2000,64,64,390,844,12)==new OverlayPosition(314,768),"drag remains on screen at bottom right");
Check(OverlayGeometry.Clamp(500,500,330,540,844,390,12)==new OverlayPosition(500,0),"oversize legacy panel has a reachable top");
Check(OverlayGeometry.Clamp(10,10,64,64,40,40,12)==new OverlayPosition(0,0),"tiny display clamps without exception");
using(var session=new OverlaySession()){
    var first=session.BeginGeneration();Check(session.Accepts(first.Revision),"current generation is accepted");
    session.Navigate();Check(first.Token.IsCancellationRequested&&!session.Accepts(first.Revision),"collapse/navigation cancels generation and rejects stale result");
    var second=session.BeginGeneration();session.Pause();Check(session.IsPaused&&second.Token.IsCancellationRequested&&!session.Accepts(second.Revision),"pause cancels generation and rejects completion");
    Reject(()=>session.BeginGeneration(),"paused assistant cannot generate");
    session.Resume();var third=session.BeginGeneration();Check(session.Accepts(third.Revision),"explicit resume permits new manual work");
    session.Dispose();Check(third.Token.IsCancellationRequested&&!session.Accepts(third.Revision),"stop rejects late completion");
    Reject(()=>session.Resume(),"stopped session cannot resume");
}
var policy=new MomentWatchPolicy();var baseline=new[]{new WatchedMoment("old","Alice",true)};
Check(policy.Observe(baseline,true,"window-1",new[]{"Alice"})==null,"initial visible posts are baseline only");
var added=new[]{new WatchedMoment("new","Alice",true),baseline[0]};
Check(policy.Observe(added,true,"window-1",new[]{"Alice"})?.Id=="new","new recent selected-author post is reserved");
Check(policy.Observe(added,true,"window-1",new[]{"Alice"})==null,"uncertain UI attempt is not retried");
Check(policy.Observe(new[]{new WatchedMoment("foreign","Bob",true)},true,"window-1",new[]{"Alice"})==null,"unselected author never matches");
Check(policy.Observe(new[]{new WatchedMoment("historical","Alice",false)},true,"window-1",new[]{"Alice"})==null,"old content not auto-liked");
Check(policy.Observe(new[]{new WatchedMoment("locked","Alice",true)},false,"window-1",new[]{"Alice"})==null,"locked/foreign/not-at-top frame cannot act");
Check(policy.Observe(new[]{new WatchedMoment("after-return","Alice",true)},true,"window-1",new[]{"Alice"})==null,"resume reseeds baseline");
Check(policy.Observe(new[]{new WatchedMoment("changed-window","Alice",true)},true,"window-2",new[]{"Alice"})==null,"new window reseeds baseline");
var batch=new[]{new WatchedMoment("batch-a","Alice",true),new WatchedMoment("batch-b","Alice",true)};
Check(policy.Observe(batch,true,"window-2",new[]{"Alice"})?.Id=="batch-a","one candidate per inspection");
Check(policy.Observe(batch,true,"window-2",new[]{"Alice"})?.Id=="batch-b","remaining candidate can be inspected later");
Check(MomentWatchPolicy.Identity("Alice","body")!=MomentWatchPolicy.Identity("Bob","body"),"fingerprints bind author");
Check(FriendPickerRules.ValidName("A")&&FriendPickerRules.ValidName("123"),"single-letter and numeric friend names remain eligible when row structure proves contact");
Check(!FriendPickerRules.ValidName("好友\n伪造"),"multiline names rejected");
// Synthetic accessibility trees exercise the actual read-only extraction rules.
FriendNode Node(int id,int parent,string text,string cls,int left,int top,int right,int bottom,bool click=false,string description="")=>new(id,parent,text,description,cls,click,left,top,right,bottom);
var contacts=new List<FriendNode>{
    Node(0,-1,"","FrameLayout",0,0,390,844),Node(1,0,"通讯录","TextView",10,30,120,65),Node(2,0,"","RecyclerView",0,90,390,760),
    Node(3,2,"","LinearLayout",0,100,390,164,true),Node(4,3,"","ImageView",14,108,62,156),Node(5,3,"好友甲","TextView",76,113,250,145),
    Node(6,2,"","LinearLayout",0,164,390,228,true),Node(7,6,"","ImageView",14,172,62,220),Node(8,6,"新的朋友","TextView",76,177,250,209)};
var frame=FriendPickerRules.Extract("com.tencent.mm",true,contacts,390,844,1);
Check(frame.Source=="微信通讯录"&&frame.Candidates.Count==1&&frame.Candidates[0].Name=="好友甲","recognize avatar/name row and reject service entry");
Check(FriendPickerRules.Extract("other.app",true,contacts,390,844,1).Candidates.Count==0,"foreign app cannot become friend source");
Check(FriendPickerRules.Extract("com.tencent.mm",false,contacts,390,844,1).Candidates.Count==0,"locked phone cannot supply contacts");
var chat=contacts.Select(n=>n.Id==1?n with{Text="聊天会话"}:n).ToArray();
Check(FriendPickerRules.Extract("com.tencent.mm",true,chat,390,844,1).Candidates.Count==0,"chat text cannot masquerade as contacts");
var bottomTab=contacts.Select(n=>n.Id==1?n with{Top=790,Bottom=830}:n).ToArray();
Check(FriendPickerRules.Extract("com.tencent.mm",true,bottomTab,390,844,1).Candidates.Count==0,"contacts navigation label alone is not a contacts page");
var duplicate=contacts.Concat(new[]{Node(9,2,"","LinearLayout",0,228,390,292,true),Node(10,9,"","ImageView",14,236,62,284),Node(11,9,"好友甲","TextView",76,241,250,273)}).ToArray();
var ambiguous=FriendPickerRules.Extract("com.tencent.mm",true,duplicate,390,844,1);
Check(ambiguous.Candidates.Single().Ambiguous,"same-name visible rows are marked ambiguous");
Reject(()=>FriendPickerRules.ValidateSelection(new[]{"好友甲"},ambiguous.Candidates),"ambiguous friend cannot be saved");
Reject(()=>FriendPickerRules.ValidateSelection(new[]{"未识别人"},frame.Candidates),"unknown name cannot be injected into selection");
Check(FriendPickerRules.ValidateSelection(new[]{"好友甲","好友甲"},frame.Candidates).Length==1,"repeated selection is stored once");
var many=Enumerable.Range(0,21).Select(n=>new FriendCandidate("好友"+n,"fixture")).ToArray();
Reject(()=>FriendPickerRules.ValidateSelection(many.Select(c=>c.Name),many),"more than twenty selected friends rejected");
Check(FriendPickerRules.ValidateSelection([],frame.Candidates).Length==0,"clear selection saves empty list");
var post=new List<FriendNode>{Node(0,-1,"","FrameLayout",0,0,390,844),Node(1,0,"朋友圈","TextView",10,30,130,64),Node(2,0,"","LinearLayout",0,110,390,420),Node(3,2,"","ImageView",12,120,60,168),Node(4,2,"好友乙","TextView",72,122,220,150,true),Node(5,2,"动态文字","TextView",72,190,310,280),Node(6,2,"","Button",310,370,370,410,true,"评论")};
Check(FriendPickerRules.Extract("com.tencent.mm",true,post,390,844,1).Candidates.Single().Name=="好友乙","visible Moments author binds avatar and comment-menu card");
var bodyOnly=post.Where(n=>n.Id!=4).ToArray();
Check(FriendPickerRules.Extract("com.tencent.mm",true,bodyOnly,390,844,1).Candidates.Count==0,"post body cannot become friend name");
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,checks=passed,realNetworkRequests=false,realWechatActions=false,androidOverlayRuntimeVerified=false}));

sealed class FixtureHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> response):HttpMessageHandler{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>response(request);
}
