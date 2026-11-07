using System;
using System.Drawing;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

sealed class Canvas : Control {
    public Image Frame;
    public bool Vertical=true;
    public Canvas(){
        DoubleBuffered=true;
        SetStyle(ControlStyles.ResizeRedraw,true);
        BackColor=Color.FromArgb(20,23,22);
        Dock=DockStyle.Fill;
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e); if(ClientSize.Width<1 || ClientSize.Height<1) return;
        double aspect=Vertical?9.0/16:16.0/9;
        int w=Math.Min(Width,(int)(Height*aspect)), h=(int)(w/aspect);
        Rectangle area=new Rectangle((Width-w)/2,(Height-h)/2,w,h);
        e.Graphics.FillRectangle(Brushes.Black,area);
        if(Frame==null) return;
        double scale=Math.Min((double)w/Frame.Width,(double)h/Frame.Height);
        int iw=(int)(Frame.Width*scale), ih=(int)(Frame.Height*scale);
        e.Graphics.DrawImage(Frame,area.X+(w-iw)/2,area.Y+(h-ih)/2,iw,ih);
    }
}

partial class Studio : Form {
    const string Nameplate="本草收藏 · 数字人直播台";
    readonly Canvas preview=new Canvas();
    FlowLayoutPanel panel;
    readonly List<FlowLayoutPanel> pages=new List<FlowLayoutPanel>();
    readonly Label lamp=new Label{AutoSize=true};
    readonly TextBox input=new TextBox();
    readonly Panel conversation=new Panel(), messages=new Panel{AutoScroll=true};
    readonly Button sendChat=new Button{Text="发送",FlatStyle=FlatStyle.Flat};
    readonly Label chatHeading=new Label{Text="与白芷对话",AutoSize=true};
    readonly List<Label> bubbles=new List<Label>();
    readonly List<string> history=new List<string>();
    readonly Panel scroll=new Panel();
    readonly ComboBox roles=new ComboBox(), sites=new ComboBox(), format=new ComboBox();
    readonly Label song=new Label();
    readonly string roleDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"角色");
    readonly string settings=Path.Combine(AppPaths.DataRoot,"站点.txt");
    readonly Panel stage=new Panel();
    readonly TextBox comment=new Composer(), donor=new TextBox();
    readonly ComboBox gift=new ComboBox();
    readonly Label replyStatus=new Label();
        sealed class GiftThanks {
        public string Viewer,Nick,Name;
        public long Count,Value;
        public long LastVersion; public string State="待答谢";
        public int Tier{get{return Value>=100?2:Value>=10?1:0;}}
    }
    readonly List<GiftThanks> thanks=new List<GiftThanks>();
    readonly Label giftReceipt=new Label{AutoSize=true,MaximumSize=new Size(190,0),Text="礼物：等待接收"};
    readonly Queue<string> playlist=new Queue<string>();
    readonly CancellationTokenSource lifetime=new CancellationTokenSource();
    Tuple<string,string,string,string,string> latestComment;
    bool draining, playingSong, speaking;

    readonly System.Windows.Forms.Timer musicWatch=new System.Windows.Forms.Timer();

    [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int mciSendString(string command,System.Text.StringBuilder buffer,int capacity,IntPtr window);
    public Studio(){
        Text=Nameplate+(AppPaths.PublicRelease?" · 外发测试版":""); AutoScaleMode=AutoScaleMode.Dpi;
        var work=Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize=new Size(Math.Min(960,work.Width),Math.Min(600,work.Height));
        Size=new Size(Math.Min(1280,work.Width),Math.Min(800,work.Height)); StartPosition=FormStartPosition.CenterScreen;
        MinimumSize=new Size(Math.Min(640,work.Width),Math.Min(510,work.Height));
        AutoScaleMode=AutoScaleMode.None;
        BackColor=Paper; ForeColor=Color.FromArgb(40,36,40);
        Font=new Font("Microsoft YaHei UI",10);
        rail.BackColor=Color.FromArgb(41,38,43);rail.ForeColor=Color.WhiteSmoke;
        rail.Controls.Add(new Label{Text="LOGO",Location=new Point(12,20),Size=new Size(58,27),BorderStyle=BorderStyle.FixedSingle,TextAlign=ContentAlignment.MiddleCenter});
        rail.Controls.Add(new Label{Text="本草收藏",Location=new Point(12,66),AutoSize=true,Font=new Font("SimSun",16)});
        rail.Controls.Add(new Label{Text="数字人直播台",Location=new Point(12,99),AutoSize=true});
        Controls.Add(rail);rail.Controls.Add(lamp);Controls.Add(scroll);Controls.Add(stage);stage.Controls.Add(preview);
        scroll.AutoScroll=true;preview.Dock=DockStyle.Fill;preview.BackColor=Color.FromArgb(31,29,32);
        Lamp(false);
        foreach(string title in new[]{"本地对话","角色与声音","直播工作台","模型设置","记忆与大脑"}){
            var page=new FlowLayoutPanel{Name=title,Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(12),FlowDirection=FlowDirection.TopDown,WrapContents=false,Visible=false};
            pages.Add(page);scroll.Controls.Add(page);
            var button=new Button{Text=title,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(7,0,0,0),ForeColor=Color.WhiteSmoke};
            button.FlatAppearance.BorderSize=0;rail.Controls.Add(button);nav.Add(button);
            button.Click+=delegate{ShowPage(page);};
        }
        conversation.Visible=false;Controls.Add(conversation);
        conversation.Controls.AddRange(new Control[]{chatHeading,messages,input,sendChat});
        input.MaxLength=120;sendChat.BackColor=Color.FromArgb(129,58,74);sendChat.ForeColor=Color.White;
        sendChat.Click+=delegate{SendLocal();};
        chatHeading.Font=new Font(Font,FontStyle.Bold);
        input.KeyDown+=(o,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;SendLocal();}};
        panel=pages[1];Section("角色");AddSelect("当前角色",roles);AddButton("导入新角色图片",ImportRole);AddButton("上传当前角色到 Vidu",UploadRole);panel.Controls.Add(roleStatus);VoiceControls();PreferenceControls();
        panel=pages[3];Section("视频 · Vidu S2");AddSelect("Vidu 站点",sites);AddButton("检查 Vidu 连接",CheckVidu);
        panel.Controls.Add(modelResult);BuildModelSettings();panel=pages[2];Section("播出控制");AddSelect("画面比例",format);ShowPage(panel);
        sites.Items.AddRange(new object[]{"国内站","国际站"});
        sites.SelectedIndex=File.Exists(settings)&&File.ReadAllText(settings).Trim()=="intl"?1:0;
        sites.SelectedIndexChanged+=delegate{ClearBilling();ShowRoleStatus();videoSecret.Clear();modelResult.Text="";
            try{Directory.CreateDirectory(Path.GetDirectoryName(settings));File.WriteAllText(settings,sites.SelectedIndex==1?"intl":"cn");}
            catch(IOException ex){MessageBox.Show(ex.Message,Text);}
        };
        format.Items.AddRange(new object[]{"竖屏 9:16","横屏 16:9"}); format.SelectedIndex=0;
        format.SelectedIndexChanged+=delegate{preview.Vertical=format.SelectedIndex==0;preview.Invalidate();};
        roles.SelectedIndexChanged+=delegate{
            string id=(string)roles.SelectedItem;
            if(id==null)return;
            if((!input.Enabled||draining||playingSong||liveBusy||liveView!=null)&&selectedRole!=null&&id!=selectedRole){
                roles.SelectedItem=selectedRole;roleStatus.Text="结束当前会话或播放后即可切换角色";return;
            }
            if(id==selectedRole)return;
            SwitchRoleHistory(id);selectedRole=id;
            naturalEvents.Clear();naturalRecent.Clear();spokenTurns.Clear();UpdateBrainMemory();
            var dir=RoleFolder(id);var labelFile=Path.Combine(dir,"名称.txt");
            string name=File.Exists(labelFile)?File.ReadAllText(labelFile).Trim():id;
            Preferences.SelectRole(id,name);chatHeading.Text="与"+name+"对话";
            ClearBilling();ReloadVoice();reloadRoleEditor();RefreshMoves();
            string file=Path.Combine(dir,"头像.png");
            ShowRoleStatus();using(Image image=Image.FromFile(file)){var old=preview.Frame;preview.Frame=new Bitmap(image);if(old!=null)old.Dispose();}
            preview.Invalidate();
        };
        if(Directory.Exists(roleDir))foreach(var dir in Directory.GetDirectories(roleDir))
            if(File.Exists(Path.Combine(dir,"头像.png")))roles.Items.Add(Path.GetFileName(dir));
        if(Directory.Exists(userRoles))foreach(var dir in Directory.GetDirectories(userRoles)){
            string id=Path.GetFileName(dir);if(!roles.Items.Contains(id)&&File.Exists(Path.Combine(dir,"头像.png")))roles.Items.Add(id);
        }
        if(roles.Items.Count>0)roles.SelectedIndex=0;
        
        AddButton("连接实时数字人",StartLive);AddButton("结束实时会话",StopLive);BuildMoves();
        Section("歌曲表演");
        if(!AppPaths.PublicRelease)AddButton("播放白芷演唱片段",PlayVocalDemo);
        AddButton("播放本地歌曲",PlaySong);AddButton("停止歌曲",StopSong);
        PairLastButtons();
        song.Width=190;song.Height=23;song.AutoEllipsis=true;panel.Controls.Add(song);
        Section("互动试播");AddButton("弹幕试播",delegate{GiftMode(false);});AddButton("礼物答谢",delegate{GiftMode(true);});PairLastButtons();
        int first=panel.Controls.Count;
        panel.Controls.Add(testLog);AddInput("测试弹幕",comment);AddButton("发送",SubmitTestComment);
        ((Composer)comment).Send=SubmitTestComment;
        for(int i=first;i<panel.Controls.Count;i++)commentControls.Add(panel.Controls[i]);
        first=panel.Controls.Count;
        AddInput("送礼昵称",donor);gift.Items.AddRange(new object[]{"小心心","棒棒糖","鲜花","闪耀星辰","礼花筒"});
        AddSelect("礼物名称",gift);gift.SelectedIndex=0;AddButton("试播礼物答谢",delegate{QueueGift(donor.Text,gift.Text);});
        for(int i=first;i<panel.Controls.Count;i++)giftControls.Add(panel.Controls[i]);
        GiftMode(false);
        replyStatus.Width=194;replyStatus.Height=52;panel.Controls.Add(replyStatus);
        Resize+=delegate{Arrange();};Arrange();
        var gazeTimer=new System.Windows.Forms.Timer{Interval=1000};
        gazeTimer.Tick+=delegate{if(liveView!=null&&lamp.ForeColor==Color.LightGreen&&!playingSong&&!speaking&&!draining&&!liveBusy&&(DateTime.UtcNow-lastMotion).TotalSeconds>=actionInterval)NaturalPause();};
        gazeTimer.Start();FormClosed+=delegate{gazeTimer.Dispose();};
        RoomControls();Section("直播间禁言词");
        var muteEditor=new TextBox{Multiline=true,Width=190,Height=95,ScrollBars=ScrollBars.Vertical,Text=Preferences.Get("mute").Replace("\r","").Replace("\n",Environment.NewLine)};
        panel.Controls.Add(muteEditor);AddButton("保存全局禁言词",delegate{try{Preferences.SaveGlobal(muteEditor.Text);roomStatus.Text="已保存全局禁言词";}catch(Exception ex){roomStatus.Text=ex.Message;}});
        musicWatch.Interval=450; musicWatch.Tick+=delegate{
            if(!playingSong)return;Beat();
            var mode=new System.Text.StringBuilder(32);
            if(mciSendString("status bencao_music mode",mode,mode.Capacity,IntPtr.Zero)!=0 || mode.ToString()=="stopped")FinishSong();
        };
        bool shutdownReady=false;
        FormClosing+=async delegate(object sender,FormClosingEventArgs e){
            if(shutdownReady)return;
            e.Cancel=true;
            if(closingStudio)return;
            closingStudio=true;
            lifetime.Cancel();gazeTimer.Stop();musicWatch.Stop();StopSong();Enabled=false;
            Text="直播台正在关闭 · 正在断开服务";
            try{
                while(liveBusy)await Task.Delay(100);
                if(liveView!=null)await CloseLive();
                if(liveView!=null){
                    Text="云端结束尚未确认 · 点击关闭重试";
                    MessageBox.Show(this,replyStatus.Text+"。请检查网络，点击窗口关闭按钮重试。","云端结束尚未确认");
                    return;
                }
                shutdownReady=true;
                BeginInvoke(new Action(Close));
            }finally{closingStudio=false;}
        };
        FormClosed+=delegate{StopSong();musicWatch.Dispose();if(preview.Frame!=null)preview.Frame.Dispose();};

    }
    static readonly Color Paper=Color.FromArgb(244,241,236);
    readonly Panel rail=new Panel();
    readonly List<Button> nav=new List<Button>();

    readonly Label modelResult=new Label{Width=192,Height=54};
    void Arrange(){
        if(pages.Count!=5||panel==null)return;
        int w=ClientSize.Width,h=ClientSize.Height;
        bool tall=h>w;int left=tall?112:152,x=left;
        rail.SetBounds(0,0,left,h);
        for(int i=0;i<nav.Count;i++)nav[i].SetBounds(6,154+i*49,left-12,38);
        lamp.Font=rail.Font;lamp.Location=new Point(8,h-36);
        bool showBrain=panel==pages[4];brainPage.Visible=showBrain;stage.Visible=!showBrain;
        if(showBrain){scroll.Visible=false;conversation.Visible=false;brainPage.SetBounds(x,0,w-x,h);brainPage.BringToFront();return;}
        bool chat=panel==pages[0];
        conversation.Visible=chat;scroll.Visible=!chat;preview.Visible=true;
        if(chat){
            int available=w-x;
            if(tall){
                int picture=Math.Min((int)(h*0.48),available*16/9);
                stage.SetBounds(x,0,available,picture);
                conversation.SetBounds(x,picture,available,h-picture);
            }else{
                int picture=Math.Min(available-248,Math.Min(available/2,h*9/16));
                stage.SetBounds(x,0,picture,h);
                conversation.SetBounds(x+picture,0,available-picture,h);
            }
            int chatHeight=conversation.ClientSize.Height;
            int cw=conversation.ClientSize.Width;
            chatHeading.Location=new Point(20,18);
            messages.SetBounds(12,56,cw-24,chatHeight-126);
            input.SetBounds(20,chatHeight-48,cw-128,32);
            sendChat.SetBounds(cw-98,chatHeight-55,78,38);
            LayoutMessages();return;
        }
        if(tall){
            int ph=Math.Min(896,Math.Max(140,(int)((h-64)*0.63)));
            stage.SetBounds(x,0,w-x,ph);
            scroll.SetBounds(x,ph+8,w-x,Math.Max(80,h-ph-8));
        }else{
            scroll.SetBounds(w-238,0,234,h);
            stage.SetBounds(x,0,w-246-x,h);
        }
    }
    readonly List<Control> commentControls=new List<Control>(),giftControls=new List<Control>();
    void GiftMode(bool giftTab){
        foreach(var c in giftControls)c.Visible=giftTab;
        foreach(var c in commentControls)c.Visible=!giftTab;
    }
    void PairLastButtons(){
        int n=panel.Controls.Count;
        var a=panel.Controls[n-2];var b=panel.Controls[n-1];
        var row=new FlowLayoutPanel{Width=196,Height=40,Margin=Padding.Empty,WrapContents=false};
        a.Width=92;b.Width=92;row.Controls.Add(a);row.Controls.Add(b);panel.Controls.Add(row);
    }
    void Section(string name){
        panel.Controls.Add(new Label{Text=name,AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(3,10,3,4)});
    }
    void ShowPage(FlowLayoutPanel selected){
        foreach(var page in pages)page.Visible=page==selected;
        panel=selected;scroll.AutoScrollPosition=Point.Empty;foreach(var n in nav)n.BackColor=n.Text==selected.Name?Color.FromArgb(80,64,73):rail.BackColor;Arrange();
    }
    void Lamp(bool on){
        lamp.Text=on?"● Vidu 已连接":"● Vidu 未连接";lamp.ForeColor=on?Color.LightGreen:Color.LightCoral;
    }
    void Bubble(string text,bool mine){
        var label=new Label{Text=text,Tag=mine,Padding=new Padding(12,8,12,8),BackColor=mine?Color.FromArgb(237,220,225):Color.White,AutoSize=false};
        messages.Controls.Add(label);bubbles.Add(label);
        if(bubbles.Count>100){bubbles[0].Dispose();bubbles.RemoveAt(0);}
        LayoutMessages();messages.ScrollControlIntoView(label);
    }
    void LayoutMessages(){
        messages.AutoScrollPosition=Point.Empty;
        int w=Math.Max(120,messages.Width-24),y=6;
        foreach(var label in bubbles){
            int max=Math.Max(80,Math.Min(520,w*3/4));
            var size=TextRenderer.MeasureText(label.Text,Font,new Size(max-24,2000),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl);
            int width=Math.Min(max,size.Width+24);
            label.SetBounds((bool)label.Tag?w-width:4,y,width,size.Height+16);
            y+=label.Height+12;
        }
        if(bubbles.Count>0)messages.ScrollControlIntoView(bubbles[bubbles.Count-1]);
    }
    string selectedRole;
    readonly Dictionary<string,List<string>> chats=new Dictionary<string,List<string>>();
    readonly Dictionary<string,List<Tuple<string,bool>>> logs=new Dictionary<string,List<Tuple<string,bool>>>();
    readonly string userRoles=Path.Combine(AppPaths.DataRoot,"自有角色");
    string RoleFolder(string id){var dir=Path.Combine(roleDir,id);return Directory.Exists(dir)?dir:Path.Combine(userRoles,id);}
    void ImportRole(){
        if(liveView!=null||liveBusy||playingSong||draining||!input.Enabled){roleStatus.Text="请在会话和播放结束后切换角色";return;}
        using(var dlg=new OpenFileDialog{Filter="角色图片|*.png;*.jpg;*.jpeg"}){
            if(dlg.ShowDialog(this)!=DialogResult.OK)return;
            try{
                string id=Path.GetFileNameWithoutExtension(dlg.FileName);
                string folder=Path.Combine(userRoles,id);
                if(String.IsNullOrWhiteSpace(id)||id.TrimEnd(' ','.')!=id||Directory.Exists(folder)||Directory.Exists(Path.Combine(roleDir,id)))throw new IOException("请用未使用的角色名命名图片");
                using(var photo=Image.FromFile(dlg.FileName)){Directory.CreateDirectory(folder);photo.Save(Path.Combine(folder,"头像.png"),System.Drawing.Imaging.ImageFormat.Png);}
                roles.Items.Add(id);roles.SelectedItem=id;
            }catch(Exception ex){roleStatus.Text=ex.Message;}
        }
    }
    void SwitchRoleHistory(string id){
        if(selectedRole!=null){
            chats[selectedRole]=new List<string>(history);
            logs[selectedRole]=bubbles.ConvertAll(l=>Tuple.Create(l.Text,(bool)l.Tag));
        }
        foreach(var l in bubbles)l.Dispose();bubbles.Clear();history.Clear();input.Clear();
        if(chats.ContainsKey(id))history.AddRange(chats[id]);
        if(logs.ContainsKey(id))foreach(var pair in logs[id])Bubble(pair.Item1,pair.Item2);
    }
    async void SendLocal(){
        string text=input.Text.Trim();
        if(!input.Enabled||text.Length==0)return;
        if(Preferences.Muted(text)){input.Clear();Flag("本地对话","","","本机",null);return;}
        input.Enabled=false;sendChat.Enabled=false;input.Clear();Bubble(text,true);
        try{
            var answer=await CharacterReply(text,history);
            if(answer==Preferences.Refusal){Flag("本地对话","","","本机",null);return;}
            if(answer==SuppressedOutput)return;
            if(String.IsNullOrWhiteSpace(answer))throw new IOException("没有取得有效回答");
            if(answer!=Preferences.Refusal){history.Add(text);history.Add(answer);if(history.Count>16)history.RemoveRange(0,2);}
            Bubble(answer,false);var session=liveView;
            if(session!=null&&lamp.ForeColor==Color.LightGreen){
                var audio=await Interaction.Speech(answer,lifetime.Token);
                if(liveView==session)await session.Audio(audio);
            }
        }catch(Exception ex){if(!lifetime.IsCancellationRequested)Bubble("未完成："+ex.Message,false);}
        finally{if(!IsDisposed){input.Enabled=true;sendChat.Enabled=true;if(conversation.Visible)input.Focus();}}
    }
    void AddInput(string name,TextBox box){
        panel.Controls.Add(new Label{Text=name,AutoSize=true,Margin=new Padding(3,9,0,3)});
        box.Width=190;box.MaxLength=120;panel.Controls.Add(box);
    }
    sealed class Composer : TextBox{
        public Action Send;
        bool composing;
        protected override void WndProc(ref Message m){
            if(m.Msg==0x10d)composing=true;
            base.WndProc(ref m);
            if(m.Msg==0x10e)composing=false;
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keys){
            if(keys==Keys.Enter&&!composing){Send();return true;}
            return base.ProcessCmdKey(ref msg,keys);
        }
    }
    readonly FlowLayoutPanel testLog=new FlowLayoutPanel{Width=194,Height=178,AutoScroll=true,WrapContents=false,FlowDirection=FlowDirection.TopDown};
    readonly Queue<Tuple<string,Label>> pendingTests=new Queue<Tuple<string,Label>>();
    Label TestBubble(string text,bool mine){
        var l=new Label{Text=text,AutoSize=true,MaximumSize=new Size(138,0),Padding=new Padding(6),Margin=new Padding(mine?34:2,3,0,3),
            BackColor=mine?Color.FromArgb(237,220,225):Color.White};
        testLog.Controls.Add(l);while(testLog.Controls.Count>80)testLog.Controls[0].Dispose();
        testLog.ScrollControlIntoView(l);return l;
    }
    void SubmitTestComment(){
        string text=comment.Text.Trim();
        if(text.Length==0)return;
        if(Preferences.Muted(text)){comment.Clear();comment.Focus();Flag("弹幕试播","","","本机",null);return;}
        if(pendingTests.Count>=12){replyStatus.Text="等待回复的消息较多，请稍后发送";return;}
        RememberNatural("测试弹幕",text);TestBubble(text,true);var reply=TestBubble(playingSong?"歌曲结束后回答…":"等待回答…",false);
        comment.Clear();comment.Focus();pendingTests.Enqueue(Tuple.Create(text,reply));Drain();
    }
    void RetryTest(string text,Label reply){
        reply.Text="回答失败 · 点击重试";reply.Cursor=Cursors.Hand;
        EventHandler retry=null;retry=delegate{
            if(pendingTests.Count>=12)return;
            reply.Click-=retry;reply.Cursor=Cursors.Default;reply.Text="等待回答…";
            pendingTests.Enqueue(Tuple.Create(text,reply));Drain();
        };reply.Click+=retry;
    }
    void QueueComment(string text,string room,string viewer,string nick,string id){
        if(String.IsNullOrWhiteSpace(text))return;
        RememberNatural("弹幕",text);latestComment=Tuple.Create(text.Trim(),room,viewer,nick,id);Drain();
    }
    static int GiftUnit(string name){
        switch(name){
            case "小心心":case "人气票":case "抖音":case "玫瑰":return 1;
            case "大啤酒":return 2;
            case "棒棒糖":return 9;
            case "鲜花":return 10;
            case "加油鸭":return 15;
            case "爱你哟":return 52;
            case "亲吻":case "Thuglife":case "闪耀星辰":return 99;
            case "礼花筒":case "为你举牌":return 199;
            case "比心兔兔":return 299;
            case "热气球":return 520;
            case "跑车":return 1200;
            case "私人飞机":return 3000;
            default:return 0;
        }
    }    void QueueGift(string nick,string item,int count=1,long diamonds=0,string viewer=""){
        if(String.IsNullOrWhiteSpace(item)||count<1||Preferences.Muted(item)||Preferences.Muted(nick))return;
        GiftThanks entry=null;
        if(viewer.Length>0&&!viewer.EndsWith(":"))
            entry=thanks.FindLast(x=>x.Viewer==viewer&&x.Name==item&&naturalVersion-x.LastVersion<memoryLength);
        if(entry==null){
            entry=new GiftThanks{Viewer=viewer,Nick=nick,Name=item};thanks.Add(entry);
            TrackGift(entry);
        }
        entry.Count+=count;
        // Match exact catalog names only when the event omits its unit price.
        long unit=diamonds>0?diamonds:GiftUnit(item);
        entry.Value+=unit*count;
        giftItems+=count;
        RememberNatural("礼物",item+" × "+count);
        entry.LastVersion=naturalVersion;
        giftReceipt.Text="已收 · "+GiftLevel(entry)+" · "+item+" × "+entry.Count;
        RefreshGifts();Drain();
    }
    GiftThanks TakeGift(){
        GiftThanks best=null;
        foreach(var entry in thanks)if(entry.Tier==2&&(best==null||entry.Value>best.Value))best=entry;
        if(best==null&&reserveSmall)best=thanks.Find(x=>x.Tier==0);
        if(best==null)foreach(var entry in thanks)if(best==null||entry.Value>best.Value)best=entry;
        reserveSmall=false;thanks.Remove(best);return best;
    }
    async void Drain(){
        if(draining||playingSong||lifetime.IsCancellationRequested)return;
        draining=true;
        try{
            while(!playingSong&&!lifetime.IsCancellationRequested&&(thanks.Count>0||pendingTests.Count>0||latestComment!=null)){
                if(thanks.Count>0){await Task.Delay(350,lifetime.Token);if(playingSong)break;}
                var gift=thanks.Count>0?TakeGift():null;
                var test=pendingTests.Count>0?pendingTests.Peek():null;
                await PerformLiveTurn(gift,test,test==null?latestComment:null);
            }
        }catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)replyStatus.Text="请求超时";}
        catch(Exception ex){if(!lifetime.IsCancellationRequested)replyStatus.Text="本轮未播出："+ex.Message;}
        finally{draining=false;StartNextSong();}
    }
    void AddSelect(string name,ComboBox combo){
        panel.Controls.Add(new Label{Text=name,AutoSize=true,Margin=new Padding(3,9,0,3)});
        combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.Width=172;panel.Controls.Add(combo);
    }
    void AddButton(string name,Action action){
        var button=new Button{Text=name,Width=172,Height=35,ForeColor=Color.Black,UseVisualStyleBackColor=true};
        button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(213,206,200);button.BackColor=name=="连接实时数字人"?Color.FromArgb(129,58,74):Color.FromArgb(255,252,248);if(name=="连接实时数字人")button.ForeColor=Color.White;button.Click+=delegate{action();};panel.Controls.Add(button);
    }
    LiveView liveView;
    bool liveBusy,closingStudio;
    async void StartLive(){
        if(liveBusy||liveView!=null)return;
        if(roles.SelectedItem==null)return;
        liveBusy=true;sites.Enabled=false;roles.Enabled=false;string failure=null;
        try{

            string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","播出资源"));
            naturalEvents.Clear();naturalRecent.Clear();spokenTurns.Clear();UpdateBrainMemory();naturalVersion++;naturalChecked=naturalVersion;
            liveView=new LiveView(sites.SelectedIndex==1,root,msg=>{replyStatus.Text=msg;if(msg=="画面已到达"){Lamp(true);LookAtCamera();}else if(msg.StartsWith("失败")||msg.Contains("断开")||msg.Contains("已结束"))Lamp(false);});
            replyStatus.Text="准备实时画面…";
            await liveView.Start(stage,Path.Combine(RoleFolder((string)roles.SelectedItem),"头像.png"),lifetime.Token);
        }catch(Exception ex){failure=ex.Message;}
        if(failure!=null){await CloseLive();replyStatus.Text="连接失败："+failure;}
        liveBusy=false;if(liveView==null){sites.Enabled=true;roles.Enabled=true;}
    }
    async Task CloseLive(){
        Lamp(false);var session=liveView;if(session==null)return;
        bool ended=false;
        try{ended=await session.Stop();}
        catch(Exception ex){replyStatus.Text="结束状态待核实："+ex.Message;}
        if(ended){liveView=null;session.Dispose();sites.Enabled=true;roles.Enabled=true;}
        replyStatus.Text=ended?"云端会话已结束":"请核对 Vidu 云端结束状态";
    }
    async void StopLive(){
        if(liveBusy || liveView==null)return;
        liveBusy=true;try{await CloseLive();}finally{liveBusy=false;}
    }
    bool checking;
    async void CheckVidu(){
        if(checking)return;
        checking=true;
        bool international=sites.SelectedIndex==1;
        sites.Enabled=false;
        modelResult.Text="正在检查 Vidu…";
        try{modelResult.Text=await Vidu.Check(international,lifetime.Token);}
        catch(Exception ex){modelResult.Text=ex.Message;}
        finally{if(!IsDisposed)sites.Enabled=liveView==null;checking=false;}
    }
    void FinishSong(){
        mciSendString("close bencao_music",null,0,IntPtr.Zero);
        musicWatch.Stop();playingSong=false;song.Text="";
        if(lifetime.IsCancellationRequested)return;
        Drain();
    }
    void StopSong(){playlist.Clear();if(songStop!=null)songStop.Cancel();else FinishSong();}
    void StartNextSong(){
        if(playingSong||speaking||draining||lifetime.IsCancellationRequested)return;
        while(playlist.Count>0){
            string path=playlist.Dequeue();
            if(liveView!=null&&lamp.ForeColor==Color.LightGreen){StreamSong(path,liveView);return;}
            if(mciSendString("open \""+path+"\" alias bencao_music",null,0,IntPtr.Zero)!=0 ||
                mciSendString("play bencao_music",null,0,IntPtr.Zero)!=0){
                mciSendString("close bencao_music",null,0,IntPtr.Zero);song.Text="此文件无法播放";continue;
            }
            playingSong=true;nextBeat=DateTime.MinValue;Beat();musicWatch.Start();song.Text=Path.GetFileName(path);return;
        }
    }
    void PlayVocalDemo(){
        string path=Path.Combine(Application.StartupPath,"演示歌曲","20261103-013733.mp3");
        if(!File.Exists(path)){song.Text="缺少白芷演唱片段";return;}
        if(playlist.Count>=20){song.Text="最多可排队20首";return;}
        playlist.Enqueue(path);
        StartNextSong();
    }
    void PlaySong(){
        using(var dialog=new OpenFileDialog{Filter="歌曲|*.mp3;*.wav;*.wma",Multiselect=true}){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            if(dialog.FileNames.Length>20-playlist.Count){song.Text="最多可排队20首";return;}
            foreach(var path in dialog.FileNames)playlist.Enqueue(path);
            StartNextSong();
        }
    }
    void Check(){
        if(preview.Frame==null)throw new Exception("角色资源缺失");
        foreach(var size in new[]{new Size(960,600),new Size(1280,720),new Size(810,1440)}){
            Size=size; PerformLayout(); panel.PerformLayout();
            if(stage.Bounds.IntersectsWith(scroll.Bounds)||scroll.HorizontalScroll.Visible)throw new Exception("面板重叠");
            foreach(Control c in panel.Controls){if(!c.Visible)continue;scroll.ScrollControlIntoView(c);
                Rectangle visible=scroll.RectangleToScreen(scroll.ClientRectangle);
                Rectangle control=c.RectangleToScreen(c.ClientRectangle);
                if(!visible.Contains(control))throw new Exception("控件不可见："+c.Text+" "+control+" / "+visible);}
        }
        ShowPage(pages[0]);Bubble("在吗？",true);Bubble("我在。",false);
        if(!conversation.Visible||bubbles[0].Left<=bubbles[1].Left||input.Bottom>conversation.Height)throw new Exception("聊天布局错误");
        foreach(var chatSize in new[]{new Size(1200,750),new Size(810,1100)}){Size=chatSize;Arrange();if(!preview.Visible||preview.Frame==null||stage.Bounds.IntersectsWith(conversation.Bounds)||preview.Width<100||input.Bottom>conversation.Height)throw new Exception("角色与聊天布局错误");}
        playingSong=true;
        QueueGift("甲","小心心",1,1,"room:a");QueueGift("甲","小心心",2,1,"room:a");
        QueueGift("乙","小心心",5,0,"room:b");QueueGift("乙","小心心",7,0,"room:b");QueueGift("丙","高档礼物",1,100,"room:c");
        if(thanks.Count!=3||TakeGift().Tier!=2)throw new Exception("礼物价值排序失败");reserveSmall=true;
        if(TakeGift().Count!=3||TakeGift().Count!=12||giftItems!=16||giftEntries!=3||
            !SpeechRules.Gift("乙","长名字的礼物不会被缩写成两个字",1).Contains("长名字的礼物不会被缩写成两个字"))throw new Exception("礼物答谢检查失败");
        playingSong=false;
        for(int i=0;i<50;i++)thanks.Add(new GiftThanks{Nick="测试",Name="小心心",Count=1,Value=1});
        var large=new GiftThanks{Nick="测试",Name="大礼物",Count=1,Value=100};thanks.Add(large);reserveSmall=true;
        if(!MustYieldToLarge(null)||!MustYieldToLarge(thanks[0])||MustYieldToLarge(large)||TakeGift()!=large)
            throw new Exception("大礼物优先或准备阶段让位失败");
        thanks.Clear();reserveSmall=false;CheckLivePlans();
        ShowPage(pages[4]);
        if(!brainPage.Visible||nodes.Count!=moves.Count||nodes.Count==0)throw new Exception("大脑动作库未完整显示");
        RememberNatural("测试弹幕","刚才聊了很久");
        if(!memory.Text.Contains("刚才聊了很久"))throw new Exception("大脑记忆未刷新");
        var firstNode=(Button)brainMap.Controls[1];
        firstNode.PerformClick();
        if(moveNote.Text!="先连接实时数字人")throw new Exception("动作离线点击异常");
        PulseBrain(nodes[firstNode].Prompt);
        if(activeNode==null)throw new Exception("动作连线未点亮");
        ShowPage(pages[2]);
        if(Application.OpenForms.Count!=1||preview.Parent!=stage)throw new Exception("工作台画面宿主错误");
    }
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [STAThread] static void Main(string[] args){
        try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
        System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        using(var form=new Studio()){
            if(Array.IndexOf(args,"--check")>=0){try{form.Opacity=0;form.Show();form.Check();}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
            else {if(Array.IndexOf(args,"--brain")>=0)form.ShowPage(form.pages[4]);Application.Run(form);}
        }
    }
}
