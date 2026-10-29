namespace SimplePZMapmaker.Core;

public static partial class GeographicImporter
{
 /// <summary>Parent hook: call AFTER ImportFences, ImportZones and ApplyStreetDressing, before final project validation.
 /// Input p is the staged import clone. Existing source-ID decorations are never rewritten or relocated.</summary>
 static void ImportFuelStations(MapProject p,List<Feature> features,List<string> notes,CancellationToken token=default)
 {
  var sources=new List<FuelStationSource>();var unsupported=0;var excess=0;var inactive=0;var inactivePrefixes=new HashSet<string>(StringComparer.Ordinal);var inactiveAreas=new List<Feature>();var lifecycleLimited=false;
  foreach(var f in features){
   token.ThrowIfCancellationRequested();
   var lifecycleFuel=new[]{"disused","abandoned"}.Any(prefix=>f.Tags.GetValueOrDefault(prefix+":amenity")=="fuel"||f.Tags.GetValueOrDefault(prefix+":man_made")=="fuel_pump");
   var explicitPump=f.Tags.GetValueOrDefault("man_made")=="fuel_pump";if(!explicitPump&&f.Tags.GetValueOrDefault("amenity")!="fuel"&&!lifecycleFuel)continue;
   if(lifecycleFuel||new[]{"disused","abandoned"}.Any(key=>f.Tags.TryGetValue(key,out var value)&&!string.IsNullOrWhiteSpace(value)&&value is not("no" or "false" or "0"))){
    inactive++;if(f.Tags.GetValueOrDefault("__pz_source") is {} inactiveSource)inactivePrefixes.Add(FuelStationLayout.SourcePrefix(inactiveSource));
    if(f.Polygon&&f.Rings.Count>0&&f.Tags.GetValueOrDefault("level","0")=="0"&&f.Tags.GetValueOrDefault("layer","0")=="0"){
     if(inactiveAreas.Count<FuelStationLayout.MaximumSources&&f.Rings.Sum(r=>(long)r.Count)<=FuelStationLayout.MaximumVertices)inactiveAreas.Add(f);else lifecycleLimited=true;
    }
    continue;
   }
   if(f.Tags.ContainsKey("building")||f.Tags.GetValueOrDefault("level","0")!="0"||f.Tags.GetValueOrDefault("layer","0")!="0"||f.Tags.GetValueOrDefault("access") is "no" or "private"||
    f.Rings.Count==0||explicitPump&&f.Polygon||!f.Polygon&&(f.Rings.Count!=1||f.Rings[0].Count!=1)){unsupported++;continue;}
   var source=f.Tags.GetValueOrDefault("__pz_source");if(string.IsNullOrWhiteSpace(source)){unsupported++;continue;}
   if(sources.Count>=FuelStationLayout.MaximumSources){excess++;continue;}
   sources.Add(new(source,f.Rings.Select(r=>(IReadOnlyList<(double X,double Y)>)r).ToArray(),f.Polygon,explicitPump));
  }
  long lifecycleWork=0;var inherited=0;
  var closedAreas=inactiveAreas.Select(f=>f.Rings.Select(r=>(IReadOnlyList<(double X,double Y)>)r).ToArray()).ToArray();
  for(var i=sources.Count-1;i>=0&&!lifecycleLimited;i--){var source=sources[i];if(source.Polygon)continue;var point=source.Rings[0][0];
   foreach(var area in closedAreas){
    token.ThrowIfCancellationRequested();var charge=area.Sum(r=>(long)r.Count);if(charge>FuelStationLayout.MaximumWork-lifecycleWork){lifecycleLimited=true;break;}lifecycleWork+=charge;
    if(FuelStationLayout.ContainsFilledPoint(area,point.X,point.Y)){inactivePrefixes.Add(FuelStationLayout.SourcePrefix(source.SourceId));sources.RemoveAt(i);inherited++;break;}
   }
  }
  inactive+=inherited;
  if(inherited>0||lifecycleLimited)notes.Add($"Fuel lifecycle: {inherited} contained pump/point sources inherit station closure; checked work {lifecycleWork}/{FuelStationLayout.MaximumWork}."+(lifecycleLimited?" Closure classification limit reached; new pump placement skipped.":""));
  if(sources.Count==0&&unsupported==0&&excess==0&&inactive==0&&!lifecycleLimited)return;
  if(inactive>0){
   bool InactivePump(WorldDecoration d)=>d.Id.Length>=38&&inactivePrefixes.Contains(d.Id[..38]);
   var removed=p.Decorations.RemoveAll(d=>InactivePump(d)&&FuelStationLayout.IsUnchangedGeneratedPump(d));
   var held=p.Decorations.Count(InactivePump);
   notes.Add($"Fuel sources skipped as disused/abandoned: {inactive}; {removed} unedited generated pumps removed; {held} authored/legacy pumps preserved. Preserved access/resources were not changed.");
  }
  // Omitting an explicit pump at the source cap must never turn it into an inferred station-centre pump.
  if(excess>0){notes.Add($"Fuel source bound ({FuelStationLayout.MaximumSources}) exceeded by {excess}; fuel placement skipped entirely, original source evidence retained.");if(inactive>0)notes.Add($"Fuel sources skipped as disused/abandoned: {inactive}.");return;}
  var plan=sources.Count>0&&!lifecycleLimited?FuelStationLayout.Plan(p,sources,cancellationToken:token):new FuelStationPlan([],0,0,0,false,[]);
  foreach(var layout in plan.Placements){
   // The planner proved these cells clear through ParcelPlacement. Generated paving suppresses native
   // vegetation in the usable forecourt/driveway; it never overwrites a road, manual surface or edit mask.
   foreach(var r in new[]{layout.Forecourt,layout.Connection})foreach(var (x,y) in FuelStationLayout.Cells(r)){var i=y*p.Width+x;if(p.Terrain[i]!=Terrain.Road)p.Terrain[i]=Terrain.Pavement;}
   p.Decorations.Add(layout.Pump);
  }
  notes.AddRange(plan.Notes);if(unsupported>0||excess>0)notes.Add($"Fuel sources unsupported (building, non-ground, restricted access or non-point geometry): {unsupported}; omitted at {FuelStationLayout.MaximumSources}-source bound: {excess}.");
 }
}
