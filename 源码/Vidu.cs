using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

static class Vidu {
    // Windows PowerShell ConvertFrom-SecureString stores a hex-encoded DPAPI blob.
    internal static string Token(string path){
        if(!File.Exists(path))throw new IOException("当前站点还没有配置密钥");
        string hex=File.ReadAllText(path).Trim();
        if(hex.Length==0 || hex.Length%2!=0 || hex.Length>65536)throw new FormatException("密钥文件格式无效");
        byte[] encrypted=new byte[hex.Length/2];
        for(int i=0;i<encrypted.Length;i++) encrypted[i]=Convert.ToByte(hex.Substring(i*2,2),16);
        byte[] plain=ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser);
        try{return Encoding.Unicode.GetString(plain).TrimEnd('\0');}
        finally{Array.Clear(plain,0,plain.Length);}
    }
    public static string SiteToken(bool international){
        string folder=AppPaths.DataRoot;
        string path=Path.Combine(folder,international?"Vidu-intl.dpapi":"Vidu-cn.dpapi");
        return Token(path);
    }
    public static async Task<string> Check(bool international,System.Threading.CancellationToken token=default(System.Threading.CancellationToken)){
        string host=international?"api.vidu.com":"api.vidu.cn";
        using(var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false})){
            client.Timeout=TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Token",SiteToken(international));
            using(var reply=await client.GetAsync("https://"+host+"/live/v1/action-libraries?pager.page=0&pager.pagesz=1",token)){
                if(reply.IsSuccessStatusCode)return "当前站点鉴权通过";
                if(reply.StatusCode==HttpStatusCode.Unauthorized)return "授权失败，请核对站点和密钥";
                return "查询失败，HTTP "+(int)reply.StatusCode;
            }
        }
    }
}
