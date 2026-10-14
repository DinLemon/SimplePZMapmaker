using System.Text.RegularExpressions;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
/// <summary>Reads literal registered distribution names. Never loads or evaluates mod Lua.</summary>
public sealed class GameplayCatalog(GameAssetCatalog assets)
{
 public sealed record Distribution(string Id,string SourceId,string[] RequiredMods);
 private readonly object gate=new();private string fingerprint="";private Dictionary<string,Distribution> loot=[],vehicles=[];
 public object Snapshot(){Refresh();return new{zoneTypes=GameZone.Types,loot=loot.Values.OrderBy(d=>d.Id).ToArray(),vehicles=vehicles.Values.OrderBy(d=>d.Id).ToArray(),limitations=new[]{"Computed Lua registrations cannot be inspected safely and are unavailable.","Vehicle spawning uses the game's distribution and sandbox probabilities."}};}
 public void Validate(StaticModelPlacement m){if(m.LootDistribution is not {} id)return;Refresh();if(!loot.TryGetValue(id,out var d)||d.SourceId!=m.LootSourceId)throw new ArgumentException("The loot distribution is absent or its active owner changed. Select it again.");m.LootRequiredMods=d.RequiredMods.ToList();}
 public void Validate(MapProject p){Refresh();foreach(var z in p.GameZones.Where(z=>z.Vehicle)){if(!vehicles.TryGetValue(z.VehicleDistribution!,out var d)||d.SourceId!=z.VehicleSourceId)throw new ArgumentException("The vehicle distribution is absent or its active owner changed. Select it again.");z.RequiredMods=d.RequiredMods.ToList();}}
 public void Refresh()
 {
  lock(gate){var files=new List<(string Path,ResourceSource Source)>();long bytes=0;foreach(var s in assets.ActiveSources()){var dir=Path.Combine(s.Root,"media","lua");if(!Directory.Exists(dir))continue;foreach(var file in SafeFiles(dir)){var info=new FileInfo(file);bytes+=info.Length;if(files.Count>=10000||info.Length>32*1024*1024||bytes>128*1024*1024)throw new ArgumentException("Gameplay declaration scan exceeds its file or size limits.");files.Add((file,s));}}
   var key=string.Join('|',files.Select(f=>f.Source.Id+":"+f.Path+":"+new FileInfo(f.Path).Length+":"+File.GetLastWriteTimeUtc(f.Path).Ticks));if(key==fingerprint)return;var l=new Dictionary<string,Distribution>();var v=new Dictionary<string,Distribution>();foreach(var f in files){var text=File.ReadAllText(f.Path);if(!text.Contains("ProceduralDistributions",StringComparison.Ordinal)&&!text.Contains("VehicleZoneDistribution",StringComparison.Ordinal))continue;var mods=f.Source.Requirements.Concat(f.Source.RequiredModId is {} modId?[modId]:[]).Distinct(StringComparer.Ordinal).ToArray();foreach(var id in LiteralDistributionNames.Read(text,"ProceduralDistributions.list",true))l[id]=new(id,f.Source.Id,mods);foreach(var id in LiteralDistributionNames.Read(text,"VehicleZoneDistribution",false))v[id]=new(id,f.Source.Id,mods);if(l.Count+v.Count>30000)throw new ArgumentException("Too many registered gameplay distributions.");}loot=l;vehicles=v;fingerprint=key;
  }
 }
 private static IEnumerable<string> SafeFiles(string dir){var stack=new Stack<string>();stack.Push(dir);while(stack.TryPop(out var current)){Reject(current);foreach(var file in Directory.EnumerateFiles(current,"*.lua")){Reject(file);yield return file;}foreach(var child in Directory.EnumerateDirectories(current))stack.Push(child);}}
 private static void Reject(string path){for(var p=Path.GetFullPath(path);p.Length>0;p=Path.GetDirectoryName(p)??"")if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Gameplay resource links and redirects are unsupported.");}
}
public static class LiteralDistributionNames
{
 public static IReadOnlyList<string> Read(string text,string table,bool nested)
 {
  if(text.Length>32*1024*1024)throw new ArgumentException("Lua declarations exceed 32 MB.");var t=Tokens(text).ToArray();var prefix=table.Split('.');var names=new HashSet<string>(StringComparer.Ordinal);
  for(var i=0;i<t.Length;i++){var n=i;var match=true;foreach(var part in prefix){if(n>=t.Length||t[n++]!=part){match=false;break;}if(part!=prefix[^1]&&(n>=t.Length||t[n++]!=".")){match=false;break;}}if(!match)continue;
   if(n+2<t.Length&&t[n]=="."&&Safe(t[n+1])&&t[n+2]=="="){names.Add(t[n+1]);continue;}
   if(!nested||n+1>=t.Length||t[n]!="="||t[n+1]!="{")continue;var depth=1;n+=2;while(n<t.Length&&depth>0){if(depth==1&&Safe(t[n])&&n+1<t.Length&&t[n+1]=="=")names.Add(t[n]);if(t[n]=="{")depth++;else if(t[n]=="}")depth--;n++;}
  }return names.Order(StringComparer.Ordinal).ToArray();
 }
 private static bool Safe(string s)=>Regex.IsMatch(s,@"^[A-Za-z][A-Za-z0-9_]{0,119}$",RegexOptions.None,TimeSpan.FromSeconds(1));
 private static IEnumerable<string> Tokens(string s)
 {
  for(var i=0;i<s.Length;){if(char.IsWhiteSpace(s[i])){i++;continue;}if(i+1<s.Length&&s[i]=='-'&&s[i+1]=='-'){i+=2;if(LongOpen(s,i,out var start,out var end)){var p=s.IndexOf(end,start,StringComparison.Ordinal);i=p<0?s.Length:p+end.Length;}else{while(i<s.Length&&s[i]!='\n')i++;}continue;}
   if(s[i] is '\'' or '"'){var quote=s[i++];while(i<s.Length){if(s[i++]=='\\'){if(i<s.Length)i++;}else if(s[i-1]==quote)break;}yield return "@string";continue;}if(LongOpen(s,i,out var body,out var close)){var p=s.IndexOf(close,body,StringComparison.Ordinal);i=p<0?s.Length:p+close.Length;yield return "@string";continue;}
   if(char.IsLetter(s[i])||s[i]=='_'){var start=i++;while(i<s.Length&&(char.IsLetterOrDigit(s[i])||s[i]=='_'))i++;yield return s[start..i];}else yield return s[i++].ToString();
  }
 }
 private static bool LongOpen(string s,int i,out int body,out string close){body=i;close="";if(i>=s.Length||s[i]!='[')return false;var n=i+1;while(n<s.Length&&s[n]=='=')n++;if(n>=s.Length||s[n]!='[')return false;body=n+1;close="]"+new string('=',n-i-1)+"]";return true;}
}
public static class GameplayAuthoringEndpoints
{
 public static void MapGameplayAuthoringEndpoints(this WebApplication app){app.MapGet("/api/gameplay/catalog",(GameplayCatalog catalog)=>catalog.Snapshot());app.MapPost("/api/gameplay/model",(StaticModelPlacement model,StaticModelCatalog catalog)=>{catalog.Validate(model);return model;});}
}
