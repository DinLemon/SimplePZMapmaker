using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

public sealed class DesignConstraints {
 public int X {get;set;} public int Y {get;set;} public int Width {get;set;}=64;public int Height {get;set;}=64;
 public int MaximumIterations {get;set;}=4;public int MaximumChanges {get;set;}=8;
 public bool AllowNewDoors {get;set;}=true;public bool AllowStairs {get;set;}=true;public bool AllowEditedEdges {get;set;}
 public bool RequireLighting {get;set;}=true;public int LightingX {get;set;} public int LightingY {get;set;} public int LightingSize {get;set;}=32;public int LightingLevel {get;set;}
 public double MinimumLight {get;set;}=.15;public double Ambient {get;set;}=.2;public double[] SkyRgb {get;set;}=[.2,.2,.2];
 public bool RoomLightsPowered {get;set;}=true;
 public bool AllowLights {get;set;} public string? LampModelId {get;set;}
 public WalkabilityOptions Crop()=>new(){X=X,Y=Y,Width=Width,Height=Height};
 public void Validate(MapProject p){if(X<0||Y<0||Width is <8 or >128||Height is <8 or >128||(long)X+Width>p.Width||(long)Y+Height>p.Height||MaximumIterations is <1 or >8||MaximumChanges is <1 or >16||LightingSize is <8 or >64||RequireLighting&&(LightingX<X||LightingY<Y||(long)LightingX+LightingSize>X+Width||(long)LightingY+LightingSize>Y+Height)||!ZLevels.Contains(LightingLevel)||!double.IsFinite(MinimumLight)||MinimumLight is <0 or >1||!double.IsFinite(Ambient)||Ambient is <0 or >1||SkyRgb is null||SkyRgb.Length!=3||SkyRgb.Any(v=>!double.IsFinite(v)||v is <0 or >1)||AllowLights&&string.IsNullOrWhiteSpace(LampModelId))throw new ArgumentException("Use an in-map 8–128 tile design area, up to 8 iterations/16 changes, and an 8–64 tile lighting crop inside it. Light repair needs a chosen registered model.");}
}
public sealed record DesignChange(string Kind,string Message,int X,int Y,int Z);
public sealed record DesignIteration(int Number,WalkabilityReport Access,string LightingStatus,string? LightingError,double? Minimum,double? Average,double? DarkFraction,JsonElement? Lighting,List<DesignChange> Changes,bool Passed);
public sealed class AgentDesignTask {
 public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Scope {get;set;}="map";public string? MemberId {get;set;}
 public long BaseRevision {get;set;} public long TaskRevision {get;set;} public string Objective {get;set;}="Checked design";
 public MapProject Candidate {get;set;}=new();public DesignConstraints Constraints {get;set;}=new();
 public List<DesignIteration> Iterations {get;set;}=[];public List<DesignChange> Changes {get;set;}=[];
 public bool Passed=>Iterations.LastOrDefault()?.Passed==true;
}
public sealed class AgentDesignStore(string directory) {
 const int MaximumBytes=48*1024*1024;readonly object gate=new();
 string PathFor(string token,string id){if(token is not{Length:64}||!token.All(char.IsAsciiHexDigit)||!WorldAssembler.SafeId(id))throw new ArgumentException("Invalid checked-design key.");return Path.Combine(directory,token.ToLowerInvariant(),id+".design.json");}
 static void Validate(AgentDesignTask task){if(task is null||!WorldAssembler.SafeId(task.Id)||task.BaseRevision<1||task.Scope is not("map" or "world")||task.Scope=="world"&&!WorldAssembler.SafeId(task.MemberId)||task.Scope=="map"&&task.MemberId is not null||string.IsNullOrWhiteSpace(task.Objective)||task.Objective.Length>800||task.Objective.Any(char.IsControl)||task.Iterations is null||task.Iterations.Count>32||task.Changes is null||task.Changes.Count>16)throw new ArgumentException("Invalid checked-design proposal.");task.Constraints.Validate(task.Candidate);}
 public AgentDesignTask Read(string token,string id){lock(gate){var file=PathFor(token,id);if(!File.Exists(file)||new FileInfo(file).Length>MaximumBytes)throw new ArgumentException("Checked-design proposal not found or too large.");var task=JsonSerializer.Deserialize<AgentDesignTask>(File.ReadAllBytes(file),NativeMapExporter.JsonOptions)??throw new ArgumentException("Invalid checked-design file.");Validate(task);return task;}}
 public AgentDesignTask Save(string token,AgentDesignTask task,long? expectedTaskRevision){lock(gate){Validate(task);var file=PathFor(token,task.Id);var folder=Path.GetDirectoryName(file)!;Directory.CreateDirectory(folder);if(expectedTaskRevision is {} expected){var old=Read(token,task.Id);if(old.TaskRevision!=expected)throw new AgentRevisionConflict(old.TaskRevision);task.TaskRevision=checked(expected+1);}else{if(File.Exists(file)||Directory.EnumerateFiles(folder,"*.design.json").Count()>=8)throw new ArgumentException("A session may have 8 checked-design proposals. Close unused sessions.");task.TaskRevision=1;}var bytes=JsonSerializer.SerializeToUtf8Bytes(task,NativeMapExporter.JsonOptions);if(bytes.Length>MaximumBytes||Directory.EnumerateFiles(folder,"*.design.json").Where(f=>f!=file).Sum(f=>new FileInfo(f).Length)+bytes.Length>192L*1024*1024)throw new ArgumentException("Checked designs exceed their 48 MB proposal or 192 MB session budget.");var tmp=file+".tmp";try{File.WriteAllBytes(tmp,bytes);File.Move(tmp,file,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}return task;}}
 public void Close(string token){lock(gate){var folder=Path.GetDirectoryName(PathFor(token,new string('a',32)))!;if(Directory.Exists(folder))foreach(var file in Directory.EnumerateFiles(folder,"*.design.json*"))File.Delete(file);}}
 public string[] List(string token){lock(gate){var folder=Path.GetDirectoryName(PathFor(token,new string('a',32)))!;return Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*.design.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(8).Select(f=>Path.GetFileName(f).Replace(".design.json","")).ToArray():[];}}
}
