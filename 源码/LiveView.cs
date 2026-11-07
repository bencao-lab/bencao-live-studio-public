using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

sealed class LiveView : IDisposable {
    readonly WebView2 view=new WebView2();
    readonly HttpClient api=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(120)};
    readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=24000000};
    readonly Action<string> update;
    readonly string host;
    readonly string folder;
    string liveId;
    bool stopping;
    TaskCompletionSource<bool> sent;
    public LiveView(bool international,string resources,Action<string> status){
        host=international?"api.vidu.com":"api.vidu.cn";folder=resources;update=status;
        api.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Token",Vidu.SiteToken(international));
    }
    public async Task Start(Panel stage,string image,System.Threading.CancellationToken shutdown){
        view.Dock=DockStyle.Fill;stage.Controls.Add(view);view.SendToBack();
        string cache=Path.Combine(AppPaths.DataRoot,"WebView2");
        var env=await CoreWebView2Environment.CreateAsync(null,cache,new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
        await view.EnsureCoreWebView2Async(env);
        var core=view.CoreWebView2;
        core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;
        core.SetVirtualHostNameToFolderMapping("studio.example",folder,CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting+=(o,e)=>{if(e.Uri!="https://studio.example/index.html")e.Cancel=true;};
        var loaded=new TaskCompletionSource<bool>();
        core.NavigationCompleted+=(o,e)=>loaded.TrySetResult(e.IsSuccess);
        core.WebMessageReceived+=(o,e)=>{
            if(e.Source!="https://studio.example/index.html")return;
            string msg=e.TryGetWebMessageAsString();
            if(msg=="画面已到达")view.BringToFront();
            if(msg=="声音已送入"&&sent!=null)sent.TrySetResult(true);
            if(msg.StartsWith("失败")||msg.Contains("断开")||msg.Contains("已结束")){
                if(sent!=null)sent.TrySetException(new IOException(msg));
            }
            update(msg);
        };
        core.Navigate("https://studio.example/index.html");
        if(await Task.WhenAny(loaded.Task,Task.Delay(10000))!=loaded.Task||!await loaded.Task)throw new IOException("播出页面未就绪");
        if(stopping)throw new OperationCanceledException();
        string asset=RoleUpload.Bound(image,host=="api.vidu.com");
        var body=new Dictionary<string,object>{{"model","vidu-s2"},{"managed_rtc",new {provider="artc"}}};
        if(asset!=null)body["avatar_id"]=asset;
        else body["image_uri"]=SessionImage(image);
        for(int attempt=0;attempt<2;attempt++)
        {
        shutdown.ThrowIfCancellationRequested();
        using(var content=new StringContent(json.Serialize(body),Encoding.UTF8,"application/json"))
        using(var res=await api.PostAsync("https://"+host+"/live/s_avatar/component",content)){
            string responseBody=await res.Content.ReadAsStringAsync();
            if(!res.IsSuccessStatusCode){
                string message="HTTP "+(int)res.StatusCode;
                try{var error=json.Deserialize<Dictionary<string,object>>(responseBody);object reason;
                    if(error.TryGetValue("message",out reason))message=Convert.ToString(reason);
                }catch(ArgumentException){}
                if(message.Contains("still processing") && body.Remove("avatar_id")){
                    update("Vidu 正在处理形象，正在用当前角色首帧连接…");
                    body["image_uri"]=SessionImage(image);
                    continue;
                }
                throw new IOException(message);
            }
            var session=json.Deserialize<Dictionary<string,object>>(responseBody);
            liveId=Convert.ToString(((Dictionary<string,object>)session["live"])["id"]);
            session["host"]=host;
            core.PostWebMessageAsJson(json.Serialize(new {command="start",session=session,exitOnly=shutdown.IsCancellationRequested}));return;
        }
    }
    }
    static string SessionImage(string file){
        using(var frame=System.Drawing.Image.FromFile(file))
        using(var jpeg=new MemoryStream()){
            frame.Save(jpeg,System.Drawing.Imaging.ImageFormat.Jpeg);
            return "data:image/jpeg;base64,"+Convert.ToBase64String(jpeg.ToArray());
        }
    }
    public void Transition(){if(!stopping&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson("{\"command\":\"transition\"}");}
    public void Motion(string prompt){
        if(!stopping&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{command="motion",prompt=prompt}));
    }
    public async Task Audio(byte[] pcm){
        if(stopping||view.CoreWebView2==null)throw new IOException("实时会话已结束");
        if(sent!=null)throw new IOException("音频通道正忙");
        sent=new TaskCompletionSource<bool>();
        try{
            view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{command="speak",audio=Convert.ToBase64String(pcm)}));
            if(await Task.WhenAny(sent.Task,Task.Delay(pcm.Length/48+6000))!=sent.Task)throw new TimeoutException("音频传送超时");
            await sent.Task;
        }finally{sent=null;}
    }
    public async Task<bool> Stop(){
        stopping=true;
        if(sent!=null)sent.TrySetCanceled();
        if(view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson("{\"command\":\"stop\"}");
        if(String.IsNullOrEmpty(liveId))return true;
        for(int i=0;i<8;i++){
            await Task.Delay(650);
            using(var timeout=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5)))
            using(var r=await api.GetAsync("https://"+host+"/live/v1/lives/"+Uri.EscapeDataString(liveId),timeout.Token)){
                if(!r.IsSuccessStatusCode)continue;
                var d=json.Deserialize<Dictionary<string,object>>(await r.Content.ReadAsStringAsync());
                object live;if(d.TryGetValue("live",out live))d=(Dictionary<string,object>)live;
                object status;if(d.TryGetValue("status",out status)&&Convert.ToString(status)=="ended")return true;
            }
        }
        return false;
    }
    public void Dispose(){view.Dispose();api.Dispose();}
}
