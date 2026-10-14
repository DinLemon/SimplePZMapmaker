using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public sealed class AgentZoneStore(string directory)
{
 public sealed record Entry(GameZone Zone,string? MemberId=null);
 public sealed class Proposal {
  public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Scope {get;set;}="map";public long BaseRevision {get;set;}
  public string Objective {get;set;}="Gameplay zones";public Entry[] Entries {get;set;}=[];
 }
 readonly object gate=new();
 string FileName(string token,string id){if(token is not {Length:64}||!token.All(char.IsAsciiHexDigit)||!WorldAssembler.SafeId(id))throw new ArgumentException("Invalid zone proposal key.");return Path.Combine(directory,token.ToLowerInvariant(),id+".zones.json");}
 static void Validate(Proposal p){if(!WorldAssembler.SafeId(p.Id)||p.Scope is not("map" or "world")||p.BaseRevision<1||string.IsNullOrWhiteSpace(p.Objective)||p.Objective.Length>800||p.Objective.Any(char.IsControl)||p.Entries is null||p.Entries.Length is <1 or >64||p.Entries.Any(e=>e is null||e.Zone is null||p.Scope=="world"&&!WorldAssembler.SafeId(e.MemberId)||p.Scope=="map"&&e.MemberId is not null)||p.Entries.Select(e=>e.Zone.Id).Distinct().Count()!=p.Entries.Length)throw new ArgumentException("Invalid gameplay zone proposal.");}
 public Proposal Save(string token,Proposal p){lock(gate){Validate(p);var file=FileName(token,p.Id);Directory.CreateDirectory(Path.GetDirectoryName(file)!);if(!File.Exists(file)&&Directory.EnumerateFiles(Path.GetDirectoryName(file)!,"*.zones.json").Count()>=16)throw new ArgumentException("A session may contain 16 zone proposals. Close unused sessions.");RecoveryArchive.Write(file,p,512*1024).GetAwaiter().GetResult();return p;}}
 public Proposal Read(string token,string id){lock(gate)return RecoveryArchive.Read<Proposal>(FileName(token,id),512*1024,Validate,version:0).GetAwaiter().GetResult()??throw new ArgumentException("Zone proposal not found.");}
 public void Close(string token){lock(gate){var folder=Path.GetDirectoryName(FileName(token,new string('a',32)))!;if(Directory.Exists(folder))foreach(var file in Directory.EnumerateFiles(folder,"*.zones.json*"))File.Delete(file);}}
}
