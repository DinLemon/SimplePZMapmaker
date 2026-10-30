using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SimplePZMapmaker.Web.Services;

/// <summary>Content-addressed asset URLs keep every relative module import in the same editor build.</summary>
public static class EditorAssets
{
 public static void UseVersionedEditorAssets(this WebApplication app){
  var root=app.Environment.WebRootPath;var index=Path.Combine(root,"index.html");
  using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  foreach(var file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".br",StringComparison.Ordinal)&&!p.EndsWith(".gz",StringComparison.Ordinal)).OrderBy(p=>Path.GetRelativePath(root,p),StringComparer.Ordinal)){
   hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root,file).Replace('\\','/')));hash.AppendData([0]);using var stream=File.OpenRead(file);hash.AppendData(SHA256.HashData(stream));
  }
  var prefix="/ui/"+Convert.ToHexStringLower(hash.GetHashAndReset())+"/";
  // Rewrite entry assets directly: the editor deliberately forbids <base> with CSP base-uri 'none'.
  var html=Regex.Replace(File.ReadAllText(index),@"(?<open><(?:script|link|img)\b[^>]*?\b(?:src|href)\s*=\s*)(?<q>[""'])(?<url>[^""']+)\k<q>",match=>{
   var url=match.Groups["url"].Value;if(url.StartsWith('/')||url.StartsWith('#')||Uri.TryCreate(url,UriKind.Absolute,out _))return match.Value;
   return match.Groups["open"].Value+match.Groups["q"].Value+prefix+url+match.Groups["q"].Value;
  },RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
  app.Use(async(context,next)=>{
   if((HttpMethods.IsGet(context.Request.Method)||HttpMethods.IsHead(context.Request.Method))&&(context.Request.Path=="/"||context.Request.Path.Equals("/index.html",StringComparison.OrdinalIgnoreCase))){
    context.Response.ContentType="text/html; charset=utf-8";context.Response.Headers.CacheControl="no-store";context.Response.ContentLength=Encoding.UTF8.GetByteCount(html);if(!HttpMethods.IsHead(context.Request.Method))await context.Response.WriteAsync(html,context.RequestAborted);return;
   }
   await next(context);
  });
  app.UseStaticFiles(new StaticFileOptions{FileProvider=app.Environment.WebRootFileProvider,RequestPath=prefix.TrimEnd('/'),OnPrepareResponse=c=>c.Context.Response.Headers.CacheControl="public, max-age=31536000, immutable"});
  // Old unversioned URLs remain usable, but may no longer acquire heuristic freshness.
  app.UseStaticFiles(new StaticFileOptions{OnPrepareResponse=c=>c.Context.Response.Headers.CacheControl="no-cache, max-age=0, must-revalidate"});
 }
}
