using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

sealed class Audience {
    public sealed class Fan { public string Name{get;set;} public long Count{get;set;} }
    public sealed class Book {
        public int Version{get;set;}
        public Dictionary<string,Fan> Fans{get;set;}
        public List<string> Recent{get;set;}
        public Book(){Version=1;Fans=new Dictionary<string,Fan>();Recent=new List<string>();}
    }
    readonly string path;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    public Audience(string path){this.path=path;}
    Book Read(){
        if(!File.Exists(path))return new Book();
        var book=json.Deserialize<Book>(File.ReadAllText(path));
        if(book==null||book.Version!=1||book.Fans==null||book.Recent==null)throw new IOException("无法读取观众记忆");
        return book;
    }
    public bool Record(string room,string viewer,string id,string name,int count){
        if(String.IsNullOrWhiteSpace(viewer)||String.IsNullOrWhiteSpace(id)||count<1)return false;
        using(var gate=new Mutex(false,"Local\\BencaoAudienceBook")){
            if(!gate.WaitOne(2000))throw new IOException("观众记忆正忙");
            try{
                var b=Read();string eventKey=room+":"+id;
                if(b.Recent.Contains(eventKey))return false;
                string key=room+":"+viewer;Fan fan;
                if(!b.Fans.TryGetValue(key,out fan))b.Fans[key]=fan=new Fan();
                fan.Count=checked(fan.Count+count);fan.Name=name;b.Recent.Add(eventKey);
                if(b.Recent.Count>512)b.Recent.RemoveAt(0);
                Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";
                File.WriteAllText(tmp,json.Serialize(b));
                if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
                return true;
            }finally{gate.ReleaseMutex();}
        }
    }
    public string Top(string room){
        return String.Join("  ·  ",Read().Fans.Where(p=>p.Key.StartsWith(room+":",StringComparison.Ordinal)).OrderByDescending(p=>p.Value.Count).Take(5).Select(p=>p.Value.Name+" "+p.Value.Count));
    }
}
sealed class RoomBridge : IDisposable {
    readonly HttpClient http=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){BaseAddress=new Uri("http://127.0.0.1:"+AppPaths.ReceiverPort),Timeout=TimeSpan.FromSeconds(18)};
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly string client=Guid.NewGuid().ToString();string token;
    async Task<Dictionary<string,object>> Read(HttpResponseMessage response){
        using(response){response.EnsureSuccessStatusCode();return json.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());}
    }
    async Task<Dictionary<string,object>> Get(string path,CancellationToken ct){return await Read(await http.GetAsync(path,ct));}
    async Task<Dictionary<string,object>> Post(string path,object payload,CancellationToken ct){
        using(var req=new HttpRequestMessage(HttpMethod.Post,path)){
            req.Headers.Add("x-stage-token",token);
            req.Content=new StringContent(json.Serialize(payload),Encoding.UTF8,"application/json");
            return await Read(await http.SendAsync(req,ct));
        }
    }
    public async Task Run(string room,Action<Dictionary<string,object>> receive,Action<string,string,string> status,CancellationToken ct){
        Exception failure=null;
        try{await RunCore(room,receive,status,ct);}
        catch(Exception ex){failure=ex;}
        await Release();
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    async Task Release(){
            if(!String.IsNullOrEmpty(token)){
                using(var cleanup=new CancellationTokenSource(2500)){
                    try{await Post("/api/douyin/release",new{clientId=client},cleanup.Token).ConfigureAwait(false);}catch{}
                }
            }
    }
    async Task RunCore(string room,Action<Dictionary<string,object>> receive,Action<string,string,string> status,CancellationToken ct){
        Dictionary<string,object> info;
        info=null;
        try{info=await Get("/api/status",ct);}catch(HttpRequestException){}
        if(info==null){
            string server=Path.Combine(AppPaths.ReceiverRoot,"server.mjs");
            if(!File.Exists(server))throw new IOException("缺少本机抖音事件接收服务");
            using(var proc=Process.Start(AppPaths.ReceiverProcess("server.mjs"))){
                info=null;
                for(int i=0;i<25&&info==null;i++){await Task.Delay(250,ct);try{info=await Get("/api/status",ct);}catch(HttpRequestException){if(proc.HasExited)break;}}
                if(info==null)throw new IOException("本机弹幕服务启动失败");
            }
        }
        if(Convert.ToString(info["app"])!=AppPaths.ReceiverApp)throw new IOException("本机接收服务不匹配");
        token=Convert.ToString(info["token"]);
        var state=(Dictionary<string,object>)info["douyin"];
        if(String.IsNullOrWhiteSpace(room))room=Convert.ToString(state["webRid"]);
        if(String.IsNullOrWhiteSpace(room)){
            var config=await Get("/app/config.json",ct);room=Convert.ToString(((Dictionary<string,object>)config["douyin"])["defaultRoom"]);
        }
        if(String.IsNullOrWhiteSpace(room))throw new IOException("请填写房间号或链接");
        var snap=await Post("/api/douyin/connect",new{room=room,clientId=client},ct);
        long cursor=Convert.ToInt64(snap["cursor"]);string generation=Convert.ToString(snap["generation"]);
        while(true){
            ct.ThrowIfCancellationRequested();
            snap=await Post("/api/douyin/poll",new{clientId=client,after=cursor},ct);
            if(Convert.ToString(snap["generation"])!=generation)throw new IOException("接收服务已切换房间，请重新连接");
            string stateName=Convert.ToString(snap["state"]);
                        var wire=snap.ContainsKey("wire")?snap["wire"] as Dictionary<string,object>:null;
            string counts="弹幕 "+(wire==null?"—":Convert.ToString(wire["chat"]))+" · 原始礼物 "+(wire==null?"—":Convert.ToString(wire["gift"]))+" · 已识别 "+Convert.ToString(snap["receivedGifts"]);
            status(stateName,Convert.ToString(snap["roomId"]),counts);
            if(stateName=="failed"||stateName=="idle"||stateName=="disconnected")throw new IOException("房间接收结束："+Convert.ToString(snap["error"]));
            if(Convert.ToBoolean(snap["autoEnabled"]))throw new IOException("请先关闭原接收页的自动回复，避免重复答谢");
            foreach(Dictionary<string,object> row in (System.Collections.IEnumerable)snap["messages"])receive(row);
            cursor=Convert.ToInt64(snap["cursor"]);await Task.Delay(800,ct);
        }
    }
    public void Dispose(){http.Dispose();}
}
