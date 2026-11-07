using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

partial class Studio {
    static Move ThankMove(string name,string prompt){return new Move{Group="感谢",Name=name,Prompt=prompt};}
    static readonly Move[][] GiftMoves={
        new[]{ThankMove("点头致谢","看向镜头，轻轻点头微笑"),ThankMove("轻轻挥手","微笑着向镜头轻轻挥一下手")},
        new[]{ThankMove("开心比心","眼睛一亮，笑着双手比心"),ThankMove("捂嘴笑","轻轻捂嘴笑，再看向镜头点头")},
        new[]{ThankMove("超级惊喜","惊喜地睁大眼睛，双手比一个大大的心，再笑着看向镜头"),ThankMove("郑重感谢","惊喜地双手捧脸，随后轻轻鞠躬致谢")}
    };
    static string GiftLevel(GiftThanks g){return new[]{"小礼物","中礼物","大礼物"}[g.Tier];}
    readonly Dictionary<string,List<GiftThanks>> giftBook=new Dictionary<string,List<GiftThanks>>();
    readonly Dictionary<string,Button> thankNodes=new Dictionary<string,Button>();
    readonly Label giftTotals=new Label{Dock=DockStyle.Top,Height=24,ForeColor=Color.WhiteSmoke};
    readonly ListBox giftsSeen=new ListBox{Dock=DockStyle.Fill};
    long giftItems,giftSpoken,giftEntries;
    bool reserveSmall;
    int contentTurnsDue;
    bool GiftTurnDue(){
        return thanks.Exists(g=>g.Tier==2)||(thanks.Count>0&&(contentTurnsDue==0||(latestComment==null&&pendingTests.Count==0)));
    }
    GiftThanks currentGift;
    void TrackGift(GiftThanks gift){
        List<GiftThanks> list;
        if(!giftBook.TryGetValue(gift.Name,out list))giftBook[gift.Name]=list=new List<GiftThanks>();
        list.Add(gift);giftEntries++;
    }
    void RefreshGifts(){
        var selected=giftsSeen.SelectedIndex;
        giftsSeen.BeginUpdate();giftsSeen.Items.Clear();
        foreach(var pair in giftBook)giftsSeen.Items.Add(pair.Key+" × "+pair.Value.Sum(g=>g.Count));
        if(selected>=0&&selected<giftsSeen.Items.Count)giftsSeen.SelectedIndex=selected;
        giftsSeen.EndUpdate();
        giftTotals.Text="礼物 · 本次运行  "+giftItems+" 件 / "+giftEntries+" 笔 / 已播 "+giftSpoken+" 笔";
        UpdateBrainMemory();
    }
    void ShowGiftRecords(){
        if(giftsSeen.SelectedIndex<0)return;
        var list=giftBook.Values.ElementAt(giftsSeen.SelectedIndex);
        using(var f=new Form{Text=list[0].Name+" · 答谢记录",Size=new Size(450,340),StartPosition=FormStartPosition.CenterParent}){
            var rows=new ListBox{Dock=DockStyle.Fill};
            var retry=new Button{Text="重试所选答谢",Dock=DockStyle.Bottom,Enabled=false};
            foreach(var g in list)rows.Items.Add(SpeechRules.Address(g.Nick,false)+"  × "+g.Count+"  "+GiftLevel(g)+"  "+g.State);
            rows.SelectedIndexChanged+=delegate{retry.Enabled=rows.SelectedIndex>=0&&list[rows.SelectedIndex].State=="待重试";};
            retry.Click+=delegate{var g=list[rows.SelectedIndex];if(g.State!="待重试")return;g.State="待答谢";thanks.Add(g);f.Close();Drain();};
            f.Controls.Add(rows);f.Controls.Add(retry);f.ShowDialog(this);
        }
    }
    Control GiftPanel(){
        var area=new TableLayoutPanel{Dock=DockStyle.Top,Height=114,ColumnCount=2,RowCount=1,Padding=new Padding(8,0,8,3)};
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));
        var left=new Panel{Dock=DockStyle.Fill};
        left.Controls.Add(giftsSeen);left.Controls.Add(giftTotals);
        giftsSeen.DoubleClick+=delegate{ShowGiftRecords();};
        giftsSeen.KeyDown+=(o,e)=>{if(e.KeyCode==Keys.Enter)ShowGiftRecords();};
        var right=new Panel{Dock=DockStyle.Fill};
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true};
        right.Controls.Add(actions);right.Controls.Add(new Label{Text="感谢动作",ForeColor=Color.WhiteSmoke,Dock=DockStyle.Top,Height=24});
        foreach(var m in GiftMoves.SelectMany(group=>group)){
            var b=new Button{Text=m.Name,AutoSize=true,FlatStyle=FlatStyle.Flat,ForeColor=Color.WhiteSmoke,BackColor=Color.FromArgb(51,48,56)};
            b.Click+=delegate{moveNote.Text=SendVisual(m.Prompt)?"已提交："+m.Name:"动作未发送，请检查连接或动作限制";};
            actions.Controls.Add(b);thankNodes.Add(m.Prompt,b);
        }
        area.Controls.Add(left,0,0);area.Controls.Add(right,1,0);RefreshGifts();return area;
    }
}
