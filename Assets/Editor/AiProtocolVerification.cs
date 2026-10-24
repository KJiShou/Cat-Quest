using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class AiProtocolVerification
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void Reject(Action action,string message) { bool rejected=false; try { action(); } catch(ArgumentException) { rejected=true; } Check(rejected,message); }
    public static void Run()
    {
        Task.Run(Connection).GetAwaiter().GetResult();
        string catalog="{\"metadata\":\""+new string('x',600000)+"\"}";
        Check(ChatGptJson.Text(ChatGptJson.Object(catalog),"metadata").Length==600000,"Large catalog rejected.");
        Reject(()=>ChatGptJson.Object(new string(' ',ChatGptJson.MaximumDocumentCharacters+1)),"Unbounded JSON accepted.");
        Reject(()=>ChatGptJson.Object("{\"aud\":\"a\",\"aud\":\"b\"}"),"Duplicate security key accepted.");
        Check(ChatGptBrowserAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")=="E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM","PKCE failed.");
        Check(ChatGptBrowserAuth.ParseCallback("/auth/callback?state=s&code=c&client_id=oaiapp_test","s",null)["code"]=="c","Callback failed.");
        Reject(()=>ChatGptBrowserAuth.ParseCallback("/auth/callback?state=wrong&code=c&client_id=oaiapp_test","s",null),"Wrong state accepted.");
        Reject(()=>ChatGptBrowserAuth.ParseCallback("/auth/callback?state=s&state=s&code=c&client_id=oaiapp_test","s",null),"Duplicate state accepted.");
        var stream=new ChatGptEventStream();
        string wire="data: {\"type\":\"response.output_text.delta\",\"delta\":\"你好\"}\r\n\r\ndata: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\r\n\r\n";
        foreach(char c in wire) stream.Push(c.ToString()); Check(stream.Completed && stream.Text=="你好","Chunked Unicode SSE failed.");
        foreach(string type in new[]{"response.failed","response.incomplete","error"}) Reject(()=>new ChatGptEventStream().Push("data: {\"type\":\""+type+"\"}\n\n"),"Failed SSE accepted.");
        using var rsa=RSA.Create(); rsa.KeySize=2048; var p=rsa.ExportParameters(false);
        var jwks=new Dictionary<string,object>{["keys"]=new List<object>{new Dictionary<string,object>{["kid"]="test",["kty"]="RSA",["n"]=ChatGptBrowserAuth.Base64(p.Modulus),["e"]=ChatGptBrowserAuth.Base64(p.Exponent)}}};
        string header=ChatGptBrowserAuth.Base64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"test\"}"));
        string payload=ChatGptBrowserAuth.Base64(Encoding.UTF8.GetBytes("{\"iss\":\"https://auth.openai.com\",\"aud\":\"oaiapp_test\",\"sub\":\"test\",\"nonce\":\"n\",\"exp\":2000}"));
        string unsigned=header+"."+payload, jwt=unsigned+"."+ChatGptBrowserAuth.Base64(rsa.SignData(Encoding.ASCII.GetBytes(unsigned),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
        ChatGptConnection.ValidateIdentity(jwt,jwks,"oaiapp_test","n",1000);
        Reject(()=>ChatGptConnection.ValidateIdentity(jwt,jwks,"wrong","n",1000),"Wrong audience accepted.");
        Reject(()=>ChatGptConnection.ValidateIdentity(jwt,jwks,"oaiapp_test","wrong",1000),"Wrong nonce accepted.");
        Reject(()=>ChatGptConnection.ValidateIdentity(jwt,jwks,"oaiapp_test","n",2000),"Expired identity accepted.");
        string folder="Library/AiVerification/Protocol-"+Guid.NewGuid().ToString("N");
        var disk=new ChatGptStorage(folder); disk.Write("connection.dpapi","synthetic",true);
        Check(disk.Read("connection.dpapi",true)=="synthetic"&&!Encoding.UTF8.GetString(File.ReadAllBytes(folder+"/connection.dpapi")).Contains("synthetic"),"DPAPI failed.");
        disk.Write("connection.dpapi","{}",true); Check(disk.Read("connection.dpapi",true)=="{}","Atomic replace failed.");
        Task.Run(Transport).GetAwaiter().GetResult();
    }
    sealed class Memory : IChatGptStorage {
        public readonly Dictionary<string,string> records=new Dictionary<string,string>();
        public string Read(string name,bool encrypted) { lock(records) return records.TryGetValue(name,out var value)?value:null; }
        public void Write(string name,string value,bool encrypted) { lock(records) records[name]=value; }
    }
    sealed class Http : IChatGptHttp {
        public int refreshes,calls;
        public Task<string> Send(string url,string method,string body,string type,string bearer,bool stream,CancellationToken token) {
            Interlocked.Increment(ref calls);
            if(url.EndsWith("/oauth/token")) { Interlocked.Increment(ref refreshes); return Task.FromResult("{\"access_token\":\"renewed\",\"refresh_token\":\"rotated\",\"expires_in\":3600}"); }
            if(url.EndsWith("/models"))return Task.FromResult("{\"models\":[{\"visibility\":\"hidden\",\"slug\":\"hidden\"},{\"visibility\":\"list\",\"slug\":\"first\"},{\"visibility\":\"list\",\"slug\":\"second\"}]}");
            if(url.EndsWith("/responses")) { var json=ChatGptJson.Object(body); Check((bool)json["stream"]&&!(bool)json["store"],"Inference flags changed."); return Task.FromResult("{\"message\":\"Hello\",\"drafts\":[]}"); }
            if(url.EndsWith("/oauth/revoke"))return Task.FromResult("");
            throw new Exception("Unexpected request.");
        }
    }
    static async Task Connection()
    {
        var storage=new Memory(); var http=new Http();
        storage.records["connection.dpapi"]="{\"client_id\":\"oaiapp_test\",\"refresh_token\":\"old\",\"access_token\":\"expired\",\"scope\":\"chatgpt.tokens.use.direct\",\"expires_at\":0}";
        using var connection=new ChatGptConnection(storage,http,()=>1000); await connection.Initialize(); Check(http.calls==0,"Initialization sent task data.");
        await Task.WhenAll(connection.LoadModels(CancellationToken.None),connection.LoadModels(CancellationToken.None));
        Check(http.refreshes==1&&connection.Model=="first"&&connection.Models.Count==2,"Refresh serialization or model order changed.");
        Check(storage.records["connection.dpapi"].Contains("rotated"),"Rotated token not persisted.");
        AiChatProtocol.Parse(await connection.Chat(new object[]{new Dictionary<string,object>{["role"]="user",["content"]="Hello"}},CancellationToken.None));
        await connection.Disconnect(CancellationToken.None);
        Check(!connection.Connected&&connection.ClientId=="oaiapp_test"&&!storage.records["connection.dpapi"].Contains("rotated"),"Disconnect did not clear tokens/preserve registration.");
    }
    static async Task Transport()
    {
        using var http=new ChatGptHttp();
        foreach(int code in new[]{401,403,429,503}) { bool failed=false; try { await Reply(http,code,"{}"); } catch(ArgumentException) { failed=true; } Check(failed,"HTTP error accepted."); }
        bool truncated=false; try { await Reply(http,200,"data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n",true); } catch(ArgumentException) { truncated=true; } Check(truncated,"Truncated stream accepted.");
        using var cancel=new CancellationTokenSource(100); bool cancelled=false;
        try { await Reply(http,200,"",true,true,cancel.Token); } catch(OperationCanceledException) { cancelled=true; } Check(cancelled,"Cancellation failed.");
        bool timeout=false; var elapsed=System.Diagnostics.Stopwatch.StartNew();
        try { await Reply(http,200,"",true,true); } catch(ArgumentException error) { timeout=error.Message.Contains("timed out"); }
        Check(timeout&&elapsed.Elapsed.TotalSeconds>=19&&elapsed.Elapsed.TotalSeconds<26,"Deadline failed.");
    }
    static async Task<string> Reply(ChatGptHttp http,int code,string body,bool stream=false,bool stall=false,CancellationToken cancellation=default)
    {
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); listener.Start(); using var stop=new CancellationTokenSource();
        int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var server=Task.Run(async()=>{
            using var peer=await listener.AcceptTcpClientAsync().ConfigureAwait(false); using var wire=peer.GetStream(); var request=new byte[4096]; await wire.ReadAsync(request,0,request.Length,stop.Token).ConfigureAwait(false);
            byte[] data=Encoding.UTF8.GetBytes(body), prefix=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" Test\r\nContent-Type: application/json\r\nContent-Length: "+(stall?1000000:data.Length)+"\r\nConnection: close\r\n\r\n");
            await wire.WriteAsync(prefix,0,prefix.Length,stop.Token).ConfigureAwait(false); await wire.WriteAsync(data,0,data.Length,stop.Token).ConfigureAwait(false);
            if(stall) await Task.Delay(30000,stop.Token).ConfigureAwait(false);
        });
        try { return await http.Send("http://127.0.0.1:"+port+"/synthetic","GET",null,null,null,stream,cancellation).ConfigureAwait(false); }
        finally { stop.Cancel(); listener.Stop(); try { await server.ConfigureAwait(false); } catch(OperationCanceledException) { } }
    }
}
