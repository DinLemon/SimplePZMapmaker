using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Diagnostics;
using SimplePZMapmaker.Core.Exchange;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

namespace SimplePZMapmaker.Cli;

internal static class AuthoringCommands
{
 public static int Doctor(MapProject? map,WorldProject? world,GameAssetCatalog assets,StaticModelCatalog models,string? output,bool strict)
 {
  var report=ProjectDoctorEndpoints.Analyze(new(){Project=map,World=world},assets,models);
  foreach(var check in report.Checks)Console.WriteLine($"{check.Category}: {check.Status} — {check.Message}");
  foreach(var finding in report.Findings.Where(f=>f.Severity!="info"))Console.WriteLine($"{finding.Severity}: {finding.Category}: {finding.Message}");
  if(output is not null)File.WriteAllText(output,JsonSerializer.Serialize(report,NativeMapExporter.JsonOptions));
  return report.Status=="error"||strict&&!report.Complete?1:0;
 }
 public static int CompareTiles(string before,string after,string? output)
 {
  TileDefinitionSnapshot Read(string path){using var stream=File.OpenRead(path);return ProjectDoctorEndpoints.ReadBinary(stream,Path.GetFileName(path));}
  var report=TileDefinitionComparer.Compare(Read(before),Read(after));
  Console.WriteLine($"tile compatibility: {report.Status}; {report.Differences.Count} differences, {report.Findings.Count} findings");
  foreach(var f in report.Findings)Console.WriteLine($"{f.Severity}: {f.Message}");
  if(output is not null)File.WriteAllText(output,JsonSerializer.Serialize(report,NativeMapExporter.JsonOptions));
  return report.Findings.Any(f=>f.Severity=="error")?1:0;
 }
 public static int Exchange(string input,string output,string format,bool import,bool allowLossy,string? reportFile)
 {
  if(new FileInfo(input).Length>BuildingExchange.MaxInputBytes)throw new ArgumentException("Building exchange input exceeds 8 MiB.");
  BuildingExchangeResult result;
  if(import)result=BuildingExchange.Import(File.ReadAllText(input),format,allowLossy);
  else{
   using var document=JsonDocument.Parse(File.ReadAllText(input));var root=document.RootElement;
   var b=(root.TryGetProperty("building",out var nested)?nested:root).Deserialize<Building>(NativeMapExporter.JsonOptions)??throw new ArgumentException("Missing building draft.");
   result=BuildingExchange.Export(b,format,allowLossy);
  }
  foreach(var issue in result.Diagnostics)Console.WriteLine($"{issue.Severity}: {issue.Path}: {issue.Message}");
  if(reportFile is not null)File.WriteAllText(reportFile,JsonSerializer.Serialize(result.Diagnostics,NativeMapExporter.JsonOptions));
  if(!result.Success)return 1;
  var text=import?JsonSerializer.Serialize(new{format="SimplePZBuilding",version=8,building=result.Building},NativeMapExporter.JsonOptions):result.Xml!;
  var target=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(target)!);var temporary=target+".tmp-"+Guid.NewGuid().ToString("N");
  try{File.WriteAllText(temporary,text);File.Move(temporary,target,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
  Console.WriteLine("wrote "+target);return 0;
 }
}
