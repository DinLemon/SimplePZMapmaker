using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public sealed class AgentPlanningStore(string directory) {
 readonly object gate=new();
 string FileName(string token,string id){if(token is not {Length:64}||!token.All(char.IsAsciiHexDigit)||!WorldAssembler.SafeId(id))throw new ArgumentException("Invalid proposal ID.");return Path.Combine(directory,token.ToLowerInvariant(),id+".plan.json");}
 public NeighborhoodPlan Save(string token,NeighborhoodPlan plan){lock(gate){var file=FileName(token,plan.Id);var folder=Path.GetDirectoryName(file)!;Directory.CreateDirectory(folder);if(!File.Exists(file)&&Directory.EnumerateFiles(folder,"*.plan.json").Count()>=16)throw new ArgumentException("A session may contain up to 16 proposals.");RecoveryArchive.Write(file,plan,12_000_000).GetAwaiter().GetResult();return plan;}}
 public NeighborhoodPlan Read(string token,string id){lock(gate)return RecoveryArchive.Read<NeighborhoodPlan>(FileName(token,id),12_000_000,p=>{NeighborhoodPlanner.ValidateConstraints(p.Constraints);if(p.Buildings.Count>128||p.Roads.Count>8192)throw new ArgumentException("The proposal exceeds its budget.");},version:0).GetAwaiter().GetResult()??throw new ArgumentException("Proposal not found.");}
 public void Close(string token){lock(gate){var folder=Path.GetFullPath(Path.GetDirectoryName(FileName(token,new string('a',32)))!);var parent=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!folder.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The proposal folder is outside the workspace.");if(Directory.Exists(folder))foreach(var file in Directory.EnumerateFiles(folder,"*.plan.json*"))File.Delete(file);}}
}
