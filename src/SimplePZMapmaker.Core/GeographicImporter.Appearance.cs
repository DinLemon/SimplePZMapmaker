using System.Globalization;
namespace SimplePZMapmaker.Core;

/// <summary>Building appearance tags of the transfer contract (docs/TRANSFER-CONTRACT.md, section 5):
/// pz:kind picks the building kind, pz:style the interior style, roof:colour the roof tile set,
/// pz:wall the exterior wall set and pz:trim the window surround overlay.</summary>
public static partial class GeographicImporter
{
    /// <summary>pz:* keys the importer understands; any other pz:* key is kept with the source and reported once.</summary>
    public static readonly HashSet<string> KnownPzKeys=["pz:room","pz:wall","pz:trim","pz:style","pz:kind","pz:attached","pz:door-to","pz:fence","pz:posts","pz:gates","pz:zone","pz:vehicles","pz:direction"];
    /// <summary>Average opaque colour of the first sprite of each Build 42 roofs_30_NN set, measured from the installed game.</summary>
    static readonly (string Tileset,int R,int G,int B)[] RoofSwatches=[("roofs_30_01",55,63,70),("roofs_30_02",70,58,55),("roofs_30_03",211,209,209),("roofs_30_04",57,59,63),("roofs_30_05",48,60,45),("roofs_30_06",93,91,84),("roofs_30_07",121,95,79),("roofs_30_08",80,93,80),("roofs_30_09",131,83,88),("roofs_30_10",76,44,34)];
    static readonly Dictionary<string,string> RoofColourNames=new(StringComparer.OrdinalIgnoreCase){["grey"]="roofs_30_04",["gray"]="roofs_30_04",["black"]="roofs_30_04",["red"]="roofs_30_09",["maroon"]="roofs_30_09",["blue"]="roofs_30_01",["green"]="roofs_30_05",["brown"]="roofs_30_07",["white"]="roofs_30_03",["silver"]="roofs_30_06"};

    /// <summary>Building kind named by pz:kind, or null when absent or unknown.</summary>
    public static string? KindFromTag(IReadOnlyDictionary<string,string> tags)=>tags.GetValueOrDefault("pz:kind") switch{
        "house" or "bathhouse"=>"house","shed" or "toilet" or "greenhouse"=>"warehouse","garage"=>"garage",_=>null};

    /// <summary>roof:colour as a named colour or #rrggbb; a hex colour takes the nearest measured roof set.</summary>
    public static string? RoofTilesetFor(string? colour){
        if(string.IsNullOrWhiteSpace(colour))return null;colour=colour.Trim();if(RoofColourNames.TryGetValue(colour,out var named))return named;
        if(colour is ['#',_,_,_,_,_,_]&&int.TryParse(colour[1..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb)){var (r,g,b)=(rgb>>16&255,rgb>>8&255,rgb&255);return RoofSwatches.MinBy(s=>(s.R-r)*(s.R-r)+(s.G-g)*(s.G-g)+(s.B-b)*(s.B-b)).Tileset;}
        return null;
    }

    /// <summary>The first tile of an exterior wall set: "set:&lt;tile&gt;", a bare tile id, or the ID of a palette whose first wall resource starts the set.</summary>
    public static (string Prefix,int Base)? WallSetFor(string? value,IReadOnlyList<ResourcePalette>? palettes){
        if(string.IsNullOrWhiteSpace(value))return null;var tile=value.StartsWith("set:",StringComparison.Ordinal)?value[4..]:value;
        if(palettes?.FirstOrDefault(p=>p.Id==tile)?.Resources.FirstOrDefault(r=>r.Role=="wall")?.Id is {} first)tile=first;
        if(!VisualStyles.TileId(tile))return null;var split=tile.LastIndexOf('_');return(tile[..split],int.Parse(tile[(split+1)..],CultureInfo.InvariantCulture));
    }

    /// <summary>Applies the appearance tags to a building whose final footprint and edges are already set. Unusable values are reported through <paramref name="problem"/>.</summary>
    public static void ApplyAppearanceTags(Building b,IReadOnlyDictionary<string,string> tags,IReadOnlyList<ResourcePalette>? palettes,Action<string> problem){
        if(b.Design is not {} d)return;
        if(tags.TryGetValue("pz:style",out var style)){if(BuildingTemplateStyles.Find(style) is {} template)BuildingTemplateStyles.Apply(b,template);else if(VisualStyles.All.ContainsKey(style))VisualStyles.Apply(b,style);else problem($"pz:style '{style}' is not a known building style or tilezed:<template>");}
        if(tags.GetValueOrDefault("pz:kind") is "bathhouse" or "toilet")foreach(var f in d.Floors.Where(f=>f.Rooms.Count(r=>r.Key!="0")==1))f.Rooms[f.Rooms.Keys.Single(k=>k!="0")]="bathroom";
        if(tags.TryGetValue("roof:colour",out var colour)){if(RoofTilesetFor(colour) is {} roof)d.RoofTileset=roof;else problem($"roof:colour '{colour}' has no matching roof set");}
        (string Prefix,int Base)? wall=null;if(tags.TryGetValue("pz:wall",out var wallTag)&&(wall=WallSetFor(wallTag,palettes)) is null)problem($"pz:wall '{wallTag}' is not a wall set");
        (string N,string W)? trim=null;if(tags.TryGetValue("pz:trim",out var trimTag)){var pair=trimTag.Split(':');if(pair.Length==2&&VisualStyles.TileId(pair[0])&&VisualStyles.TileId(pair[1]))trim=(pair[0],pair[1]);else problem($"pz:trim '{trimTag}' is not a '<north tile>:<west tile>' pair");}
        if(wall is null&&trim is null)return;
        foreach(var f in d.Floors)foreach(var e in f.Edges){
            var north=e.Edge=="n";var a=BuildingDesigner.Cell(b,f,e.X,e.Y);var other=north?BuildingDesigner.Cell(b,f,e.X,e.Y-1):BuildingDesigner.Cell(b,f,e.X-1,e.Y);
            if(e.Kind=="open"||(a=='.')==(other=='.')||f.Rooms.GetValueOrDefault((a=='.'?other:a).ToString())=="balcony")continue;
            if(wall is {} set)e.WallTile=WallCatalog.Tile(set.Prefix,set.Base,e.Kind,north);
            if(trim is {} t&&e.Kind=="window")e.OverlayTile=north?t.N:t.W;
        }
    }

    /// <summary>One report line per unknown pz:* key across the imported features.</summary>
    static IEnumerable<string> UnknownPzKeys(IEnumerable<IReadOnlyDictionary<string,string>> tags)=>tags.SelectMany(t=>t.Keys).Where(k=>k.StartsWith("pz:",StringComparison.Ordinal)&&!KnownPzKeys.Contains(k)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(32).Select(k=>$"Unknown source key {k} was kept with the source data and not applied.");
}
