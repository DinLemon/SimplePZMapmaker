using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SimplePZMapmaker.Web.Services;

public static class EditorAssetTests
{
 public static IEnumerable<(string,Action)> Cases(){yield return("editor assets version the entire module graph and revalidate legacy URLs",()=>Verify().GetAwaiter().GetResult());}
 static async Task Verify(){
  var root=Path.Combine(Directory.GetCurrentDirectory(),"work","editor-asset-tests",Guid.NewGuid().ToString("N"));var web=Path.Combine(root,"wwwroot");Directory.CreateDirectory(web);
  await File.WriteAllTextAsync(Path.Combine(web,"index.html"),"<!doctype html><html><head><script type=\"module\" src=\"app.js\"></script></head><body>Editor</body></html>");
  await File.WriteAllTextAsync(Path.Combine(web,"app.js"),"import './session.js';");await File.WriteAllTextAsync(Path.Combine(web,"session.js"),"export const revision=1;");
  async Task<string> Run(string? previous=null){
   var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions{ContentRootPath=root,WebRootPath=web});builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();await using var app=builder.Build();app.UseVersionedEditorAssets();await app.StartAsync();
   try{using var client=new HttpClient{BaseAddress=new(app.Urls.Single())};using var entry=await client.GetAsync("/");var html=await entry.Content.ReadAsStringAsync();var prefix=Regex.Match(html,"src=\"(/ui/[^\"]+/)app.js\"").Groups[1].Value;Check(prefix.StartsWith("/ui/")&&prefix.EndsWith('/'),"Editor has no versioned entry module");Check(!html.Contains("<base"),"Editor versioning conflicts with CSP base-uri none");Check(entry.Headers.CacheControl?.NoStore==true,"Entry can be reused from a stale cache");
    using var module=await client.GetAsync(prefix+"session.js");Check(module.IsSuccessStatusCode,"Versioned module is unavailable");Check(module.Headers.CacheControl?.Extensions.Any(e=>e.Name=="immutable")==true,"Versioned graph is not immutable");
    using var legacy=await client.GetAsync("/session.js");Check(legacy.Headers.CacheControl?.NoCache==true,"Unversioned module can bypass revalidation");
    using var head=new HttpRequestMessage(HttpMethod.Head,"/index.html");using var headResponse=await client.SendAsync(head);Check(headResponse.StatusCode==HttpStatusCode.OK&&headResponse.Headers.CacheControl?.NoStore==true,"HEAD entry cache policy differs");
    if(previous is not null){Check(prefix!=previous,"Changing a transitive module did not change the graph URL");using var old=await client.GetAsync(previous+"session.js");Check(old.StatusCode==HttpStatusCode.NotFound,"A stale graph silently served another build's module");}return prefix;
   }finally{await app.StopAsync();}
  }
  var first=await Run();Check(await Run()==first,"Unchanged content has an unstable asset ID");await File.WriteAllTextAsync(Path.Combine(web,"session.js"),"export const revision=2;");await Run(first);
 }
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
}
