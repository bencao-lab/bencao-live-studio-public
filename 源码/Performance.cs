using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Threading.Tasks;
partial class Studio {
    readonly Audience audience=new Audience(Path.Combine(AppPaths.DataRoot,"观众记忆.json"));
    readonly TextBox roomInput=new TextBox();
    readonly string roomFile=Path.Combine(AppPaths.DataRoot,"常用直播间.txt");
    string savedRoom="";
    void SaveRoom(){
        string value=roomInput.Text.Trim();
        if(value==savedRoom)return;
        try{Preferences.Write(roomFile,value);savedRoom=value;}
        catch(Exception ex){roomStatus.Text="房号保存失败："+ex.Message;}
    }
    readonly Label roomStatus=new Label{AutoSize=true,MaximumSize=new Size(190,0)},leaders=new Label{AutoSize=true,MaximumSize=new Size(190,0)};
    readonly Random random=new Random();
    readonly Dictionary<string,int> recentActions=new Dictionary<string,int>();
    CancellationTokenSource roomStop;
    DateTime nextBeat;
    Button receiveRoom;
    readonly Label lastIncoming=new Label{AutoSize=true,MaximumSize=new Size(190,0)};
    void RoomControls(){
        Section("直播间互动");AddInput("抖音房间（自动保存）",roomInput);
        if(AppPaths.PublicRelease)AddButton("登录抖音（接收礼物）",LoginDouyin);
        try{if(File.Exists(roomFile))roomInput.Text=savedRoom=File.ReadAllText(roomFile).Trim();}
        catch(Exception ex){roomStatus.Text="房号读取失败："+ex.Message;}
        roomInput.Leave+=delegate{SaveRoom();};
        FormClosing+=delegate{SaveRoom();};
        AddButton("接收弹幕和礼物",ConnectRoom);receiveRoom=(Button)panel.Controls[panel.Controls.Count-1];AddButton("停止接收",delegate{if(roomStop!=null)roomStop.Cancel();});
        panel.Controls.Add(roomStatus);panel.Controls.Add(lastIncoming);panel.Controls.Add(giftReceipt);panel.Controls.Add(leaders);
    }
    bool loginBusy;
    async void LoginDouyin(){
        if(loginBusy)return;
        if(roomStop!=null){roomStatus.Text="请先停止接收，再更新登录";return;}
        loginBusy=true;
        try{
            roomStatus.Text="请在弹出的抖音窗口完成登录";
            using(var proc=System.Diagnostics.Process.Start(AppPaths.ReceiverProcess("douyin/login.mjs"))){
                while(!proc.HasExited&&!lifetime.IsCancellationRequested)await Task.Delay(300);
                if(lifetime.IsCancellationRequested){if(!proc.HasExited)proc.Kill();return;}
                roomStatus.Text=proc.ExitCode==0?"登录已保存，可以接收弹幕和礼物":"登录未完成，请重试";
            }
        }catch(Exception){if(!IsDisposed)roomStatus.Text="登录窗口启动失败，请检查 Chrome 和接收组件";}
        finally{loginBusy=false;}
    }
    async void ConnectRoom(){
        if(roomStop!=null)return;
        roomStop=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        receiveRoom.Enabled=false;receiveRoom.Text="正在连接…";roomInput.ReadOnly=true;lastIncoming.Text="等待第一条弹幕或礼物";roomStatus.Text="正在连接…";leaders.Text="";
        try{using(var bridge=new RoomBridge())await bridge.Run(roomInput.Text.Trim(),RoomEvent,delegate(string status,string room,string counts){
            receiveRoom.Text=status=="connected"?"已连接 · 接收中":"连接中…";
            roomStatus.Text=status=="connected"?"正在接收抖音互动 · "+counts:"直播间："+status;
            if(room.Length>0&&room!=lastRoom){lastRoom=room;UpdateTop(room);}
        },roomStop.Token);}
        catch(OperationCanceledException){if(!IsDisposed)roomStatus.Text="已停止接收";}
        catch(Exception ex){if(!IsDisposed)roomStatus.Text="接收失败："+ex.Message;}
        finally{roomStop.Dispose();roomStop=null;lastRoom=null;if(!IsDisposed){receiveRoom.Text="接收弹幕和礼物";receiveRoom.Enabled=true;roomInput.ReadOnly=false;}}
    }
    string lastRoom;
    void UpdateTop(string room){string top=audience.Top(room);leaders.Text=top.Length==0?"":"常送礼观众（按件数）："+top;}
    void RoomEvent(Dictionary<string,object> row){
        string room=Convert.ToString(row["roomId"]),nick=Convert.ToString(row["nickname"]);
        if(Convert.ToString(row["kind"])=="gift"){
            var item=(Dictionary<string,object>)row["gift"];string viewer=Convert.ToString(row["viewerId"]);
            if(viewer.Length>0&&!audience.Record(room,viewer,Convert.ToString(row["id"]),nick,Convert.ToInt32(item["count"])))return;
            QueueGift(nick,Convert.ToString(item["name"]),Convert.ToInt32(item["count"]),item.ContainsKey("unitDiamonds")?Convert.ToInt64(item["unitDiamonds"]):0,room+":"+viewer);UpdateTop(room);
        }else{
            string text=Convert.ToString(row["text"]),viewer=Convert.ToString(row["viewerId"]),id=Convert.ToString(row["id"]);
            if(Preferences.Muted(text)){Flag("抖音",room,viewer,nick,id);return;}
            lastIncoming.Text="最近弹幕："+text;
            if(Convert.ToBoolean(row["replyable"]))QueueComment(text,room,viewer,nick,id);
        }
    }
    readonly TextBox apiKeyBox=new TextBox{UseSystemPasswordChar=true},voiceBox=new TextBox();
    readonly CheckBox licensed=new CheckBox{Text="已确认声音授权及使用条件",AutoSize=true};
    readonly Label voiceInfo=new Label{AutoSize=true,MaximumSize=new Size(192,0)};
    readonly ComboBox provider=new ComboBox();
    readonly TextBox urlBox=new TextBox(),modelBox=new TextBox();
    void VoiceControls(){
        Section("声音 · 当前角色");
        panel.Controls.Add(voiceInfo);
        var advancedVoice=new CheckBox{Text="高级设置：其他 TTS（实验性，未实测）",AutoSize=true,MaximumSize=new Size(190,0)};
        panel.Controls.Add(advancedVoice);
        AddSelect("语音服务",provider);provider.Items.AddRange(new object[]{"BreezeBlue（已实测）","其他兼容 TTS（实验性，未实测）"});
        var providerLabel=panel.Controls[panel.Controls.Count-2];
        advancedVoice.CheckedChanged+=delegate{
            provider.Visible=providerLabel.Visible=advancedVoice.Checked;
            if(!advancedVoice.Checked)provider.SelectedIndex=0;
        };
        provider.Visible=providerLabel.Visible=false;
        var help=new Label{AutoSize=true,MaximumSize=new Size(190,0)};panel.Controls.Add(help);
        int start=panel.Controls.Count;
        AddButton("申请 BreezeBlue API Key",delegate{System.Diagnostics.Process.Start("https://breezeblue.ai/app/developer/api-keys");});
        AddButton("获取音色 ID（声音工作台）",delegate{System.Diagnostics.Process.Start("https://breezeblue.ai/");});
        var breeze=new List<Control>();for(int i=start;i<panel.Controls.Count;i++)breeze.Add(panel.Controls[i]);
        start=panel.Controls.Count;AddInput("完整语音合成地址",urlBox);urlBox.MaxLength=2048;AddInput("模型名称",modelBox);
        var extra=new List<Control>();for(int i=start;i<panel.Controls.Count;i++)extra.Add(panel.Controls[i]);
        provider.SelectedIndexChanged+=delegate{
            bool custom=provider.SelectedIndex==1;
            if(custom)advancedVoice.Checked=true;
            foreach(var c in breeze)c.Visible=!custom;foreach(var c in extra)c.Visible=custom;
            apiKeyBox.Clear();voiceBox.Clear();licensed.Checked=false;
            help.Text=custom?"实验性接口，尚未实测具体服务商。填写语音地址、模型名和音色。需兼容 /audio/speech 并返回 24 kHz 单声道 PCM，本机服务可留空 Key。":"BreezeBlue 已实测。打开下方声音工作台，选择有权使用的音色并复制 voc_ 开头的音色 ID，再填写 API Key，保存后试听。";
        };
        AddInput("所选服务的 API Key",apiKeyBox);apiKeyBox.MaxLength=1024;
        AddInput("音色名称或 Voice ID",voiceBox);panel.Controls.Add(licensed);
        AddButton("保存声音到当前角色",delegate{try{ClearBilling();
            if(!licensed.Checked)throw new IOException("请先确认声音授权及使用条件");
            bool custom=provider.SelectedIndex==1;
            if(custom&&String.IsNullOrWhiteSpace(urlBox.Text))throw new IOException("请填写完整语音合成地址");
            Interaction.Bind(apiKeyBox.Text,voiceBox.Text,custom?urlBox.Text:"",custom?modelBox.Text:"");ReloadVoice();
        }catch(Exception ex){voiceInfo.Text=ex.Message;}});
        AddButton("试听已绑定声音",PreviewVoice);
        AddButton("解除当前角色绑定",delegate{try{Interaction.Unbind();ClearBilling();ReloadVoice();}catch(Exception ex){voiceInfo.Text=ex.Message;}});
    }
    void ReloadVoice(){
        apiKeyBox.Clear();voiceBox.Clear();licensed.Checked=false;
        try{var cfg=Interaction.ProviderSettings();provider.SelectedIndex=cfg[0].Length>0?1:0;urlBox.Text=cfg[0];modelBox.Text=cfg[1];voiceBox.Text=Interaction.BoundVoice;voiceInfo.Text=Preferences.RoleName+(voiceBox.Text.Length==0?" · 未绑定声音":" · 已保存绑定");}
        catch(Exception){voiceInfo.Text="绑定信息读取失败，请重新填写";}
    }
    async void PreviewVoice(){
        string role=Preferences.RoleId;
        try{voiceInfo.Text="正在试听…";var data=await Interaction.Speech("你好，很高兴见到你。",lifetime.Token);
            if(role!=Preferences.RoleId)return;await Interaction.Play(data,lifetime.Token);
            if(role==Preferences.RoleId)voiceInfo.Text="试听完成";
        }catch(Exception ex){if(!IsDisposed&&role==Preferences.RoleId)voiceInfo.Text="试听失败："+ex.Message;}
    }
    string Pick(string key,string[] actions){
        if(actions.Length==0)return "";if(actions.Length==1)return actions[0];int last;int n=recentActions.TryGetValue(key,out last)?(last+1+random.Next(actions.Length-1))%actions.Length:random.Next(actions.Length);
        recentActions[key]=n;return actions[n];
    }
    Task<string> CharacterReply(string text,List<string> history){
        var session=liveView;
        Action<string> act=null;
        if(session!=null&&lamp.ForeColor==Color.LightGreen)act=delegate(string prompt){if(liveView==session)SendVisual(prompt);};
        return Interaction.Generate(text,history,lifetime.Token,act);
    }

    readonly Queue<string> naturalEvents=new Queue<string>(),naturalRecent=new Queue<string>();
    long naturalVersion,naturalChecked;
    bool choosingNatural;
    void RememberNatural(string kind,string text){
        if(lifetime.IsCancellationRequested||String.IsNullOrWhiteSpace(text)||Preferences.Muted(text))return;
        naturalEvents.Enqueue(kind+"："+text.Substring(0,Math.Min(text.Length,180)));
        if(naturalEvents.Count>memoryLength)naturalEvents.Dequeue();UpdateBrainMemory();
        naturalVersion++;
    }
    async void NaturalPause(){
        if(choosingNatural)return;
        if(naturalVersion==naturalChecked){LookAtCamera();return;}
        var session=liveView;
        var motionAtStart=lastMotion;
        var version=naturalVersion;
        naturalChecked=version;
        var candidates=moves.Where(m=>!naturalRecent.Contains(m.Prompt)&&!Preferences.Muted(m.Name)&&!Preferences.Muted(m.Prompt)).ToArray();
        choosingNatural=true;
        try{
            int index=await Interaction.NaturalChoice(naturalEvents.ToArray(),candidates.Select(m=>m.Group+" / "+m.Name+"："+m.Prompt.Substring(0,Math.Min(m.Prompt.Length,100))).ToArray(),naturalRecent.ToArray(),lifetime.Token);
            if(session!=liveView||motionAtStart!=lastMotion||(naturalVersion-version>=new[]{4,2,1}[interruptSensitivity])||playingSong||speaking||draining||liveBusy||lifetime.IsCancellationRequested)return;
            if(index>=0&&moves.Contains(candidates[index])&&SendVisual(candidates[index].Prompt)){
                moveNote.Text="自然动作："+candidates[index].Name;
            }else LookAtCamera();
        }catch(OperationCanceledException){}
        catch(Exception ex){if(!IsDisposed&&session==liveView)moveNote.Text="自然动作跳过："+ex.Message;}
        finally{choosingNatural=false;}
    }
    DateTime lastMotion;
    void LookAtCamera(){SendVisual(Preferences.Get("gaze"));}
    void Motion(string instruction){
        if(String.IsNullOrWhiteSpace(instruction))return;
        if(liveView!=null&&lamp.ForeColor==Color.LightGreen){
            lastMotion=DateTime.UtcNow;
            SendVisual(instruction+"。"+Preferences.Get("gaze"));
        }
    }
    bool SendVisual(string prompt){
        if(String.IsNullOrWhiteSpace(prompt)||liveView==null||lamp.ForeColor!=Color.LightGreen||Preferences.Muted(prompt))return false;
        if(Preferences.Get("flip")=="开"&&System.Text.RegularExpressions.Regex.IsMatch(prompt,@"后\s*(空\s*)?翻|向后.{0,8}(空翻|翻腾|翻转一周)|后(空)?翻筋斗|back[ -]?(flip|somersault)|backward[s]?.{0,8}(flip|somersault)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return false;
        foreach(var term in Preferences.Lines("avoid"))if(prompt.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)return false;
        lastMotion=DateTime.UtcNow;liveView.Motion(prompt);PulseBrain(prompt);if(prompt!=Preferences.Get("gaze")){naturalRecent.Enqueue(prompt);if(naturalRecent.Count>4)naturalRecent.Dequeue();}return true;
    }
    string GiftAction(string gift){
        if(gift.Contains("礼花筒"))return Pick("party",Preferences.Lines("party"));
        if(gift.Contains("荧光棒"))return Pick("light",Preferences.Lines("light"));
        return Pick("simple",Preferences.Lines("simple"));
    }
    void Beat(){
        if(!playingSong||DateTime.UtcNow<nextBeat)return;nextBeat=DateTime.UtcNow.AddSeconds(Preferences.Number("beat"));
        Motion(Pick("song",Preferences.Lines("song")));
    }
}
