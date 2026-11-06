using Liubai;
using System.Net;
using System.Net.Http;

internal static class ServiceChecks
{
    internal static async Task<int> Run(string root, string release, string package, Action<bool,string> check)
    {
        int requests = 0;
        string install = Path.Combine(root,"service-runtime");Directory.CreateDirectory(install);
        foreach(string file in UpdatePolicy.Required) File.WriteAllText(Path.Combine(install,file),"old "+file);
        var layout = new UpdateLayout(install);
        UpdateService Make(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handle) => new(()=>new HttpClient(new Handler(handle)),layout);
        var service=Make((request,_)=> {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri!.AbsoluteUri==UpdatePolicy.Latest ? new StringContent(release):new ByteArrayContent(File.ReadAllBytes(package))});
        });
        check(requests==0 && service.Release==null && !service.Busy,"No network at construction");
        await service.Check();check(service.CanInstall && !service.Busy && requests==1,"Manual check enables newer update");
        var plan=await service.Prepare(false);
        check(plan!=null && File.Exists(Path.Combine(plan.Stage,"Liubai.dll")),"Valid package prepared");
        check(plan!.Hashes.Count==6 && plan.ExeSha256==UpdatePolicy.Hash(Path.Combine(plan.Stage,"Liubai.exe")),"Core manifest prepared");
        check(File.ReadAllText(Path.Combine(install,"Liubai.dll"))=="old Liubai.dll","Preparation does not replace running app");
        service.Abandon(plan);check(!Directory.Exists(plan.Stage),"Abandoned stage removed");
        var bad=Make((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}" )}));
        await bad.Check();check(!bad.Busy && bad.Release==null,"Invalid metadata recoverable");
        var limited=Make((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        await limited.Check();check(!limited.Busy && limited.Status.Contains("受限"),"Rate limit recoverable");
        var cancel=Make(async (_,token)=>{await Task.Delay(30000,token);return new HttpResponseMessage(HttpStatusCode.OK);});
        var pending=cancel.Check();cancel.Cancel();await pending;check(!cancel.Busy && cancel.Status.Contains("取消"),"Check cancellation");
        var corrupt=Make((request,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=request.RequestUri!.AbsoluteUri==UpdatePolicy.Latest?new StringContent(release):new ByteArrayContent(new byte[new FileInfo(package).Length])}));
        await corrupt.Check();check(await corrupt.Prepare(false)==null && !corrupt.Busy,"Corrupt package rejected without replacement");
        var same=Make((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(release.Replace("0.7.0","0.6.0"))}));
        await same.Check();check(!same.CanInstall && same.Status.Contains("相同"),"Same version never replaces local unpublished changes");
        check(await same.Prepare(false)==null,"Same version preparation refused");
        return 12;
    }
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token); }
}

namespace Liubai
{
    public sealed class App { internal static string DataDirectory {get;set;}=""; }
}
