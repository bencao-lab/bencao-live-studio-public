using System;
using System.Linq;
using System.Text.RegularExpressions;
public static class SpeechRules {
    public static string Address(string nick,bool female){
        nick=Regex.Replace(nick??"",@"[^\u4e00-\u9fffA-Za-z0-9]","");
        female=female||Regex.IsMatch(nick,"姐姐|女士");
        var generic=Regex.Match(nick,@"^用户([0-9]{1,3})");
        string name=generic.Success?"用户"+String.Concat(generic.Groups[1].Value.Select(c=>"零一二三四五六七八九"[c-'0'])):Regex.Replace(nick,"小姐姐|姐姐|女士|哥哥|先生","");
        if(!generic.Success&&name.Length>3)name=name.Substring(0,3);
        return name+(female?"姐姐":"哥哥");
    }
    public static string Gift(string nick,string item,int variant){
        string who=Address(nick,false);
        string line=variant%2==0?"谢谢"+who+"的"+item:who+"，"+item+"收到啦";
        return line;
    }
    public static string ContentAnswer(string reply,int minimum){
        string line=(reply??"").Trim().Trim('“','”');
        if(line.Length<minimum||line.Length>120||Regex.IsMatch(line,"谢谢|感谢|送礼|礼物|打赏|```|[{}]"))return "";
        return line;
    }
    public static string ShortAnswer(string reply,string comment){
        string line=Regex.Replace(reply??"",@"(^|[，。！？、\s])[啊哈哇诶呀嗯哦]+(?:[！!。，、～\s]+|$)","$1").Trim();
        line=Regex.Split(line,"[。！？!?\r\n]")[0].Trim(' ','，','：','“','”');
        string source=Regex.Replace(comment??"",@"[\p{P}\s]","");
        string answer=Regex.Replace(line,@"[\p{P}\s]","");
        if(answer.Length==0||line.Length>Preferences.Number("limit")||answer==source||Regex.IsMatch(line,"^(你说|你问|有人说|观众说)")||(source.Length>=4&&answer.Contains(source)))return "";
        return line;
    }
}
