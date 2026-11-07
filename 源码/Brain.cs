using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

partial class Studio {
    readonly Panel brainPage=new Panel{Visible=false,BackColor=Color.FromArgb(31,32,38)};
    BrainMap brainMap;
    readonly Label memory=new Label{ForeColor=Color.WhiteSmoke,TextAlign=ContentAlignment.MiddleCenter,AutoEllipsis=true};
    readonly Dictionary<Button,Move> nodes=new Dictionary<Button,Move>();
    readonly Timer brainPulse=new Timer{Interval=32};
    readonly List<Button> highlights=new List<Button>();
    static readonly Color[] green={Color.FromArgb(13,127,65),Color.FromArgb(34,99,62),Color.FromArgb(51,81,63),Color.FromArgb(57,69,61)};
    Button activeNode;
    DateTime pulseStart;
    sealed class BrainMap:Panel {public BrainMap(){DoubleBuffered=true;}}

    int memoryLength=12,actionInterval,interruptSensitivity=2;
    void AddBrainSlider(TableLayoutPanel footer,int column,string name,int min,int max,int current,Func<int,string> format,Action<int> apply){
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        var title=new Label{Text=name+"  "+format(current),ForeColor=Color.WhiteSmoke,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
        var slider=new TrackBar{AccessibleName=name,Minimum=min,Maximum=max,Value=current,Dock=DockStyle.Fill,TickStyle=TickStyle.None,BackColor=brainPage.BackColor};
        footer.Controls.Add(title,column,0);footer.Controls.Add(slider,column,1);
        slider.ValueChanged+=delegate{title.Text=name+"  "+format(slider.Value);apply(slider.Value);};
    }
    void BuildBrain(){
        Controls.Add(brainPage);
        var header=new Panel{Height=52,Dock=DockStyle.Top};
        var title=new Label{Text="记忆与大脑",AutoSize=true,Location=new Point(16,14),ForeColor=Color.WhiteSmoke};
        var edit=new Button{Text="编辑动作库",FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,BackColor=Color.FromArgb(68,60,73),ForeColor=Color.White,Dock=DockStyle.Right,Width=116};
        edit.Click+=delegate{EditMoves();};
        moveNote.ForeColor=Color.Silver;moveNote.SetBounds(124,16,240,25);
        header.Controls.AddRange(new Control[]{title,edit,moveNote});
        var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
                var footer=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=70,Padding=new Padding(12,2,12,2),ColumnCount=3,RowCount=2};
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute,27));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        AddBrainSlider(footer,0,"记忆长度",1,24,memoryLength,n=>n+" 条",n=>{
            memoryLength=n;while(naturalEvents.Count>n)naturalEvents.Dequeue();naturalVersion++;UpdateBrainMemory();
        });
        actionInterval=Math.Min(60,Preferences.Number("idle"));
        AddBrainSlider(footer,1,"动作间隔",3,60,actionInterval,n=>n+" 秒",n=>actionInterval=n);
        AddBrainSlider(footer,2,"打断灵敏度",0,2,interruptSensitivity,n=>new[]{"低","中","高"}[n],n=>interruptSensitivity=n);
        brainPage.Controls.Add(viewport);brainPage.Controls.Add(GiftPanel());brainPage.Controls.Add(header);brainPage.Controls.Add(footer);
        brainMap=new BrainMap();viewport.Controls.Add(brainMap);
        viewport.SizeChanged+=delegate{ArrangeNodes();};
        brainMap.Paint+=PaintConnections;brainMap.Controls.Add(memory);
        brainPulse.Tick+=delegate{
                        double elapsed=(DateTime.UtcNow-pulseStart).TotalMilliseconds;
            if(activeNode!=null&&thankNodes.ContainsValue(activeNode)){
                float t=(float)Math.Min(1,elapsed/650);
                activeNode.BackColor=Color.FromArgb((int)(95-82*t),(int)(210-83*t),(int)(119-54*t));
            }
            if(elapsed>=650){activeNode=null;brainPulse.Stop();}
            brainMap.Invalidate();
        };
        FormClosed+=delegate{brainPulse.Dispose();};
        UpdateBrainMemory();
    }
    void UpdateBrainMemory(){
        var latest=naturalEvents.Reverse().Take(4).Reverse();
        memory.Text="短期记忆"+Environment.NewLine+Environment.NewLine+
            (currentGift==null?"":GiftLevel(currentGift)+" · "+currentGift.Name+" × "+currentGift.Count+Environment.NewLine)+(naturalEvents.Count==0?"等待弹幕":String.Join(Environment.NewLine+Environment.NewLine,latest));
    }
    void DrawBrain(){
        highlights.Clear();
        activeNode=null;brainPulse.Stop();
        foreach(var b in nodes.Keys)b.Dispose();
        nodes.Clear();
        foreach(var m in moves){
            var node=new Button{Text=m.Name,AccessibleDescription=m.Group,AutoEllipsis=true,FlatStyle=FlatStyle.Flat,
                BackColor=Color.FromArgb(51,48,56),ForeColor=Color.WhiteSmoke};
            node.FlatAppearance.BorderColor=Color.FromArgb(103,91,107);
            node.Click+=delegate{
                if(liveView==null||lamp.ForeColor!=Color.LightGreen){moveNote.Text="先连接实时数字人";return;}
                moveNote.Text=SendVisual(m.Prompt)?"已提交："+m.Name:"动作受当前规则限制";
            };
            nodes.Add(node,m);brainMap.Controls.Add(node);
        }
        ArrangeNodes();
    }
    void ArrangeNodes(){
        var viewport=brainMap.Parent;
        int w=Math.Max(420,viewport.ClientSize.Width-SystemInformation.VerticalScrollBarWidth);
        int columns=w>=800?2:1;
        int rows=(nodes.Count+2*columns-1)/(2*columns);
        int h=Math.Max(viewport.ClientSize.Height,rows*39+38);
        brainMap.Size=new Size(w,h);
        int centerWidth=Math.Min(220,w/3), bank=(w-centerWidth)/2;
        int cell=(bank-24)/columns;
        int i=0;
        foreach(var b in brainMap.Controls.OfType<Button>()){
            int side=i%2,col=(i/2)%columns,row=i/(2*columns);
            int x=side==0?10+cell*col:w-10-cell*(col+1);
            b.SetBounds(x,16+row*39,cell-5,33);i++;
        }
        memory.SetBounds(bank+12,h/2-116,centerWidth-24,232);
        brainMap.Invalidate();
    }
    void PulseBrain(string prompt){
        if(brainMap==null)return;
        var match=nodes.FirstOrDefault(n=>n.Value.Prompt==prompt);
        Button node=match.Key;
        if(node==null&&!thankNodes.TryGetValue(prompt,out node))return;
        highlights.Remove(node);
        highlights.Insert(0,node);
        if(highlights.Count>green.Length){
            highlights[green.Length].BackColor=Color.FromArgb(51,48,56);
            highlights.RemoveAt(green.Length);
        }
        for(int rank=0;rank<highlights.Count;rank++)highlights[rank].BackColor=green[rank];
        activeNode=node;pulseStart=DateTime.UtcNow;brainPulse.Start();brainMap.Invalidate();
    }
    void PaintConnections(object sender,PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        int cx=brainMap.Width/2,cy=brainMap.Height/2;
        using(var line=new Pen(Color.FromArgb(75,85,91)))
        using(var lit=new Pen(Color.FromArgb(168,222,203),2)){
            foreach(var node in nodes.Keys){
                int edge=node.Left<cx?node.Right:node.Left,y=node.Top+node.Height/2;
                int start=edge<cx?memory.Left:memory.Right;
                g.DrawLine(node==activeNode?lit:line,start,cy,edge,y);
                if(node==activeNode){
                    float t=(float)Math.Min(1,(DateTime.UtcNow-pulseStart).TotalMilliseconds/650);
                    g.FillEllipse(Brushes.Honeydew,start+(edge-start)*t-3,cy+(y-cy)*t-3,7,7);
                }
            }
        }
    }
}
