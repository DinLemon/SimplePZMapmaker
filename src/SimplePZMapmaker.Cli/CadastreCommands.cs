using System.Globalization;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

namespace SimplePZMapmaker.Cli;

public static class CadastreCommands
{
 /// <summary>Command dispatcher seam: cadastre --bounds south,west,north,east --out file --cache directory --layers parcels,buildings.</summary>
 public static int Run(IReadOnlyDictionary<string,string> options)=>RunAsync(options).GetAwaiter().GetResult();
 public static async Task<int> RunAsync(IReadOnlyDictionary<string,string> options,CancellationToken cancellationToken=default)
 {
  try{
   var known=new[]{"--bounds","--out","--cache","--layers","--grid","--step-metres","--expected-region","--refresh"};
   if(options.Keys.Any(k=>!known.Contains(k)))throw new ArgumentException("Unknown cadastre option. Use --bounds --out --cache --layers [--grid 1..16] [--refresh].");
   var parts=(options.GetValueOrDefault("--bounds")??"").Split(',');var values=new double[4];
   if(parts.Length!=4)throw new ArgumentException("--bounds requires south,west,north,east.");
   for(var i=0;i<4;i++)if(!double.TryParse(parts[i],NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])||!double.IsFinite(values[i]))throw new ArgumentException("--bounds requires four finite numbers: south,west,north,east.");
   var target=LocalPath(options.GetValueOrDefault("--out")??throw new ArgumentException("cadastre requires --out source.geojson."));
   var cache=LocalPath(options.GetValueOrDefault("--cache")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SimplePZMapmaker","cadastre"));
   var layers=(options.GetValueOrDefault("--layers")??"parcels,buildings").Split(',').Select(s=>s.Trim().ToLowerInvariant() switch{"parcels" or "36048"=>36048,"buildings" or "36049"=>36049,_=>throw new ArgumentException("--layers accepts parcels,buildings or 36048,36049.")}).ToArray();
   var grid=0;if(options.TryGetValue("--grid",out var text)&&(!int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out grid)||grid is <1 or >16))throw new ArgumentException("--grid requires an integer 1..16 (explicit coarse sampling).");
   var step=9d;if(options.TryGetValue("--step-metres",out var stepText)&&(!double.TryParse(stepText,NumberStyles.Float,CultureInfo.InvariantCulture,out step)||!double.IsFinite(step)))throw new ArgumentException("--step-metres requires a finite number (default 9).");
   if(options.ContainsKey("--grid")&&options.ContainsKey("--step-metres"))throw new ArgumentException("Choose --step-metres or the explicit coarse --grid, not both.");
   using var client=new CadastreClient(cache);
   var request=new CadastreRequest{Bounds=new(){South=values[0],West=values[1],North=values[2],East=values[3]},Layers=layers,GridSize=grid,StepMetres=step,ExpectedRegionCode=options.GetValueOrDefault("--expected-region"),Refresh=options.ContainsKey("--refresh")};
   var plan=client.Plan(request);Console.WriteLine($"cadastre plan: {plan.Columns} x {plan.Rows}; {plan.SamplesPlanned} layer-samples; minimum {plan.MinimumSecondsWithoutCoverageSkipping:F0}s without coverage skipping; complete parcel inventory cannot be guaranteed.");
   var result=await client.FetchAsync(request,cancellationToken,new ConsoleProgress());
   var parent=Path.GetDirectoryName(target)!;Directory.CreateDirectory(parent);var temporary=target+".tmp-"+Guid.NewGuid().ToString("N");
   try{await File.WriteAllTextAsync(temporary,result.GeoJson,cancellationToken);cancellationToken.ThrowIfCancellationRequested();File.Move(temporary,target,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
   Console.WriteLine($"cadastre: {result.Features} objects; {result.Requests} HTTP requests; {result.SkippedSamples} covered samples skipped; cache={result.FromCache.ToString().ToLowerInvariant()}");
   foreach(var diagnostic in result.Diagnostics)Console.WriteLine(diagnostic);return 0;
  }
  catch(ArgumentException e){Console.Error.WriteLine("cadastre: "+e.Message);return 2;}
  catch(CadastreException e){Console.Error.WriteLine($"cadastre [{e.Code}]: {e.Message}");if(e.Progress is { } p)Console.Error.WriteLine($"Grid {p.SamplesCompleted}/{p.SamplesPlanned}; cached points {p.PointsFromCache}; requests {p.Requests}. Rerun the same selection to resume.");return 1;}
  catch(OperationCanceledException){Console.Error.WriteLine("cadastre: cancelled; incomplete output/cache is not published.");return 130;}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or NotSupportedException){Console.Error.WriteLine("cadastre: local input/output unavailable; previous output retained.");return 1;}
 }
 static string LocalPath(string value){try{return Path.GetFullPath(value);}catch(Exception e) when(e is ArgumentException or NotSupportedException){throw new ArgumentException("Invalid local output/cache path.");}}
 sealed class ConsoleProgress:IProgress<CadastreProgress>{int last=-1;public void Report(CadastreProgress p){if(p.SamplesCompleted/25==last&&!p.PlannedGridComplete)return;last=p.SamplesCompleted/25;Console.WriteLine($"cadastre grid: {p.SamplesCompleted}/{p.SamplesPlanned}; HTTP {p.Requests}; cached {p.PointsFromCache}; covered {p.SkippedSamples}");}}
}
