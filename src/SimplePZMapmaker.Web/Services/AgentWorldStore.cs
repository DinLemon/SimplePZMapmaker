using System.Security.Cryptography;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;
public sealed class AgentWorldStore(string directory) {
 readonly object gate=new();
 readonly Dictionary<string,AgentStatus> statuses=[];
 public sealed class Snapshot {
  public string Token {get;set;}="";public long Revision {get;set;}=1;public string Author {get;set;}="browser";public DateTimeOffset Updated {get;set;}=DateTimeOffset.UtcNow;public WorldProject World {get;set;}=new();
 }
 string FileName(string token){if(token is not {Length:64}||!token.All(char.IsAsciiHexDigit))throw new ArgumentException("Invalid world session key.");return Path.Combine(directory,token.ToLowerInvariant()+".world-agent.json");}
 Snapshot Load(string token)=>RecoveryArchive.Read<Snapshot>(FileName(token),WorldStore.MaxBytes,d=>WorldAssembler.Validate(d.World),version:0).GetAwaiter().GetResult()??throw new ArgumentException("World session not found.");
 void Save(Snapshot value){RecoveryArchive.Write(FileName(value.Token),value,WorldStore.MaxBytes).GetAwaiter().GetResult();statuses[value.Token]=new(value.Revision,value.Author,value.Updated);}
 public Snapshot Create(WorldProject world){lock(gate){Directory.CreateDirectory(directory);if(Directory.EnumerateFiles(directory,"*.world-agent.json").Count()>=16)throw new ArgumentException("Close unused world sessions: the limit is 16.");WorldAssembler.Validate(world);var s=new Snapshot{Token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),World=NeighborhoodPlanner.Copy(world)};Save(s);return s;}}
 public Snapshot Read(string token){lock(gate)return Load(token);}
 public object Status(string token){lock(gate){FileName(token);token=token.ToLowerInvariant();if(statuses.TryGetValue(token,out var status))return status;var s=Load(token);return statuses[token]=new(s.Revision,s.Author,s.Updated);}}
 public Snapshot Mutate(string token,long revision,Action<WorldProject> action,string author="agent"){lock(gate){var s=Load(token);if(s.Revision!=revision)throw new AgentRevisionConflict(s.Revision);action(s.World);WorldAssembler.Validate(s.World);s.Revision++;s.Author=author;s.Updated=DateTimeOffset.UtcNow;Save(s);return s;}}
 public Snapshot Publish(string token,long revision,WorldProject world)=>Mutate(token,revision,w=>{var copy=NeighborhoodPlanner.Copy(world);w.Version=copy.Version;w.Name=copy.Name;w.Maps=copy.Maps;w.LinearFeatures=copy.LinearFeatures;w.Buildings=copy.Buildings;w.GeoReference=copy.GeoReference;w.Elevation=copy.Elevation;},"browser");
 public void Close(string token){lock(gate){RecoveryArchive.Clear(FileName(token));statuses.Remove(token.ToLowerInvariant());}}
}
