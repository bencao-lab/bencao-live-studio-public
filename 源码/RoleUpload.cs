using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class RoleUpload {
    static string BindingPath(string image,bool intl,string key){
        using(var hash=SHA256.Create()){
            string fingerprint=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","");
            string photo=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(image))).Replace("-","");
            return Path.Combine(Preferences.ProfileDir,(intl?"国际":"国内")+"形象-"+fingerprint.Substring(0,16)+"-"+photo+".txt");
        }
    }
    public static string Bound(string image,bool intl){
        string path=BindingPath(image,intl,Vidu.SiteToken(intl));
        return File.Exists(path)?File.ReadAllText(path).Trim():null;
    }
    static async Task<Dictionary<string,object>> Request(HttpClient client,HttpMethod method,string url,object payload,string key,System.Threading.CancellationToken token){
        var json=new JavaScriptSerializer();
        using(var request=new HttpRequestMessage(method,url)){
            request.Headers.Authorization=new AuthenticationHeaderValue("Token",key);
            request.Content=new StringContent(json.Serialize(payload),Encoding.UTF8,"application/json");
            using(var response=await client.SendAsync(request,token)){
                if(!response.IsSuccessStatusCode)throw new IOException("Vidu "+new Uri(url).AbsolutePath+" 返回 HTTP "+(int)response.StatusCode);
                return json.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
            }
        }
    }
    public static async Task<string> Upload(string image,string name,bool intl,System.Threading.CancellationToken token){
        string key=Vidu.SiteToken(intl),path=BindingPath(image,intl,key);
        if(File.Exists(path))return File.ReadAllText(path).Trim();
        string host=intl?"https://api.vidu.com":"https://api.vidu.cn";
        using(var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(3)}){
            var slot=await Request(client,HttpMethod.Post,host+"/tools/v2/files/uploads",new {scene="vidu"},key,token);
            var target=new Uri((string)slot["put_url"]);if(target.Scheme!="https")throw new IOException("上传地址无效");
            string tag;
            using(var bytes=new ByteArrayContent(File.ReadAllBytes(image))){
                bytes.Headers.ContentType=new MediaTypeHeaderValue("image/png");
                using(var put=await client.PutAsync(target,bytes,token)){
                    put.EnsureSuccessStatusCode();if(put.Headers.ETag==null)throw new IOException("上传缺少文件回执");
                    tag=put.Headers.ETag.Tag.Trim('"');
                }
            }
            string id=Convert.ToString(slot["id"]);
            var done=await Request(client,HttpMethod.Put,host+"/tools/v2/files/uploads/"+Uri.EscapeDataString(id)+"/finish",new {etag=tag},key,token);
            object uri;var created=await Request(client,HttpMethod.Post,host+"/live/v1/avatars",new {image_uri=done.TryGetValue("uri",out uri)&&uri!=null?uri:"ssupload:?id="+id,name=name},key,token);
            object entry;if(created.TryGetValue("avatar",out entry)||created.TryGetValue("item",out entry))created=(Dictionary<string,object>)entry;
            object asset;if(!created.TryGetValue("id",out asset))created.TryGetValue("avatar_id",out asset);
            string value=Convert.ToString(asset);if(String.IsNullOrWhiteSpace(value))throw new IOException("Vidu 未返回形象编号");
            Preferences.Write(path,value);return value;
        }
    }
}
partial class Studio {
    readonly Label roleStatus=new Label{AutoSize=true,MaximumSize=new System.Drawing.Size(192,0)};
    bool uploadingRole;
    void ShowRoleStatus(){
        if(selectedRole==null)return;
        try{roleStatus.Text=selectedRole+(RoleUpload.Bound(Path.Combine(RoleFolder(selectedRole),"头像.png"),sites.SelectedIndex==1)==null?" · 尚未绑定当前站点形象":" · 已绑定 Vidu 形象");}
        catch(Exception e){roleStatus.Text=selectedRole+" · "+e.Message;}
    }
    async void UploadRole(){
        if(uploadingRole||selectedRole==null)return;
        if(liveBusy||liveView!=null){roleStatus.Text="请先结束实时会话，再上传角色";return;}
        string role=selectedRole;bool intl=sites.SelectedIndex==1;uploadingRole=true;
        roleStatus.Text="正在上传 "+role+" 到 Vidu "+(intl?"国际站":"国内站");
        try{await RoleUpload.Upload(Path.Combine(RoleFolder(role),"头像.png"),role,intl,lifetime.Token);
            if(selectedRole==role&&intl==(sites.SelectedIndex==1))ShowRoleStatus();}
        catch(Exception e){if(selectedRole==role&&intl==(sites.SelectedIndex==1))roleStatus.Text="上传失败："+e.Message;}
        finally{uploadingRole=false;}
    }
}
