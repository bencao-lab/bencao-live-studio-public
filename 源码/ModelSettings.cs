using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

partial class Studio {
    static readonly string modelDir=AppPaths.DataRoot;
    static string ModelFile{get{return Path.Combine(modelDir,"对话模型.txt");}}
    internal static string ChatModel{get{return File.Exists(ModelFile)?File.ReadAllText(ModelFile).Trim():"deepseek-flash";}}
    readonly TextBox videoSecret=new TextBox{UseSystemPasswordChar=true};
    readonly Label billing=new Label{AutoSize=true,MaximumSize=new System.Drawing.Size(192,0)};
    readonly Dictionary<string,decimal> opening=new Dictionary<string,decimal>();
    int billingVersion,refreshRequest;
    void ClearBilling(){billingVersion++;opening.Clear();billing.Text="刷新后显示账户余额和变化。";}
    static void SaveSecret(string file,string key){
        if(String.IsNullOrWhiteSpace(key))return;
        var bytes=Encoding.Unicode.GetBytes(key.Trim());
        try{var secret=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);
            Preferences.Write(Path.Combine(modelDir,file+".dpapi"),BitConverter.ToString(secret).Replace("-",""));}
        finally{Array.Clear(bytes,0,bytes.Length);}
    }
    void BuildModelSettings(){
        AddInput("当前站点 API Key",videoSecret);videoSecret.MaxLength=2048;
        AddButton("保存视频密钥",delegate{try{if(videoSecret.Text.Trim().Length==0)return;SaveSecret(sites.SelectedIndex==1?"Vidu-intl":"Vidu-cn",videoSecret.Text);videoSecret.Clear();ClearBilling();modelResult.Text="已保存当前站点密钥";}catch(Exception e){modelResult.Text=e.Message;}});
        Section("大模型 · DeepSeek");
        var model=new TextBox{Text=ChatModel};var secret=new TextBox{UseSystemPasswordChar=true};
        AddInput("对话模型名称",model);AddInput("DeepSeek API Key",secret);secret.MaxLength=2048;
        var info=new Label{Width=192,AutoSize=true};panel.Controls.Add(info);
        AddButton("保存大模型设置",delegate{try{
            string value=model.Text.Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"^[A-Za-z0-9._/-]{1,128}$")){info.Text="请填写有效的模型名称";return;}
            SaveSecret("DeepSeek",secret.Text);Preferences.Write(ModelFile,value);secret.Clear();ClearBilling();info.Text="已保存，下次对话生效";
        }catch(Exception e){info.Text=e.Message;}});
        Section("费用与积分");AddButton("刷新账户余额",RefreshBilling);panel.Controls.Add(billing);
    }
    static readonly HttpClient balanceClient=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(10)};
    static async Task<Dictionary<string,decimal>> Balances(int service,bool intl,System.Threading.CancellationToken token){
        string url,key,header="Authorization";
        if(service==0){url="https://"+(intl?"api.vidu.com":"api.vidu.cn")+"/ent/v2/credits?show_detail=false";key="Token "+Vidu.SiteToken(intl);}
        else if(service==1){url="https://api.deepseek.com/user/balance";key="Bearer "+Interaction.Key("DeepSeek");}
        else{var b=Interaction.Binding();if(b==null)throw new IOException("当前角色未绑定声音");if(b.Length==4&&b[2].Length>0)throw new IOException("请到自定义服务查看账单");url="https://api.breeze.blue/v1/balance";header="xi-api-key";key=b[0];}
        using(var req=new HttpRequestMessage(HttpMethod.Get,url)){
            req.Headers.Add(header,key);
            using(var res=await balanceClient.SendAsync(req,token)){if(!res.IsSuccessStatusCode)throw new IOException("查询失败 HTTP "+(int)res.StatusCode);
                var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await res.Content.ReadAsStringAsync());
                var totals=new Dictionary<string,decimal>();Action<string,object> add=(unit,v)=>{decimal amount;if(!Decimal.TryParse(Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out amount))throw new IOException("余额格式无法识别");decimal prior;totals.TryGetValue(unit,out prior);totals[unit]=prior+amount;};
                if(service==2)add("账户余额",data["balance"]);
                else foreach(Dictionary<string,object> row in (IEnumerable)data[service==0?"remains":"balance_infos"]){
                    if(service==0)add("积分",row["credit_remain"]);else add(Convert.ToString(row["currency"]),row["total_balance"]);
                }
                return totals;
            }
        }
    }
    async void RefreshBilling(){
        int request=++refreshRequest,version=billingVersion;bool intl=sites.SelectedIndex==1;
        billing.Text="正在查询…";var lines=new List<string>();
        for(int i=0;i<3;i++){
            string title=i==0?"Vidu":i==1?"DeepSeek":"音频";
            try{var items=await Balances(i,intl,lifetime.Token);if(lifetime.IsCancellationRequested||version!=billingVersion||request!=refreshRequest||IsDisposed)return;
                if(items.Count==0)lines.Add(title+" · 暂无余额记录");
                foreach(var item in items){string id=title+item.Key;decimal start;if(!opening.TryGetValue(id,out start))opening[id]=start=item.Value;
                    lines.Add(title+" "+item.Key+" "+item.Value.ToString("0.####")+"（净减少 "+(start-item.Value).ToString("0.####")+"）");}
            }catch(Exception ex){lines.Add(title+" · "+(ex is IOException?ex.Message:"无法查询余额"));}
            if(lifetime.IsCancellationRequested||version!=billingVersion||request!=refreshRequest||IsDisposed)return;
        }
        lines.Add("净减少从首次刷新起算，含同账户其他消费及充值。实付费用以平台账单为准。");billing.Text=String.Join(Environment.NewLine,lines);
    }
}
