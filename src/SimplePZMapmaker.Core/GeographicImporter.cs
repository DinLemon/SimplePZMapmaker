using System.Text.Json;
namespace SimplePZMapmaker.Core;
public sealed record ImportResult(MapProject Project,int ImportedFeatures,int SkippedBuildings,List<string> Warnings)
{
    public int SourceBuildings {get;init;}
    public int SourceFeatures {get;init;}
    public Dictionary<string,int> SkipReasons {get;init;}=[];
}
public static partial class GeographicImporter {
    private sealed record Feature(Dictionary<string,string> Tags,List<List<(double X,double Y)>> Rings,bool Polygon) { public List<List<(double X,double Y)>>? OriginalRings {get;init;} }
    public static ImportResult Import(string json,MapProject project,GeoBounds? bounds=null,CancellationToken cancellationToken=default,bool preserveEdits=false,GeographicProjection? projection=null)
    {
        ProjectValidator.Validate(project);
        if(string.IsNullOrWhiteSpace(json))throw new ArgumentException("The geographic data file is empty or missing.");
        cancellationToken.ThrowIfCancellationRequested();
        if(json.Length>16_000_000)throw new ArgumentException("The geographic data file exceeds 16 MB.");
        using var document=Parse(json); var root=document.RootElement;
        var features=ReadFeatures(root,out var omittedGeometry); if(features.Count==0)throw new ArgumentException(omittedGeometry>0?"The source contains only incomplete geometry. Import canceled; the original project was preserved.":"The file contains no supported points, lines or polygons.");
        var points=features.SelectMany(f=>f.Rings.SelectMany(r=>r)).ToArray();
        bounds ??=new(){West=points.Min(p=>p.X),East=points.Max(p=>p.X),South=points.Min(p=>p.Y),North=points.Max(p=>p.Y)};
        if(bounds.East-bounds.West<0.000001)bounds.East=bounds.West+0.000001;
        if(bounds.North-bounds.South<0.000001)bounds.North=bounds.South+0.000001;
        ProjectValidator.ValidateBounds(bounds);
        if(preserveEdits&&project.Bounds is {} previous&&(Math.Abs(previous.West-bounds.West)>1e-9||Math.Abs(previous.East-bounds.East)>1e-9||Math.Abs(previous.South-bounds.South)>1e-9||Math.Abs(previous.North-bounds.North)>1e-9))throw new ArgumentException("To preserve manual edits, keep the original import bounds. Import a different area without preserving edits.");
        var preserved=preserveEdits&&project.LinearFeatures.Count>0?LinearFeatureEditor.Rebuild(project,[]):project;
        var p=MapGenerator.Generate(new(){Size=project.Width,Seed=project.Seed,Kind="blank",Name=project.Name});
        if(project.Height!=project.Width) {p.Height=project.Height;p.Terrain=new Terrain[p.Width*p.Height];p.Zombies=new int[p.Width*p.Height/64];}
        p.WorldX=project.WorldX;p.WorldY=project.WorldY;p.Bounds=bounds;
        p.WorldSection=projection is not null||project.WorldSection;if(p.WorldSection)p.Version=Math.Max(p.Version,6);
        p.Attribution=[..project.Attribution];
        p.ResourcePalettes=NeighborhoodPlanner.Copy(project.ResourcePalettes);
        p.SurfaceTheme=project.SurfaceTheme;
        p.BuildingLibrary=JsonSerializer.Deserialize<List<Building>>(JsonSerializer.Serialize(project.BuildingLibrary))!;
        if(p.BuildingLibrary.Any(b=>b.RequiredMods.Count>0||b.AssetSources.Count>0||b.Design?.Floors.Any(f=>f.StaticModels.Count>0)==true))p.Version=Math.Max(p.Version,6);
        var source=root.TryGetProperty("elements",out _) ? "Map data © OpenStreetMap contributors — https://www.openstreetmap.org/copyright — ODbL 1.0":"Source: imported GeoJSON. Rights to the original data are determined by its provider.";
        if(!p.Attribution.Contains(source))p.Attribution.Add(source);
        var cos=Math.Cos((bounds.North+bounds.South)/2*Math.PI/180);
        var scale=Math.Min((p.Width-16)/((bounds.East-bounds.West)*cos),(p.Height-16)/(bounds.North-bounds.South));
        p.MetresPerTile=projection?.Reference.MetresPerTile??111320/scale;
        var left=(p.Width-(bounds.East-bounds.West)*cos*scale)/2;var top=(p.Height-(bounds.North-bounds.South)*scale)/2;
        (double X,double Y) Project((double X,double Y) v)=>projection?.Project(v)??(left+(v.X-bounds.West)*cos*scale,top+(bounds.North-v.Y)*scale);
        var transformed=features.Select(f=>f with{OriginalRings=f.Rings,Rings=f.Rings.Select(r=>r.Select(Project).ToList()).ToList()}).ToList();
        double work=0;
        foreach(var f in transformed.Where(f=>f.Polygon&&!f.Tags.ContainsKey("building")&&(f.Tags.ContainsKey("landuse")||f.Tags.ContainsKey("natural")))){var outer=f.Rings[0];var rows=Math.Clamp(outer.Max(v=>v.Y)-outer.Min(v=>v.Y)+2,0,p.Height);var columns=Math.Clamp(outer.Max(v=>v.X)-outer.Min(v=>v.X)+2,0,p.Width);work+=rows*(columns+f.Rings.Sum(r=>r.Count));}
        if(work>40_000_000)throw new ArgumentException("The outlines are too complex for this map. Reduce the area or map size, or simplify the GeoJSON.");
        var imported=0;var skipped=0;var limitedLevels=0;var approximateRoofs=0;var skipReasons=new Dictionary<string,int>();var skippedOutlines=new List<SkippedImportOutline>();
        foreach(var f in transformed.Where(f=>f.Polygon&&!f.Tags.ContainsKey("building")&&(f.Tags.ContainsKey("landuse")||f.Tags.ContainsKey("natural")))) {
            cancellationToken.ThrowIfCancellationRequested();
            var natural=f.Tags.GetValueOrDefault("natural");var land=f.Tags.GetValueOrDefault("landuse");
            var terrain=natural is "water" or "wetland" || land=="reservoir" ? Terrain.Water : natural=="wood"||land=="forest" ? Terrain.Forest : natural is "beach" or "sand" ? Terrain.Sand : Terrain.Grass;
            FillPolygon(p,f.Rings,terrain,cancellationToken);imported++;
        }
        var editableLines=new List<LinearFeature>();var staticLines=0;var omittedRails=0;var simplifiedLines=0;var lineBudget=0;
        p.Spawns=[];
        foreach(var f in transformed.Where(f=>!f.Polygon || f.Tags.ContainsKey("highway"))) {
            cancellationToken.ThrowIfCancellationRequested();
            var water=f.Tags.ContainsKey("waterway");var rail=f.Tags.ContainsKey("railway");if(!water&&!rail&&!f.Tags.ContainsKey("highway"))continue;
            var highway=f.Tags.GetValueOrDefault("highway");var bridge=f.Tags.TryGetValue("bridge",out var bridgeTag)&&bridgeTag is not("no" or "0" or "false");
            var kind=water?"river":rail?"railway":bridge?"bridge":highway is "footway" or "path" or "cycleway" or "pedestrian"?"sidewalk":"road";
            double widthMetres=water?8:rail?3:highway is "motorway" or "trunk"?16:highway is "primary" or "secondary"?10:kind=="sidewalk"?2:6;
            if(f.Tags.TryGetValue("width",out var widthText)&&double.TryParse(widthText.Replace(" m",""),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var specifiedWidth)&&specifiedWidth>0&&specifiedWidth<=60)widthMetres=specifiedWidth;
            var tileWidth=Math.Clamp(widthMetres/p.MetresPerTile.Value,1,48);var radius=Math.Clamp((int)Math.Round(tileWidth/2),0,24);
            foreach(var (ring,index) in f.Rings.Select((r,i)=>(r,i))){
                var sourceId=f.Tags.GetValueOrDefault("__pz_source");if(preserveEdits&&sourceId is not null&&project.LinearFeatures.Any(l=>l.Modified&&l.SourceId==sourceId))continue;
                var line=new LinearFeature{Id="geo-line-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((sourceId??string.Join(";",ring.Select(v=>v.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+v.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture))))+"/"+index))).ToLowerInvariant()[..24],Kind=kind,Width=rail?3:tileWidth,SourceId=sourceId,SourceTags=f.Tags.Where(v=>v.Key.Length<=100).Take(47).ToDictionary(v=>v.Key,v=>v.Value[..Math.Min(300,v.Value.Length)]),Points=ring.Select(v=>new LinearPoint(v.X,v.Y)).ToList()};
                var sourceRing=f.OriginalRings![index];line.SourcePath=sourceRing.Count<=512?sourceRing.Select(v=>new SourceOutlinePoint(v.X,v.Y)).ToList():Enumerable.Range(0,512).Select(i=>sourceRing[(int)((long)i*(sourceRing.Count-1)/511)]).Select(v=>new SourceOutlinePoint(v.X,v.Y)).ToList();
                if(line.Points.Count>512){line.Points=Enumerable.Range(0,512).Select(i=>line.Points[(int)((long)i*(line.Points.Count-1)/511)]).ToList();line.SourceTags["pzmap:geometry"]="simplified to 512 source samples";simplifiedLines++;}
                bool eligible=editableLines.Count<64-(preserveEdits?project.LinearFeatures.Count(l=>l.Modified):0);try{LinearFeatureEditor.ValidateFeature(line);if(eligible){var cells=LinearFeatureEditor.Rasterize(line,p.Width,p.Height);eligible=cells.Count>0&&lineBudget+cells.Count<=100000;if(eligible)lineBudget+=cells.Count;}}catch(ArgumentException){eligible=false;}
                if(eligible)editableLines.Add(line);else if(rail)omittedRails++;else{for(var i=1;i<ring.Count;i++)DrawLine(p,ring[i-1],ring[i],radius,water?Terrain.Water:kind=="sidewalk"?Terrain.Pavement:Terrain.Road);staticLines++;}
            }
            imported++;
        }

        if(preserveEdits){p.Generation=NeighborhoodPlanner.Copy(project.Generation);p.GameZones=NeighborhoodPlanner.Copy(project.GameZones);p.Version=Math.Max(p.Version,project.Version);p.TerrainEdits=project.TerrainEdits;var mask=TerrainEditMask.Decode(project);for(var i=0;i<p.Terrain.Length;i++)if(TerrainEditMask.IsEdited(mask,i))p.Terrain[i]=preserved.Terrain[i];p.RequiredMods=[..project.RequiredMods];p.AssetSources=new(project.AssetSources);p.StaticModels=JsonSerializer.Deserialize<List<StaticModelPlacement>>(JsonSerializer.Serialize(project.StaticModels))!;p.Zombies=(int[])project.Zombies.Clone();p.SurfaceRegions=JsonSerializer.Deserialize<List<SurfaceRegion>>(JsonSerializer.Serialize(project.SurfaceRegions))!;if(p.SurfaceRegions.Count>0)p.Version=Math.Max(p.Version,5);p.SurfaceTiles=new(preserved.SurfaceTiles);p.Decorations=JsonSerializer.Deserialize<List<WorldDecoration>>(JsonSerializer.Serialize(preserved.Decorations))!;foreach(var i in p.SurfaceTiles.Keys)p.Terrain[i]=preserved.Terrain[i];foreach(var model in p.StaticModels)foreach(var cell in StaticModelPlacement.FootprintCells(model)){var i=(model.Y+cell.Dy)*p.Width+model.X+cell.Dx;p.Terrain[i]=preserved.Terrain[i];}foreach(var d in p.Decorations)foreach(var part in d.Parts){var i=(d.Y+part.Dy)*p.Width+d.X+part.Dx;p.Terrain[i]=preserved.Terrain[i];}}
        if(p.BuildingLibrary.Any(b=>b.RequiredMods.Count>0||b.AssetSources.Count>0||b.Design?.Floors.Any(f=>f.StaticModels.Count>0)==true)){p.Version=Math.Max(p.Version,6);p.RequiredMods=p.RequiredMods.Concat(p.BuildingLibrary.SelectMany(b=>b.RequiredMods)).Distinct().ToList();}
        if(preserveEdits)foreach(var old in project.Buildings.Where(b=>b.Modified||project.Generation?.LockedBuildingIds.Contains(b.Id)==true||project.Sites.Any(s=>s.Locked&&s.BuildingIds.Contains(b.Id)))){
            var kept=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(old))!;
            foreach(var index in BuildingDesigner.Occupied(kept,false)){var px=kept.X+index%(kept.Width+1);var py=kept.Y+index/(kept.Width+1);p.Terrain[py*p.Width+px]=preserved.Terrain[py*project.Width+px];}
            if(!MapGenerator.CanPlace(p,kept,false))throw new ArgumentException("Manual buildings conflict after the geographic update. Import canceled; the original project was preserved.");p.Buildings.Add(kept);
        }
        var plannedLines=editableLines.Concat(preserveEdits?project.LinearFeatures.Where(f=>f.Modified).Select(NeighborhoodPlanner.Copy):[]).ToList();
        foreach(var line in plannedLines){line.Underlay=[];line.GeneratedDecorations=[];line.DecorationIds=[];}
        if(plannedLines.Count>0)p=LinearFeatureEditor.Rebuild(p,plannedLines);
        foreach(var f in transformed.Where(f=>f.Polygon&&f.Tags.ContainsKey("building"))) {
            cancellationToken.ThrowIfCancellationRequested();
            if(preserveEdits&&f.Tags.TryGetValue("__pz_source",out var sourceKey)&&p.Buildings.Any(b=>b.SourceId==sourceKey)){imported++;continue;}
            var ring=f.Rings[0]; var x=(int)Math.Floor(Math.Clamp(ring.Min(v=>v.X),-100000,100000));var y=(int)Math.Floor(Math.Clamp(ring.Min(v=>v.Y),-100000,100000));
            var w=(int)Math.Clamp(Math.Ceiling(ring.Max(v=>v.X)-x),0,100000);var h=(int)Math.Clamp(Math.Ceiling(ring.Max(v=>v.Y)-y),0,100000);
            var sourceKind=f.Tags.GetValueOrDefault("building","yes");
            var kind=f.Tags.ContainsKey("shop")?"shop":sourceKind switch{"retail" or "commercial" or "train_station"=>"shop","apartments" or "residential"=>"apartments","school" or "college" or "kindergarten"=>"school","hospital"=>"clinic","hotel"=>"hotel","industrial"=>"industrial","warehouse" or "shed" or "service"=>"warehouse","garages" or "garage"=>"garage",_=>"house"};
            var b=new Building{Id="import-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(f.Tags["__pz_source"])))[..24].ToLowerInvariant(),X=x,Y=y,Width=Math.Max(6,w),Height=Math.Max(6,h),Kind=kind,Name=f.Tags.GetValueOrDefault("name",BuildingDesigner.Names[kind]),SourceKind=sourceKind,SourceId=f.Tags.GetValueOrDefault("__pz_source")};
            b.Name=b.Name[..Math.Min(100,b.Name.Length)];
            b.SourceLevels=f.Tags.GetValueOrDefault("building:levels")?[..Math.Min(100,f.Tags["building:levels"].Length)];b.SourceRoof=f.Tags.GetValueOrDefault("roof:shape")?[..Math.Min(100,f.Tags["roof:shape"].Length)];
            if(w<=64&&h<=64){
                var levels=kind is "apartments" or "school" or "clinic" or "hotel"?2:1;
                if(f.Tags.TryGetValue("building:levels",out var levelText)&&int.TryParse(levelText,out var specified)&&specified>0){levels=Math.Clamp(specified,1,ZLevels.AboveGroundCount);b.LevelsOrigin="osm-levels";if(specified>ZLevels.AboveGroundCount)limitedLevels++;}
                else if(f.Tags.TryGetValue("height",out var heightText)&&double.TryParse(heightText.Replace(" m",""),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var height)&&height>0&&double.IsFinite(height)){levels=Math.Clamp((int)Math.Min(ZLevels.AboveGroundCount,Math.Round(height/3)),1,ZLevels.AboveGroundCount);b.LevelsOrigin="osm-height";}
                var basements=0;b.SourceBasements=f.Tags.GetValueOrDefault("building:levels:underground")?[..Math.Min(100,f.Tags["building:levels:underground"].Length)];
                if(b.SourceBasements is not null&&int.TryParse(b.SourceBasements,out var depth)&&depth>0){basements=Math.Clamp(depth,0,ZLevels.BasementCount);if(depth>ZLevels.BasementCount)limitedLevels++;}
                var seed=unchecked(project.Seed^x*73856093^y*19349663);b.Design=BuildingDesigner.Generate(b,levels,seed,"rect",basements);
                if(p.SurfaceTheme!="legacy")VisualStyles.Apply(b);
                if(f.Tags.GetValueOrDefault("roof:shape") is "flat" or "gabled" or "shed" or "skillion" or "hipped")b.Design.Roof=f.Tags["roof:shape"]=="skillion"?"shed":f.Tags["roof:shape"];
                else if(b.SourceRoof is not null){b.Design.Roof=b.SourceRoof=="pyramidal"?"hipped":b.SourceRoof=="saltbox"?"gabled":"flat";approximateRoofs++;}
                if(RoofGeometry.SelectSafeAutomaticRoof(b))approximateRoofs++;
                if(!ImportedBuildingGeometry.Apply(b,f.Rings,out var footprintReason)){skipped++;skipReasons[footprintReason]=skipReasons.GetValueOrDefault(footprintReason)+1;if(skippedOutlines.Count<256)skippedOutlines.Add(new(b.SourceId,b.Name,footprintReason,x,y,w,h));continue;}
                b.SourceOutline=ImportedBuildingGeometry.Capture(f.OriginalRings!,out var sourceSimplified);b.SourceOutlineSimplified=sourceSimplified;
            }
            if(w>64||h>64||!MapGenerator.CanPlace(p,b)){
                skipped++;
                var reason=w>64||h>64?"Outline too large":b.X<1||b.Y<1||b.X+b.Width>=p.Width||b.Y+b.Height>=p.Height?"Outside the map":b.X/256!=(b.X+b.Width)/256||b.Y/256!=(b.Y+b.Height)/256?"Cell boundary":p.Buildings.Any(a=>b.X<a.X+a.Width+1&&b.X+b.Width+1>a.X&&b.Y<a.Y+a.Height+1&&b.Y+b.Height+1>a.Y)?"Building overlap":"Water or road";
                skipReasons[reason]=skipReasons.GetValueOrDefault(reason)+1;if(skippedOutlines.Count<256)skippedOutlines.Add(new(b.SourceId,b.Name,reason,x,y,w,h));continue;
            }
            p.Buildings.Add(b);imported++;
        }
        if(preserveEdits){p.Spawns=project.Spawns.Select(s=>new SpawnPoint{X=s.X,Y=s.Y}).ToList();foreach(var spawn in p.Spawns)p.Terrain[spawn.Y*p.Width+spawn.X]=preserved.Terrain[spawn.Y*p.Width+spawn.X];}else{try{p.Spawns=[MapGenerator.FindSafeSpawn(p)];}catch(ArgumentException) when(p.WorldSection){p.Spawns=[];}}
        CaptureEvidence(p,transformed,project,preserveEdits,cancellationToken);
        p.ImportSummary=new(){SourceFeatures=features.Count,SourceBuildings=transformed.Count(f=>f.Polygon&&f.Tags.ContainsKey("building")),PlacedBuildings=p.Buildings.Count,SkipReasons=new(skipReasons),Quality=ImportQuality.Summarize(p.Buildings,omittedGeometry),SkippedOutlines=skippedOutlines};
        p.ImportSummary.Notes.Add($"Editable source paths: {editableLines.Count}; raster-only paths retained because of authoring limits: {staticLines}. Imported path widths are estimated from source tags or road classes.");
        p.ImportSummary.Notes.Add("Building outlines preserve concavity and courtyard holes through tile-center resampling; rooms, stairs and furniture are generated, not surveyed. Original geographic rings are retained separately.");
        if(omittedRails>0)p.ImportSummary.Notes.Add($"Railway paths skipped because cardinal native artwork or authoring limits were not supported: {omittedRails}. No train simulation is provided.");
        if(simplifiedLines>0)p.ImportSummary.Notes.Add($"Long source paths simplified to 512 samples: {simplifiedLines}.");
        p.ImportSummary.Notes.Add($"OSM floor counts: {p.Buildings.Count(b=>b.LevelsOrigin=="osm-levels")}; estimated from height: {p.Buildings.Count(b=>b.LevelsOrigin=="osm-height")}; assumed: {p.Buildings.Count(b=>b.LevelsOrigin=="inferred")}.");
        if(limitedLevels>0)p.ImportSummary.Notes.Add($"Above-ground and basement levels were limited to 32 for {limitedLevels} source outlines.");if(approximateRoofs>0)p.ImportSummary.Notes.Add($"Unusual roof shapes were adapted for {approximateRoofs} outlines; original tags were preserved in the building properties.");
        if(preserveEdits)p.ImportSummary.Notes.Add($"Manually edited buildings preserved: {p.Buildings.Count(b=>b.Modified)}. Adjacent source outlines may be skipped to preserve these edits.");
        if(preserveEdits&&project.TerrainEdits.Length==0)p.ImportSummary.Notes.Add("Legacy manual terrain changes without a mask cannot be identified. Explicit surfaces, decoration, and buildings were preserved; unmarked terrain was refreshed from geographic data.");
        if(!preserveEdits)foreach(var b in p.Buildings)for(var y=b.Y/8;y<=(b.Y+b.Height)/8;y++)for(var x=b.X/8;x<=(b.X+b.Width)/8;x++)p.Zombies[y*(p.Width/8)+x]=90;
        ImportQuality.Validate(p.ImportSummary);ProjectValidator.Validate(p);
        var warnings=new List<string>();if(skipped>0)warnings.Add($"Buildings skipped: {skipped}. "+string.Join("; ",skipReasons.Select(r=>$"{r.Key}: {r.Value}"))+". Outlines adapted to the grid; interiors generated.");if(omittedGeometry>0){var warning=$"Features with missing points or unclosed OSM relations skipped: {omittedGeometry}. Partial outlines were not used.";warnings.Add(warning);p.ImportSummary.Notes.Add(warning);}return new(p,imported,skipped,warnings){SourceBuildings=transformed.Count(f=>f.Polygon&&f.Tags.ContainsKey("building")),SourceFeatures=features.Count,SkipReasons=skipReasons};
    }
    private static JsonDocument Parse(string json){try{return JsonDocument.Parse(json,new(){MaxDepth=48});}catch(JsonException){throw new ArgumentException("Could not read the geographic JSON data.");}}
    private static List<Feature> ReadFeatures(JsonElement root,out int omittedGeometry)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw new ArgumentException("A GeoJSON object or Overpass response is required.");
        var result=new List<Feature>();var pointCount=0;var omitted=0;
        (double,double) Point(double x,double y){if(!double.IsFinite(x)||!double.IsFinite(y)||x is < -180 or >180||y is < -85 or >85||++pointCount>250000)throw new ArgumentException("Invalid coordinates or more than 250,000 points.");return(x,y);}
        List<(double,double)> GeoRing(JsonElement ring) {
            if(ring.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Invalid GeoJSON coordinates.");
            return ring.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.Array&&v.GetArrayLength()>=2&&v[0].TryGetDouble(out var x)&&v[1].TryGetDouble(out var y)?Point(x,y):throw new ArgumentException("Invalid GeoJSON point.")).ToList();
        }
        Dictionary<string,string> Tags(JsonElement el,string key){var tags=new Dictionary<string,string>();if(el.TryGetProperty(key,out var props)&&props.ValueKind==JsonValueKind.Object)foreach(var pair in props.EnumerateObject())tags[pair.Name]=pair.Value.ToString();return tags;}
        List<(double X,double Y)>? OsmRing(JsonElement geometry){if(geometry.ValueKind!=JsonValueKind.Array)return null;var ring=new List<(double,double)>();foreach(var point in geometry.EnumerateArray()){if(point.ValueKind!=JsonValueKind.Object||!point.TryGetProperty("lon",out var lon)||!point.TryGetProperty("lat",out var lat)||!lon.TryGetDouble(out var x)||!lat.TryGetDouble(out var y))return null;ring.Add(Point(x,y));}return ring;}
        void Add(Dictionary<string,string> tags,List<List<(double,double)>> rings,bool polygon,bool point=false) {
            if(rings.Count==0||rings.Any(r=>r.Count<(point?1:polygon?4:2)||polygon&&r[0]!=r[^1])){omitted++;return;}
            if(result.Count>=30000)throw new ArgumentException("Too many objects: the limit is 30,000.");
            tags=new(tags);if(!tags.ContainsKey("__pz_source"))tags["__pz_source"]="geo-sha-"+SourceGeometryHash(rings,GeographicEvidence.CaptureTags(tags));
            result.Add(new(tags,rings,polygon));
        }
        if(root.TryGetProperty("elements",out var elements)&&elements.ValueKind==JsonValueKind.Array) {
            var relationMembers=new HashSet<long>();
            foreach(var relation in elements.EnumerateArray().Where(e=>e.TryGetProperty("members",out _)))foreach(var member in relation.GetProperty("members").EnumerateArray())if(member.TryGetProperty("ref",out var reference)&&reference.TryGetInt64(out var id))relationMembers.Add(id);
            foreach(var el in elements.EnumerateArray()) {
                if(el.TryGetProperty("id",out var elementId)&&elementId.TryGetInt64(out var wayId)&&relationMembers.Contains(wayId)&&el.TryGetProperty("type",out var elementType)&&elementType.GetString()=="way")continue;
                var tags=Tags(el,"tags");if(el.TryGetProperty("id",out var sourceId)&&sourceId.TryGetInt64(out var sourceNumber))tags["__pz_source"]=$"osm-{(el.TryGetProperty("type",out var sourceType)?sourceType.GetString():"way")}-{sourceNumber}";
                if(el.TryGetProperty("type",out var nodeType)&&nodeType.GetString()=="node"&&el.TryGetProperty("lon",out var nodeLon)&&el.TryGetProperty("lat",out var nodeLat)){Add(tags,[[Point(nodeLon.GetDouble(),nodeLat.GetDouble())]],false,true);}
                else if(el.TryGetProperty("geometry",out var geometry)&&geometry.ValueKind==JsonValueKind.Array) {
                    var ring=OsmRing(geometry);var polygon=tags.ContainsKey("building")||tags.ContainsKey("landuse")||tags.ContainsKey("site")||tags.ContainsKey("amenity")||tags.ContainsKey("leisure")||tags.ContainsKey("healthcare")||tags.GetValueOrDefault("natural") is "water" or "wood" or "sand" or "beach" or "wetland";if(ring is null||ring.Count<(polygon?4:2)||polygon&&ring[0]!=ring[^1]){omitted++;continue;}Add(tags,[ring],polygon);
                } else if(el.TryGetProperty("members",out var members)&&members.ValueKind==JsonValueKind.Array) {
                    var outerSegments=new List<List<(double X,double Y)>>();var innerSegments=new List<List<(double X,double Y)>>();
                    var complete=true;foreach(var member in members.EnumerateArray())if(member.TryGetProperty("geometry",out var memberGeometry)&&memberGeometry.ValueKind==JsonValueKind.Array){
                        var segment=OsmRing(memberGeometry);if(segment is null||segment.Count<2){complete=false;break;}
                        (member.TryGetProperty("role",out var role)&&role.GetString()=="inner"?innerSegments:outerSegments).Add(segment);
                    }else if(member.TryGetProperty("type",out var memberType)&&memberType.GetString() is "way" or "relation"){complete=false;break;}
                    var outers=Stitch(outerSegments,out var outerComplete);var inners=Stitch(innerSegments,out var innerComplete);if(!complete||!outerComplete||!innerComplete||outers.Count==0){omitted++;continue;}
                    var part=0;foreach(var outer in outers){var rings=new List<List<(double,double)>>{outer};foreach(var inner in inners)if(Inside(inner[0].X,inner[0].Y,outer))rings.Add(inner);var partTags=new Dictionary<string,string>(tags);if(outers.Count>1&&partTags.ContainsKey("__pz_source"))partTags["__pz_source"]+=$"-part-{part}";part++;Add(partTags,rings,true);}
                }
            }
        }else if(root.TryGetProperty("features",out var features)&&features.ValueKind==JsonValueKind.Array) {
            foreach(var feature in features.EnumerateArray()) {
                if(!feature.TryGetProperty("geometry",out var geometry)||geometry.ValueKind!=JsonValueKind.Object||!geometry.TryGetProperty("coordinates",out var coords))continue;
                var tags=Tags(feature,"properties");if(feature.TryGetProperty("id",out var sourceId)&&sourceId.ValueKind is JsonValueKind.String or JsonValueKind.Number){var text=sourceId.ToString();tags["__pz_source"]="geo-"+(text.Length>75?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant():text);}var type=geometry.GetProperty("type").GetString();
                if(type=="Point")Add(tags,[[Point(coords[0].GetDouble(),coords[1].GetDouble())]],false,true);
                else if(type=="LineString")Add(tags,[GeoRing(coords)],false);
                else if(type=="Polygon")Add(tags,coords.EnumerateArray().Select(GeoRing).ToList(),true);
                else if(type=="MultiPolygon"){var part=0;foreach(var polygon in coords.EnumerateArray()){var partTags=new Dictionary<string,string>(tags);if(partTags.ContainsKey("__pz_source"))partTags["__pz_source"]+=$"-part-{part}";part++;Add(partTags,polygon.EnumerateArray().Select(GeoRing).ToList(),true);}}
                else if(type=="MultiLineString")foreach(var line in coords.EnumerateArray())Add(tags,[GeoRing(line)],false);
            }
        } else throw new ArgumentException("GeoJSON FeatureCollection and Overpass JSON are supported.");
        omittedGeometry=omitted;return result;
    }
    private static List<List<(double X,double Y)>> Stitch(List<List<(double X,double Y)>> segments,out bool complete)
    {
        complete=true;var endpoints=new Dictionary<(double,double),List<int>>();var used=new bool[segments.Count];
        void Link((double,double) point,int index){if(!endpoints.TryGetValue(point,out var list))endpoints[point]=list=[];list.Add(index);}
        for(var i=0;i<segments.Count;i++){Link(segments[i][0],i);Link(segments[i][^1],i);}
        var result=new List<List<(double,double)>>();
        for(var i=0;i<segments.Count;i++){if(used[i])continue;used[i]=true;var ring=new List<(double,double)>(segments[i]);
            while(ring[^1]!=ring[0]){
                if(!endpoints.TryGetValue(ring[^1],out var candidates))break;var next=candidates.FindIndex(j=>!used[j]);if(next<0)break;var index=candidates[next];used[index]=true;var segment=segments[index];
                if(segment[0]==ring[^1])ring.AddRange(segment.Skip(1));else for(var j=segment.Count-2;j>=0;j--)ring.Add(segment[j]);
            }
            if(ring.Count>=4&&ring[0]==ring[^1])result.Add(ring);else complete=false;
        }
        return result;
    }
    private static void FillPolygon(MapProject p,List<List<(double X,double Y)>> rings,Terrain terrain,CancellationToken token)
    {
        var ring=rings[0];
        var minY=(int)Math.Clamp(Math.Floor(ring.Min(v=>v.Y)),0,p.Height-1);var maxY=(int)Math.Clamp(Math.Ceiling(ring.Max(v=>v.Y)),0,p.Height-1);
        var intersections=new List<double>();
        for(var y=minY;y<=maxY;y++) {
            token.ThrowIfCancellationRequested();intersections.Clear();var scan=y+0.5;
            foreach(var contour in rings)for(int i=0,j=contour.Count-1;i<contour.Count;j=i++)if((contour[i].Y>scan)!=(contour[j].Y>scan))intersections.Add(contour[i].X+(scan-contour[i].Y)*(contour[j].X-contour[i].X)/(contour[j].Y-contour[i].Y));
            intersections.Sort();
            for(var i=0;i+1<intersections.Count;i+=2){var first=(int)Math.Clamp(Math.Ceiling(intersections[i]-.5),0,p.Width);var end=(int)Math.Clamp(Math.Ceiling(intersections[i+1]-.5),0,p.Width);for(var x=first;x<end;x++)p.Terrain[y*p.Width+x]=terrain;}
        }
    }
    private static bool Inside(double x,double y,List<(double X,double Y)> ring) {
        var inside=false;for(int i=0,j=ring.Count-1;i<ring.Count;j=i++)if((ring[i].Y>y)!=(ring[j].Y>y)&&x<(ring[j].X-ring[i].X)*(y-ring[i].Y)/(ring[j].Y-ring[i].Y)+ring[i].X)inside=!inside;return inside;
    }
    private static void DrawLine(MapProject p,(double X,double Y) a,(double X,double Y) b,int r,Terrain terrain)
    {
        var dx=b.X-a.X;var dy=b.Y-a.Y;var enter=0.0;var exit=1.0;
        bool Clip(double direction,double distance){if(direction==0)return distance>=0;var t=distance/direction;if(direction<0){if(t>exit)return false;enter=Math.Max(enter,t);}else{if(t<enter)return false;exit=Math.Min(exit,t);}return true;}
        if(!Clip(-dx,a.X+r)||!Clip(dx,p.Width+r-a.X)||!Clip(-dy,a.Y+r)||!Clip(dy,p.Height+r-a.Y))return;
        var x0=a.X+enter*dx;var y0=a.Y+enter*dy;var x1=a.X+exit*dx;var y1=a.Y+exit*dy;
        var steps=(int)Math.Ceiling(Math.Max(Math.Abs(x1-x0),Math.Abs(y1-y0)));
        for(var i=0;i<=steps;i++){var t=steps==0?0:i/(double)steps;var x=(int)Math.Round(x0+(x1-x0)*t);var y=(int)Math.Round(y0+(y1-y0)*t);
            for(var yy=Math.Max(0,y-r);yy<=Math.Min(p.Height-1,y+r);yy++)for(var xx=Math.Max(0,x-r);xx<=Math.Min(p.Width-1,x+r);xx++)p.Terrain[yy*p.Width+xx]=terrain;
        }
    }
}
