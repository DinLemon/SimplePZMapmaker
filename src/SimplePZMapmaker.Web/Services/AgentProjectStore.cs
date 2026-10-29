using System.Security.Cryptography;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

public sealed class AgentRevisionConflict(long current):Exception("The editor and agent changed the same revision. Retrieve the latest project version first.") {public long Current {get;}=current;}
public sealed record AgentSnapshot(string Token,long Revision,string Author,DateTimeOffset Updated,MapProject Project);
public sealed record AgentDraft(string Id,Building Building);
public sealed record AgentStatus(long Revision,string Author,DateTimeOffset Updated);
public sealed class AgentProjectStore(string directory) {
 private readonly object gate=new();
 private readonly Dictionary<string,AgentStatus> statusCache=[];
 public sealed class Document {
  public string Token {get;set;}="";public long Revision {get;set;}=1;public string Author {get;set;}="browser";public DateTimeOffset Updated {get;set;}=DateTimeOffset.UtcNow;
  public MapProject Project {get;set;}=new();public Dictionary<string,Building> Drafts {get;set;}=[];
 }
 static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
 string FileName(string token){if(token is not {Length:64}||!token.All(char.IsAsciiHexDigit))throw new ArgumentException("Invalid agent session key.");return Path.Combine(directory,token.ToLowerInvariant()+".agent.json");}
 Document Load(string token){var file=FileName(token);if(!File.Exists(file))throw new ArgumentException("Agent session not found. Connect the current map in the editor.");if(new FileInfo(file).Length>ProjectValidator.MaximumProjectBytes)throw new ArgumentException("The session exceeds 128 MB.");var document=JsonSerializer.Deserialize<Document>(File.ReadAllBytes(file),NativeMapExporter.JsonOptions)??throw new ArgumentException("Invalid session.");ProjectValidator.Validate(document.Project);return document;}
 void Save(Document document){ProjectValidator.Validate(document.Project);var bytes=JsonSerializer.SerializeToUtf8Bytes(document,NativeMapExporter.JsonOptions);if(bytes.Length>ProjectValidator.MaximumProjectBytes)throw new ArgumentException("The session exceeds 128 MB.");Directory.CreateDirectory(directory);var file=FileName(document.Token);var temporary=file+".tmp";try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,file,true);statusCache[document.Token]=new(document.Revision,document.Author,document.Updated);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
 static AgentSnapshot Snapshot(Document d)=>new(d.Token,d.Revision,d.Author,d.Updated,Clone(d.Project));
 public AgentSnapshot Create(MapProject project){lock(gate){Directory.CreateDirectory(directory);if(Directory.EnumerateFiles(directory,"*.agent.json").Count()>=32)throw new ArgumentException("The limit of 32 sessions has been reached. Close unused agent sessions.");var document=new Document{Token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),Project=Clone(project)};Save(document);return Snapshot(document);}}
 public AgentSnapshot Read(string token){lock(gate)return Snapshot(Load(token));}
 public AgentStatus Status(string token){lock(gate){FileName(token);token=token.ToLowerInvariant();if(statusCache.TryGetValue(token,out var status))return status;var d=Load(token);return statusCache[token]=new(d.Revision,d.Author,d.Updated);}}
 public AgentSnapshot Mutate(string token,long expectedRevision,Action<MapProject> mutate,string author){lock(gate){var document=Load(token);if(document.Revision!=expectedRevision)throw new AgentRevisionConflict(document.Revision);mutate(document.Project);ProjectValidator.Validate(document.Project);document.Revision=checked(document.Revision+1);document.Updated=DateTimeOffset.UtcNow;document.Author=author=="agent"?"agent":"browser";Save(document);return Snapshot(document);}}
 public AgentSnapshot Publish(string token,long expectedRevision,MapProject project){lock(gate){var d=Load(token);if(d.Revision!=expectedRevision)throw new AgentRevisionConflict(d.Revision);if(d.Project.Width!=project.Width||d.Project.Height!=project.Height)throw new ArgumentException("Create a new session for a map with different dimensions.");d.Project=Clone(project);d.Revision=checked(d.Revision+1);d.Author="browser";d.Updated=DateTimeOffset.UtcNow;Save(d);return Snapshot(d);}}
 public AgentDraft SaveDraft(string token,Building building,string? draftId=null){lock(gate){BuildingDesigner.Validate(building,false);var d=Load(token);var id=draftId??Guid.NewGuid().ToString("N");if(!WorldAssembler.SafeId(id)||draftId is not null&&!d.Drafts.ContainsKey(id))throw new ArgumentException("Building draft not found.");if(draftId is null&&d.Drafts.Count>=16)throw new ArgumentException("The session already contains 16 drafts.");d.Drafts[id]=Clone(building);Save(d);return new(id,Clone(building));}}
 public AgentDraft ReadDraft(string token,string id){lock(gate){var d=Load(token);return new(id,Clone(d.Drafts.GetValueOrDefault(id)??throw new ArgumentException("Building draft not found.")));}}
 public void Close(string token){lock(gate){var file=FileName(token);if(File.Exists(file))File.Delete(file);statusCache.Remove(token.ToLowerInvariant());}}
}
