using System;
using System.IO;
using System.Diagnostics;

static class AppPaths {
public const bool PublicRelease=true;
    public static readonly string DataRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        PublicRelease?"本草收藏直播台外发版":"本草收藏直播软件");
    public static string ReceiverRoot{get{return PublicRelease?
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","接收服务")):Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","接收服务"));}}
    public static string Node{get{return PublicRelease?Path.Combine(ReceiverRoot,"node.exe"):"node.exe";}}
    public static int ReceiverPort{get{return PublicRelease?18769:18768;}}
    public static string ReceiverApp{get{return PublicRelease?"bencao-public-receiver-v1":"baizhi-live-mvp-v1";}}
    public static ProcessStartInfo ReceiverProcess(string file){
        var info=new ProcessStartInfo(Node,"\""+Path.Combine(ReceiverRoot,file)+"\""){
            WorkingDirectory=ReceiverRoot,UseShellExecute=false,CreateNoWindow=true
        };
        if(PublicRelease)info.EnvironmentVariables["BENCAO_DATA_ROOT"]=DataRoot;
        return info;
    }
}
