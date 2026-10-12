using System.Text.Json;
namespace SimplePZMapmaker.Core;
public sealed record ImportResult(MapProject Project,int ImportedFeatures,int SkippedBuildings,List<string> Warnings)
{
    public int SourceBuildings {get;init;}
    public int SourceFeatures {get;init;}
    public Dictionary<string,int> SkipReasons {get;init;}=[];
}
public static class GeographicImporter {
    private sealed record Feature(Dictionary<string,string> Tags,List<List<(double X,double Y)>> Rings,bool Polygon);
    public static ImportResult Import(string json,MapProject project,GeoBounds? bounds=null,CancellationToken cancellationToken=default,bool preserveEdits=false)
    {
        ProjectValidator.Validate(project);
        if(string.IsNullOrWhiteSpace(json))throw new ArgumentException("Файл геоданных пуст или не задан.");
        cancellationToken.ThrowIfCancellationRequested();
        if(json.Length>16_000_000)throw new ArgumentException("Файл геоданных превышает 16 МБ.");
        using var document=Parse(json); var root=document.RootElement;
        var features=ReadFeatures(root); if(features.Count==0)throw new ArgumentException("В файле нет поддерживаемых линий или полигонов.");
        var points=features.SelectMany(f=>f.Rings.SelectMany(r=>r)).ToArray();
        bounds ??=new(){West=points.Min(p=>p.X),East=points.Max(p=>p.X),South=points.Min(p=>p.Y),North=points.Max(p=>p.Y)};
        if(bounds.East-bounds.West<0.000001)bounds.East=bounds.West+0.000001;
        if(bounds.North-bounds.South<0.000001)bounds.North=bounds.South+0.000001;
        ProjectValidator.ValidateBounds(bounds);
        var p=MapGenerator.Generate(new(){Size=project.Width,Seed=project.Seed,Kind="blank",Name=project.Name});
        if(project.Height!=project.Width) {p.Height=project.Height;p.Terrain=new Terrain[p.Width*p.Height];p.Zombies=new int[p.Width*p.Height/64];}
        p.WorldX=project.WorldX;p.WorldY=project.WorldY;p.Bounds=bounds;
        p.Attribution=[..project.Attribution];
        p.SurfaceTheme=project.SurfaceTheme;
        p.BuildingLibrary=JsonSerializer.Deserialize<List<Building>>(JsonSerializer.Serialize(project.BuildingLibrary))!;
        var source=root.TryGetProperty("elements",out _) ? "Map data © OpenStreetMap contributors — https://www.openstreetmap.org/copyright — ODbL 1.0":"Источник: импортированный GeoJSON. Права на исходные данные определяет их поставщик.";
        if(!p.Attribution.Contains(source))p.Attribution.Add(source);
        var cos=Math.Cos((bounds.North+bounds.South)/2*Math.PI/180);
        var scale=Math.Min((p.Width-16)/((bounds.East-bounds.West)*cos),(p.Height-16)/(bounds.North-bounds.South));
        p.MetresPerTile=111320/scale;
        var left=(p.Width-(bounds.East-bounds.West)*cos*scale)/2;var top=(p.Height-(bounds.North-bounds.South)*scale)/2;
        (double X,double Y) Project((double X,double Y) v)=>(left+(v.X-bounds.West)*cos*scale,top+(bounds.North-v.Y)*scale);
        var transformed=features.Select(f=>f with{Rings=f.Rings.Select(r=>r.Select(Project).ToList()).ToList()}).ToList();
        double work=0;
        foreach(var f in transformed.Where(f=>f.Polygon&&!f.Tags.ContainsKey("building"))){var outer=f.Rings[0];var rows=Math.Clamp(outer.Max(v=>v.Y)-outer.Min(v=>v.Y)+2,0,p.Height);var columns=Math.Clamp(outer.Max(v=>v.X)-outer.Min(v=>v.X)+2,0,p.Width);work+=rows*(columns+f.Rings.Sum(r=>r.Count));}
        if(work>40_000_000)throw new ArgumentException("Контуры слишком сложные для выбранной карты. Уменьшите участок, размер карты или упростите GeoJSON.");
        var imported=0;var skipped=0;var limitedLevels=0;var approximateRoofs=0;var skipReasons=new Dictionary<string,int>();
        foreach(var f in transformed.Where(f=>f.Polygon&&!f.Tags.ContainsKey("building"))) {
            cancellationToken.ThrowIfCancellationRequested();
            var natural=f.Tags.GetValueOrDefault("natural");var land=f.Tags.GetValueOrDefault("landuse");
            var terrain=natural is "water" or "wetland" || land=="reservoir" ? Terrain.Water : natural=="wood"||land=="forest" ? Terrain.Forest : natural is "beach" or "sand" ? Terrain.Sand : Terrain.Grass;
            FillPolygon(p,f.Rings,terrain,cancellationToken);imported++;
        }
        foreach(var f in transformed.Where(f=>!f.Polygon || f.Tags.ContainsKey("highway"))) {
            cancellationToken.ThrowIfCancellationRequested();
            var water=f.Tags.ContainsKey("waterway"); if(!water&&!f.Tags.ContainsKey("highway"))continue;
            var highway=f.Tags.GetValueOrDefault("highway");
            double widthMetres=water?8:highway is "motorway" or "trunk"?16:highway is "primary" or "secondary"?10:highway is "footway" or "path" or "cycleway"?2:6;
            if(f.Tags.TryGetValue("width",out var widthText)&&double.TryParse(widthText.Replace(" m",""),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var specifiedWidth)&&specifiedWidth>0&&specifiedWidth<=60)widthMetres=specifiedWidth;
            var radius=Math.Clamp((int)Math.Round(widthMetres/p.MetresPerTile.Value/2),0,24);
            foreach(var ring in f.Rings)for(var i=1;i<ring.Count;i++) DrawLine(p,ring[i-1],ring[i],radius,water?Terrain.Water:Terrain.Road);
            imported++;
        }
        if(preserveEdits){p.SurfaceTiles=new(project.SurfaceTiles);p.Decorations=JsonSerializer.Deserialize<List<WorldDecoration>>(JsonSerializer.Serialize(project.Decorations))!;foreach(var i in p.SurfaceTiles.Keys)p.Terrain[i]=project.Terrain[i];foreach(var d in p.Decorations)foreach(var part in d.Parts){var i=(d.Y+part.Dy)*p.Width+d.X+part.Dx;p.Terrain[i]=project.Terrain[i];}}
        if(preserveEdits)foreach(var old in project.Buildings.Where(b=>b.Modified)){
            var kept=JsonSerializer.Deserialize<Building>(JsonSerializer.Serialize(old))!;
            foreach(var index in BuildingDesigner.Occupied(kept,false)){var px=kept.X+index%(kept.Width+1);var py=kept.Y+index/(kept.Width+1);p.Terrain[py*p.Width+px]=project.Terrain[py*project.Width+px];}
            if(!MapGenerator.CanPlace(p,kept,false))throw new ArgumentException("Ручные здания конфликтуют после обновления географии. Импорт отменён; исходный проект сохранён.");p.Buildings.Add(kept);
        }
        foreach(var f in transformed.Where(f=>f.Polygon&&f.Tags.ContainsKey("building"))) {
            cancellationToken.ThrowIfCancellationRequested();
            if(preserveEdits&&f.Tags.TryGetValue("__pz_source",out var sourceKey)&&p.Buildings.Any(b=>b.Modified&&b.SourceId==sourceKey)){imported++;continue;}
            var ring=f.Rings[0]; var x=(int)Math.Floor(Math.Clamp(ring.Min(v=>v.X),-100000,100000));var y=(int)Math.Floor(Math.Clamp(ring.Min(v=>v.Y),-100000,100000));
            var w=(int)Math.Clamp(Math.Ceiling(ring.Max(v=>v.X)-x),0,100000);var h=(int)Math.Clamp(Math.Ceiling(ring.Max(v=>v.Y)-y),0,100000);
            var sourceKind=f.Tags.GetValueOrDefault("building","yes");
            var kind=f.Tags.ContainsKey("shop")?"shop":sourceKind switch{"retail" or "commercial" or "train_station"=>"shop","apartments" or "residential"=>"apartments","school" or "college" or "kindergarten"=>"school","hospital"=>"clinic","hotel"=>"hotel","industrial"=>"industrial","warehouse" or "shed" or "service"=>"warehouse","garages" or "garage"=>"garage",_=>"house"};
            var b=new Building{Id=$"import-{x}-{y}-{p.Buildings.Count}",X=x,Y=y,Width=Math.Max(6,w),Height=Math.Max(6,h),Kind=kind,Name=f.Tags.GetValueOrDefault("name",BuildingDesigner.Names[kind]),SourceKind=sourceKind,SourceId=f.Tags.GetValueOrDefault("__pz_source")};
            b.Name=b.Name[..Math.Min(100,b.Name.Length)];
            b.SourceLevels=f.Tags.GetValueOrDefault("building:levels")?[..Math.Min(100,f.Tags["building:levels"].Length)];b.SourceRoof=f.Tags.GetValueOrDefault("roof:shape")?[..Math.Min(100,f.Tags["roof:shape"].Length)];
            if(w<=64&&h<=64){
                var levels=kind is "apartments" or "school" or "clinic" or "hotel"?2:1;
                if(f.Tags.TryGetValue("building:levels",out var levelText)&&int.TryParse(levelText,out var specified)&&specified>0){levels=Math.Clamp(specified,1,16);b.LevelsOrigin="osm-levels";if(specified>16)limitedLevels++;}
                else if(f.Tags.TryGetValue("height",out var heightText)&&double.TryParse(heightText.Replace(" m",""),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var height)&&height>0&&double.IsFinite(height)){levels=Math.Clamp((int)Math.Min(16,Math.Round(height/3)),1,16);b.LevelsOrigin="osm-height";}
                var basements=0;b.SourceBasements=f.Tags.GetValueOrDefault("building:levels:underground")?[..Math.Min(100,f.Tags["building:levels:underground"].Length)];
                if(b.SourceBasements is not null&&int.TryParse(b.SourceBasements,out var depth)&&depth>0){basements=Math.Clamp(depth,0,16);if(depth>16)limitedLevels++;}
                var seed=unchecked(project.Seed^x*73856093^y*19349663);b.Design=BuildingDesigner.Generate(b,levels,seed,"rect",basements);
                if(p.SurfaceTheme!="legacy")VisualStyles.Apply(b);
                if(f.Tags.GetValueOrDefault("roof:shape") is "flat" or "gabled" or "shed" or "skillion" or "hipped")b.Design.Roof=f.Tags["roof:shape"]=="skillion"?"shed":f.Tags["roof:shape"];
                else if(b.SourceRoof is not null){b.Design.Roof=b.SourceRoof=="pyramidal"?"hipped":b.SourceRoof=="saltbox"?"gabled":"flat";approximateRoofs++;}
                ApplyOutline(b,f.Rings);
            }
            if(w>64||h>64||!MapGenerator.CanPlace(p,b)){
                skipped++;
                var reason=w>64||h>64?"Слишком большой контур":b.X<1||b.Y<1||b.X+b.Width>=p.Width||b.Y+b.Height>=p.Height?"За границей карты":b.X/256!=(b.X+b.Width)/256||b.Y/256!=(b.Y+b.Height)/256?"Граница ячейки":p.Buildings.Any(a=>b.X<a.X+a.Width+1&&b.X+b.Width+1>a.X&&b.Y<a.Y+a.Height+1&&b.Y+b.Height+1>a.Y)?"Пересечение зданий":"Вода или дорога";
                skipReasons[reason]=skipReasons.GetValueOrDefault(reason)+1;continue;
            }
            p.Buildings.Add(b);imported++;
        }
        p.Spawns=[MapGenerator.FindSafeSpawn(p)];
        p.ImportSummary=new(){SourceFeatures=features.Count,SourceBuildings=transformed.Count(f=>f.Polygon&&f.Tags.ContainsKey("building")),PlacedBuildings=p.Buildings.Count,SkipReasons=new(skipReasons)};
        p.ImportSummary.Notes.Add($"Этажность из OSM: {p.Buildings.Count(b=>b.LevelsOrigin=="osm-levels")}; оценка по высоте: {p.Buildings.Count(b=>b.LevelsOrigin=="osm-height")}; предположена: {p.Buildings.Count(b=>b.LevelsOrigin=="inferred")}.");
        if(limitedLevels>0)p.ImportSummary.Notes.Add($"Наземная/подземная этажность ограничена 16 уровнями для {limitedLevels} исходных контуров.");if(approximateRoofs>0)p.ImportSummary.Notes.Add($"Необычные формы крыши адаптированы для {approximateRoofs} контуров; исходный тег сохранён в свойствах здания.");
        if(preserveEdits)p.ImportSummary.Notes.Add($"Сохранено вручную изменённых зданий: {p.Buildings.Count(b=>b.Modified)}. Соседние исходные контуры могут быть пропущены ради сохранения этих изменений.");
        foreach(var b in p.Buildings)for(var y=b.Y/8;y<=(b.Y+b.Height)/8;y++)for(var x=b.X/8;x<=(b.X+b.Width)/8;x++)p.Zombies[y*(p.Width/8)+x]=90;
        ProjectValidator.Validate(p);
        return new(p,imported,skipped,skipped>0?[$"Пропущено зданий: {skipped}. "+string.Join("; ",skipReasons.Select(r=>$"{r.Key}: {r.Value}"))+". Контуры адаптированы к сетке; интерьеры сгенерированы."]:[]){SourceBuildings=transformed.Count(f=>f.Polygon&&f.Tags.ContainsKey("building")),SourceFeatures=features.Count,SkipReasons=skipReasons};
    }
    private static void ApplyOutline(Building b,List<List<(double X,double Y)>> rings){
        var original=b.Design!;var mask=new bool[b.Width*b.Height];
        for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)mask[y*b.Width+x]=Inside(b.X+x+.5,b.Y+y+.5,rings[0])&&!rings.Skip(1).Any(r=>Inside(b.X+x+.5,b.Y+y+.5,r));
        if(mask.Count(v=>v)<16)return;
        var staircase=-1;for(var y=0;y<b.Height-4&&staircase<0;y++)for(var x=0;x<b.Width;x++)if(Enumerable.Range(0,5).All(i=>mask[(y+i)*b.Width+x])){staircase=y*b.Width+x;break;}
        if(original.Floors.Count>1&&staircase<0)return;
        var draft=JsonSerializer.Deserialize<BuildingDesign>(JsonSerializer.Serialize(original))!;b.Design=draft;draft.Shape="custom";
        for(var z=0;z<draft.Floors.Count;z++){
            var f=draft.Floors[z];var cells=f.Cells.ToCharArray();for(var i=0;i<cells.Length;i++)if(!mask[i])cells[i]='.';
            f.Objects.Clear();if(staircase>=0&&draft.Floors.Count>1){for(var i=0;i<5;i++)cells[staircase+i*b.Width]='0';if(z<draft.Floors.Count-1)f.Objects.Add(new(){X=staircase%b.Width,Y=staircase/b.Width,Kind="stairs"});}
            f.Cells=new(cells);f.Edges=BuildingDesigner.AutoEdges(b,f,z+draft.MinLevel);
        }
        try{BuildingDesigner.Validate(b);for(var z=0;z<draft.Floors.Count;z++)BuildingDesigner.Furnish(b,z,new Random(unchecked(draft.Variant+z*1009)));BuildingDesigner.Validate(b);}catch(ArgumentException){b.Design=original;}
    }
    private static JsonDocument Parse(string json){try{return JsonDocument.Parse(json,new(){MaxDepth=48});}catch(JsonException){throw new ArgumentException("Не удалось прочитать JSON геоданных.");}}
    private static List<Feature> ReadFeatures(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Ожидается объект GeoJSON или ответ Overpass.");
        var result=new List<Feature>();var pointCount=0;
        (double,double) Point(double x,double y){if(!double.IsFinite(x)||!double.IsFinite(y)||x is < -180 or >180||y is < -85 or >85||++pointCount>250000)throw new ArgumentException("Некорректные координаты или больше 250 000 точек.");return(x,y);}
        List<(double,double)> GeoRing(JsonElement ring) {
            if(ring.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Повреждённые координаты GeoJSON.");
            return ring.EnumerateArray().Select(v=>v.ValueKind==JsonValueKind.Array&&v.GetArrayLength()>=2&&v[0].TryGetDouble(out var x)&&v[1].TryGetDouble(out var y)?Point(x,y):throw new ArgumentException("Повреждённая точка GeoJSON.")).ToList();
        }
        Dictionary<string,string> Tags(JsonElement el,string key){var tags=new Dictionary<string,string>();if(el.TryGetProperty(key,out var props)&&props.ValueKind==JsonValueKind.Object)foreach(var pair in props.EnumerateObject())tags[pair.Name]=pair.Value.ToString();return tags;}
        void Add(Dictionary<string,string> tags,List<List<(double,double)>> rings,bool polygon) {
            if(rings.Count==0||rings.Any(r=>r.Count<(polygon?4:2)))return;
            if(result.Count>=30000)throw new ArgumentException("Слишком много объектов: предел 30 000.");
            result.Add(new(tags,rings,polygon));
        }
        if(root.TryGetProperty("elements",out var elements)&&elements.ValueKind==JsonValueKind.Array) {
            var relationMembers=new HashSet<long>();
            foreach(var relation in elements.EnumerateArray().Where(e=>e.TryGetProperty("members",out _)))foreach(var member in relation.GetProperty("members").EnumerateArray())if(member.TryGetProperty("ref",out var reference)&&reference.TryGetInt64(out var id))relationMembers.Add(id);
            foreach(var el in elements.EnumerateArray()) {
                if(el.TryGetProperty("id",out var elementId)&&elementId.TryGetInt64(out var wayId)&&relationMembers.Contains(wayId)&&el.TryGetProperty("type",out var elementType)&&elementType.GetString()=="way")continue;
                var tags=Tags(el,"tags");if(el.TryGetProperty("id",out var sourceId)&&sourceId.TryGetInt64(out var sourceNumber))tags["__pz_source"]=$"osm-{(el.TryGetProperty("type",out var sourceType)?sourceType.GetString():"way")}-{sourceNumber}";
                if(el.TryGetProperty("geometry",out var geometry)&&geometry.ValueKind==JsonValueKind.Array) {
                    var ring=geometry.EnumerateArray().Where(v=>v.TryGetProperty("lon",out _)&&v.TryGetProperty("lat",out _)).Select(v=>Point(v.GetProperty("lon").GetDouble(),v.GetProperty("lat").GetDouble())).ToList();
                    Add(tags,[ring],tags.ContainsKey("building")||tags.ContainsKey("landuse")||tags.GetValueOrDefault("natural") is "water" or "wood" or "sand" or "beach" or "wetland");
                } else if(el.TryGetProperty("members",out var members)&&members.ValueKind==JsonValueKind.Array) {
                    var outerSegments=new List<List<(double X,double Y)>>();var innerSegments=new List<List<(double X,double Y)>>();
                    foreach(var member in members.EnumerateArray())if(member.TryGetProperty("geometry",out var memberGeometry)&&memberGeometry.ValueKind==JsonValueKind.Array){
                        var segment=memberGeometry.EnumerateArray().Where(v=>v.TryGetProperty("lon",out _)&&v.TryGetProperty("lat",out _)).Select(v=>Point(v.GetProperty("lon").GetDouble(),v.GetProperty("lat").GetDouble())).ToList();
                        if(segment.Count<2)continue;
                        (member.TryGetProperty("role",out var role)&&role.GetString()=="inner"?innerSegments:outerSegments).Add(segment);
                    }
                    var outers=Stitch(outerSegments);var inners=Stitch(innerSegments);
                    var part=0;foreach(var outer in outers){var rings=new List<List<(double,double)>>{outer};foreach(var inner in inners)if(Inside(inner[0].X,inner[0].Y,outer))rings.Add(inner);var partTags=new Dictionary<string,string>(tags);if(outers.Count>1&&partTags.ContainsKey("__pz_source"))partTags["__pz_source"]+=$"-part-{part}";part++;Add(partTags,rings,true);}
                }
            }
        }else if(root.TryGetProperty("features",out var features)&&features.ValueKind==JsonValueKind.Array) {
            foreach(var feature in features.EnumerateArray()) {
                if(!feature.TryGetProperty("geometry",out var geometry)||geometry.ValueKind!=JsonValueKind.Object||!geometry.TryGetProperty("coordinates",out var coords))continue;
                var tags=Tags(feature,"properties");if(feature.TryGetProperty("id",out var sourceId)&&sourceId.ValueKind is JsonValueKind.String or JsonValueKind.Number){var text=sourceId.ToString();tags["__pz_source"]="geo-"+(text.Length>75?Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant():text);}var type=geometry.GetProperty("type").GetString();
                if(type=="LineString")Add(tags,[GeoRing(coords)],false);
                else if(type=="Polygon")Add(tags,coords.EnumerateArray().Select(GeoRing).ToList(),true);
                else if(type=="MultiPolygon"){var part=0;foreach(var polygon in coords.EnumerateArray()){var partTags=new Dictionary<string,string>(tags);if(partTags.ContainsKey("__pz_source"))partTags["__pz_source"]+=$"-part-{part}";part++;Add(partTags,polygon.EnumerateArray().Select(GeoRing).ToList(),true);}}
                else if(type=="MultiLineString")foreach(var line in coords.EnumerateArray())Add(tags,[GeoRing(line)],false);
            }
        } else throw new ArgumentException("Поддерживаются GeoJSON FeatureCollection и JSON Overpass.");
        return result;
    }
    private static List<List<(double X,double Y)>> Stitch(List<List<(double X,double Y)>> segments)
    {
        var endpoints=new Dictionary<(double,double),List<int>>();var used=new bool[segments.Count];
        void Link((double,double) point,int index){if(!endpoints.TryGetValue(point,out var list))endpoints[point]=list=[];list.Add(index);}
        for(var i=0;i<segments.Count;i++){Link(segments[i][0],i);Link(segments[i][^1],i);}
        var result=new List<List<(double,double)>>();
        for(var i=0;i<segments.Count;i++){if(used[i])continue;used[i]=true;var ring=new List<(double,double)>(segments[i]);
            while(ring[^1]!=ring[0]){
                if(!endpoints.TryGetValue(ring[^1],out var candidates))break;var next=candidates.FindIndex(j=>!used[j]);if(next<0)break;var index=candidates[next];used[index]=true;var segment=segments[index];
                if(segment[0]==ring[^1])ring.AddRange(segment.Skip(1));else for(var j=segment.Count-2;j>=0;j--)ring.Add(segment[j]);
            }
            if(ring.Count>=4&&ring[0]==ring[^1])result.Add(ring);
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
