using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
static class Preferences {
    static readonly string path=Path.Combine(AppPaths.DataRoot,"直播表现.json");
    public static readonly Dictionary<string,string> Defaults=new Dictionary<string,string>{
        {"party","随礼花绽放舒展双臂，笑着向观众致意\n轻快摆肩两拍，向镜头优雅鞠躬\n明亮地微笑，转向镜头张开手掌送上祝福"},
        {"light","左右轻摆肩，微笑点头\n向镜头靠近一点，抬手轻挥\n跟着一拍轻晃身体，侧头微笑"},
        {"simple","轻轻点头致谢\n侧头微笑致意\n抬眼看向镜头，轻挥一下手\n轻轻扬眉，微笑点头"},
        {"song","随音乐节奏轻点肩膀，温柔地唱\n自然转头看回镜头，随拍轻晃身体\n微笑着向镜头靠近一点，带着热情唱歌"},
        {"gaze","下巴自然抬平，双眼看向镜头里的观众，放松眨眼，偶尔略微睁大眼睛露出回应的表情。"},
        {"chat","你是白芷，与面前的人自然聊天，结合前文回答，不复述、不喊叫。"},
        {"live","你是白芷，正在直播。理解弹幕后自然回答，只答一个重点，不朗读或复述弹幕，不用独立感叹词，不喊叫。"},
        {"visual","描述观众希望看到的画面，包括动作、发色、造型、道具和场景。理解否定和上下文，普通聊天不触发画面指令。"},
        {"speech","保持自然平稳，像与旁边的人聊天，不喊叫，不突然重读。"},
        {"limit","18"},
        {"beat","7"},
        {"idle","25"},
        {"flip","开"},
        {"avoid",""},
        {"mute",String.Join("\n","军事 军队 武器 军演 国防 战争 导弹 航母 战机 坦克 核武 军事基地 驻军 空袭 征兵 政治 时政 政府 政党 选举 总统 总理 国家主席 领导人 国会 议会 外交 公共政策 国务院 党派 两会 医疗 医学 医生 医院 诊疗 诊断 治疗 疾病 症状 处方 药物 中药 用药 吃药 药效 药量 手术 疫苗 血压 血糖 糖尿病 癌症 感冒 发烧 头疼 咳嗽 失眠 看病 治病 保健 养生 食疗 药膳 滋补 补气 补肾 保健品 提高免疫力 偏方 排毒 功效 暴恐 赌博 色情 贩毒 诈骗 开盒 人肉搜索".Split(' '))}
    };
    public static string LoadError="";
    public sealed class Entry {
        public string Id{get;set;} public string Name{get;set;} public string Target{get;set;} public string Text{get;set;}
        public override string ToString(){return Name;}
    }
    public static readonly string[] Keys={"chat","live","speech","visual","gaze","simple","light","party","song","limit","idle","beat","flip","avoid","mute"};
    public static readonly string[] Names={"本地对话语气","直播弹幕语气","语音表现","画面指令规则","镜头视线","普通礼物动作","荧光棒动作","礼花筒动作","唱歌动作","单句字数上限","空闲视线间隔（秒）","唱歌动作间隔（秒）","禁止后空翻（开／关）","其他禁演词","禁言词"};
    static readonly string legacyCatalog=Path.Combine(Path.GetDirectoryName(path),"表现项目列表.json");
    public static string RoleId="白芷", RoleName="白芷";
    public static string ProfileDir{get{return Path.Combine(Path.GetDirectoryName(path),"角色设置",RoleId);}}
    static string catalogPath{get{return Path.Combine(ProfileDir,"表现项目列表.json");}}
    static readonly string mutePath=Path.Combine(Path.GetDirectoryName(path),"全局禁言词.txt");
    static List<Entry> entries=new List<Entry>();
    static string globalMute=Defaults["mute"];
    public static string Template(string key){return Defaults[key].Replace("你是白芷","你是"+RoleName);}
    public static void SelectRole(string id,string name){
        RoleId=id;RoleName=name;LoadError="";
        var legacy=LoadEntries(legacyCatalog,true);
        var imported=legacy.FirstOrDefault(e=>e.Target=="mute");
        globalMute=File.Exists(mutePath)?File.ReadAllText(mutePath):(imported==null?Defaults["mute"]:imported.Text);
        var list=File.Exists(catalogPath)?LoadEntries(catalogPath,false):
            id=="白芷"?legacy:LoadEntries(null,false);
        entries=list.Where(e=>e.Target!="mute").ToList();
    }
    public static void SaveGlobal(string value){
        Validate("mute",value);Write(mutePath,value);globalMute=value;
    }
    public static Entry[] Entries{get{return entries.ToArray();}}
    public static bool Multi(string key){return Array.IndexOf(Keys,key)>=0&&Array.IndexOf(Keys,key)<9;}
    static List<Entry> LoadEntries(string source,bool legacy){
        var baseList=Keys.Select((k,i)=>new Entry{Id=k,Target=k,Name=Names[i],Text=Template(k)}).ToList();
        try{
            var json=new JavaScriptSerializer();
            if(File.Exists(source)){
                var list=json.Deserialize<List<Entry>>(File.ReadAllText(source));
                if(list==null||list.Any(e=>e==null||!Defaults.ContainsKey(e.Target)||String.IsNullOrWhiteSpace(e.Id)||String.IsNullOrWhiteSpace(e.Name))||list.Select(e=>e.Id).Distinct().Count()!=list.Count)throw new IOException("列表格式错误");
                foreach(var e in list)Validate(e.Target,e.Text);
                return list;
            }
            if(legacy&&File.Exists(path)){
                var old=json.Deserialize<Dictionary<string,string>>(File.ReadAllText(path));
                foreach(var e in baseList)if(old.ContainsKey(e.Target)){Validate(e.Target,old[e.Target]);e.Text=old[e.Target];}
            }
        }catch(Exception ex){LoadError="设置读取失败："+ex.Message;}
        return baseList;
    }
    static void Commit(List<Entry> next){
        if(LoadError.Length>0)throw new IOException(LoadError);
        Write(catalogPath,new JavaScriptSerializer().Serialize(next));entries=next;
    }
    internal static void Write(string file,string text){
        Directory.CreateDirectory(Path.GetDirectoryName(file));string tmp=file+"."+Guid.NewGuid()+".tmp";
        try{File.WriteAllText(tmp,text);if(File.Exists(file))File.Replace(tmp,file,null);else File.Move(tmp,file);}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public static Entry Put(Entry previous,string name,string target,string content){
        if(target=="mute")throw new ArgumentException("禁言词由直播间统一设置");
        name=name.Trim();content=content.Trim();Validate(target,content);
        if(name.Length==0||name.Length>48)throw new ArgumentException("请输入 1 至 48 字的名称");
        if(entries.Any(e=>e!=previous&&String.Equals(e.Name,name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("已有同名项目");
        if(!Multi(target)&&entries.Any(e=>e!=previous&&e.Target==target))throw new ArgumentException("该设置已有项目，请直接编辑");
        var next=new List<Entry>(entries);var entry=new Entry{Id=previous==null?Guid.NewGuid().ToString("N"):previous.Id,Name=name,Target=target,Text=content};
        if(previous==null)next.Add(entry);else next[next.IndexOf(previous)]=entry;
        Commit(next);return entry;
    }
    public static void Remove(Entry e){var next=new List<Entry>(entries);next.Remove(e);Commit(next);}
    public const string Refusal="这个话题我不聊，换一个吧。";
    static string Fold(string s){return System.Text.RegularExpressions.Regex.Replace((s??"").Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant(),@"[\s\p{P}\p{S}\p{Cf}]+","");}
    public static bool Muted(string text){
        string folded=Fold(text);
        foreach(string term in Lines("mute")){string key=Fold(term);if(key.Length>0&&folded.Contains(key))return true;}
        return false;
    }
    public static string Get(string k){if(k=="mute")return globalMute;var items=entries.Where(e=>e.Target==k).Select(e=>e.Text).ToArray();return items.Length>0?String.Join("\n",items):(Multi(k)?"":Defaults[k]);}
    public static int Number(string k){return int.Parse(Get(k));}
    public static string[] Lines(string k){return Get(k).Split(new[]{'\n','\r'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToArray();}
    public static string Restrictions(){return (Get("flip")=="开"?"禁止后空翻及同义动作。":"")+(Get("avoid").Length>0?"跳过以下表演："+Get("avoid"):"");}
    static void Validate(string key,string value){
        if(value==null||value.Length>4000||(key!="avoid"&&key!="mute"&&String.IsNullOrWhiteSpace(value)))throw new ArgumentException("设置不能为空或超过 4000 字");
        if(key=="flip"&&value!="开"&&value!="关")throw new ArgumentException("请输入开或关");
        if(key=="beat"||key=="idle"||key=="limit"){
            int n,min=key=="limit"?1:3,max=key=="limit"?120:300;
            if(!int.TryParse(value,out n)||n<min||n>max)throw new ArgumentException("请输入 "+min+" 至 "+max+" 的整数");
        }
    }
}

partial class Studio {
    Action reloadRoleEditor;
    void PreferenceControls(){
        Section("直播表现设置");
        var choice=new ComboBox();var target=new ComboBox();var name=new TextBox{Width=190,MaxLength=48};
        var editor=new TextBox{Multiline=true,Width=190,Height=115,MaxLength=4000,ScrollBars=ScrollBars.Vertical};
        var status=new Label{AutoSize=true,MaximumSize=new Size(192,0)};
        AddSelect("项目列表",choice);
        panel.Controls.Add(new Label{Text="项目名称",AutoSize=true});panel.Controls.Add(name);
        AddSelect("生效位置",target);target.Items.AddRange(Preferences.Names.Take(Preferences.Names.Length-1).ToArray());
        panel.Controls.Add(new Label{Text="内容",AutoSize=true});panel.Controls.Add(editor);
        Action display=delegate{
            var e=choice.SelectedItem as Preferences.Entry;
            name.Text=e==null?"":e.Name;target.SelectedIndex=e==null?0:Array.IndexOf(Preferences.Keys,e.Target);
            editor.Text=e==null?"":e.Text.Replace("\r","").Replace("\n",Environment.NewLine);
            status.Text="";
        };
        Action<Preferences.Entry> refresh=delegate(Preferences.Entry selected){
            choice.Items.Clear();choice.Items.AddRange(Preferences.Entries);choice.SelectedItem=selected;display();
        };
        choice.SelectedIndexChanged+=delegate{display();};
        refresh(Preferences.Entries.FirstOrDefault());
        status.Text=Preferences.LoadError;
        AddButton("新增项目",delegate{choice.SelectedIndex=-1;display();name.Focus();});
        AddButton("保存到当前角色",delegate{try{
            string key=Preferences.Keys[target.SelectedIndex];
            var e=Preferences.Put(choice.SelectedItem as Preferences.Entry,name.Text,key,editor.Text);
            refresh(e);status.Text="已保存，下次触发时生效";
        }catch(Exception ex){status.Text=ex.Message;}});
        var save=(Button)panel.Controls[panel.Controls.Count-1];
        reloadRoleEditor=delegate{refresh(Preferences.Entries.FirstOrDefault());save.Text="保存到"+Preferences.RoleName;status.Text=Preferences.LoadError;};
        AddButton("删除项目",delegate{try{
            var e=choice.SelectedItem as Preferences.Entry;if(e==null)return;
            Preferences.Remove(e);refresh(Preferences.Entries.FirstOrDefault());
            status.Text=Preferences.Multi(e.Target)?"已删除该条提示词":"已删除项目，该设置使用默认值";
        }catch(Exception ex){status.Text=ex.Message;}});
        AddButton("载入该位置默认内容",delegate{editor.Text=Preferences.Template(Preferences.Keys[target.SelectedIndex]).Replace("\n",Environment.NewLine);status.Text="点保存项目后生效";});
        panel.Controls.Add(status);
    }
}
