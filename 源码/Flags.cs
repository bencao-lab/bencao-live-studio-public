using System;
using System.IO;
using System.Web.Script.Serialization;
partial class Studio {
    internal const string SuppressedOutput="__OUTPUT_FILTERED__";
    static readonly string FlagDir=Path.Combine(AppPaths.DataRoot,"发言标记");
    void Flag(string source,string room,string viewer,string nickname,string id){
        try{
            var now=DateTimeOffset.Now;
            var entry=new {time=now.ToString("o"),source=source,room=room,viewerId=viewer,nickname=nickname,eventId=id,reason="禁言话题"};
            Directory.CreateDirectory(FlagDir);
            File.AppendAllText(Path.Combine(FlagDir,now.ToString("yyyy-MM")+".jsonl"),new JavaScriptSerializer().Serialize(entry)+Environment.NewLine);
        }catch(IOException){roomStatus.Text="本机标记记录写入失败";}catch(UnauthorizedAccessException){roomStatus.Text="本机标记记录无写入权限";}
    }
}