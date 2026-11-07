using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows.Forms;
using System.Web.Script.Serialization;

partial class Studio {
    public class Move { public string Group{get;set;} public string Name{get;set;} public string Prompt{get;set;} public override string ToString(){return Group+" / "+Name;} }
    readonly Label moveNote=new Label{AutoEllipsis=true};
    List<Move> moves=new List<Move>(); bool moveReadable;
    string MoveFile{get{return Path.Combine(Preferences.ProfileDir,"快捷动作.json");}}
    static List<Move> BaseMoves(){
        var list=new List<Move>();
        Action<string,string> add=(g,names)=>{foreach(var n in names.Split('|'))list.Add(new Move{Group=g,Name=n,Prompt=n=="鬼脸"?"面对镜头做个俏皮的鬼脸":n});};
        add("吃喝","喝水|吃西瓜|吃榴莲|吃棒棒糖|吃雪糕|吃苹果|吃汉堡|吃薯条|吃饺子|吃小龙虾|喝可乐|嗑瓜子|吃泡面|吃葡萄|喝奶茶");
        add("动作外表","双马尾|头发别到耳后|高马尾|扎丸子头|甩头发|戴上蝴蝶结发夹|编辫子|反戴棒球帽|绕头发|拿出墨镜戴上|涂口红");
        add("拟人动作","揉眼睛|挠头|托腮发呆|捂嘴偷笑|抚摸肩颈|对镜头竖大拇指|鬼脸|张开双臂求抱抱|凑近镜头，可爱表情|飞吻|戴上红色凤冠|拿出琵琶");
        add("常用道具","拿出哆啦A梦|拿出奥特曼玩偶|拿出皮卡丘玩偶|拿出一束花|拿出小蛋糕|拿出手机自拍|拿出一只猫|拿出一只小狗");return list;
    }
    void BuildMoves(){
        BuildBrain();RefreshMoves();
    }
    void RefreshMoves(){
        if(brainMap==null)return;
        try{moves=File.Exists(MoveFile)?new JavaScriptSerializer().Deserialize<List<Move>>(File.ReadAllText(MoveFile)):BaseMoves();
            ValidateMoves(moves);moveReadable=true;moveNote.Text="点动作即可发送";}
        catch{moves=new List<Move>();moveReadable=false;moveNote.Text="动作文件无法读取，已保留原文件";}
        DrawBrain();
    }
    static void ValidateMoves(List<Move> list){
        if(list==null||list.Count>500)throw new IOException("每个角色最多保存 500 个动作");
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var m in list)if(m==null||String.IsNullOrWhiteSpace(m.Group)||String.IsNullOrWhiteSpace(m.Name)||String.IsNullOrWhiteSpace(m.Prompt)||m.Group.Length>40||m.Name.Length>60||m.Prompt.Length>2000||!keys.Add(m.Group+"\n"+m.Name))throw new IOException("分组、名称和提示词均需填写，同组名称不可重复；提示词最多 2000 字。");
    }
    void EditMoves(){
        if(!moveReadable)return;
        using(var f=new Form{Text="当前角色的快捷动作",ClientSize=new System.Drawing.Size(420,360),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false}){
            var select=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};var group=new ComboBox{DropDownStyle=ComboBoxStyle.DropDown,MaxLength=40};var name=new TextBox();var prompt=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical};var note=new Label();
            Action<Control,int,int,int,int> place=(c,x,y,w,h)=>{c.SetBounds(x,y,w,h);f.Controls.Add(c);};
            place(select,12,10,396,26);place(new Label{Text="分组"},12,48,45,22);place(group,60,46,125,24);place(new Label{Text="名称"},200,48,45,22);place(name,246,46,162,24);place(new Label{Text="动作提示词"},12,84,100,22);place(prompt,12,108,396,134);place(note,12,295,396,55);
            Action<Move> fill=m=>{group.Items.Clear();foreach(string g in moves.Select(x=>x.Group).Distinct())group.Items.Add(g);select.Items.Clear();select.Items.Add("＋ 新动作");foreach(var x in moves)select.Items.Add(x);select.SelectedIndex=m==null?0:select.Items.IndexOf(m);};
            select.SelectedIndexChanged+=delegate{var m=select.SelectedItem as Move;group.Text=m==null?"自定义":m.Group;name.Text=m==null?"":m.Name;prompt.Text=m==null?"":m.Prompt;};fill(null);
            Action<int,string,Action> button=(x,t,fn)=>{var b=new Button{Text=t};place(b,x,252,126,34);b.Click+=delegate{try{fn();}catch(Exception e){note.Text=e.Message;}};};
            Action<List<Move>,Move> persist=(next,m)=>{ValidateMoves(next);Preferences.Write(MoveFile,new JavaScriptSerializer().Serialize(next));moves=next;DrawBrain();fill(m);note.Text="已保存到当前角色";};
            button(12,"保存动作",delegate{var m=new Move{Group=group.Text.Trim(),Name=name.Text.Trim(),Prompt=prompt.Text.Trim()};var next=new List<Move>(moves);var i=next.IndexOf(select.SelectedItem as Move);if(i<0)next.Add(m);else next[i]=m;persist(next,m);});
            button(147,"导入 TXT",delegate{using(var d=new OpenFileDialog{Filter="提示词文本 (*.txt)|*.txt"}){if(d.ShowDialog(f)!=DialogResult.OK)return;if(new FileInfo(d.FileName).Length>16000)throw new IOException("文本过长");string body=File.ReadAllText(d.FileName);select.SelectedIndex=0;name.Text=Path.GetFileNameWithoutExtension(d.FileName);prompt.Text=body;note.Text="可编辑，点保存动作后生效";}});
            button(282,"删除所选",delegate{var next=new List<Move>(moves);if(next.Remove(select.SelectedItem as Move))persist(next,null);});f.ShowDialog(this);
        }
    }
}
