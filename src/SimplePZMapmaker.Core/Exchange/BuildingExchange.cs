using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SimplePZMapmaker.Core.Exchange;

public sealed record ExchangeDiagnostic(string Code, string Severity, string Path, string Message);
public sealed record BuildingExchangeResult(Building? Building, string? Xml, IReadOnlyList<ExchangeDiagnostic> Diagnostics)
{
    public bool Success => Building is not null || Xml is not null;
}

/// <summary>Local building exchange. Never resolves files, URLs, external tilesets or entities.</summary>
public static partial class BuildingExchange
{
    public const int MaxInputBytes = 8 * 1024 * 1024;
    public const int MaxDepth = 16;
    public const int MaxElements = 150_000;
    public const int MaxDimension = 64;
    public const int MaxLayers = 1024;
    public const int MaxDecodedTiles = 1_000_000;
    const string RoomCodes = "0123456789abcdefghijklmnopqrstuvwxy";
    static XAttribute A(string name, object value) => new(name, value);
    static string S(XElement e, string name, string fallback = "") => (string?)e.Attribute(name) ?? fallback;
    static int Int(XElement e, string name, int min, int max, int? fallback = null)
    {
        var raw = (string?)e.Attribute(name);
        if (raw is null && fallback.HasValue) return fallback.Value;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            throw new ArgumentException($"Invalid {e.Name}/@{name}; expected {min}..{max}.");
        return n;
    }
    static string Tile(string value)
    {
        if (!VisualStyles.TileId(value)) throw new ArgumentException("Invalid tile identifier.");
        var split = value.LastIndexOf('_');
        return value[..(split + 1)] + int.Parse(value[(split + 1)..], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }
    sealed class Report
    {
        readonly List<ExchangeDiagnostic> diagnostics = [];
        public IReadOnlyList<ExchangeDiagnostic> Diagnostics => diagnostics;
        public bool Blocked(bool allowLossy) => diagnostics.Any(d => d.Severity == "error" || !allowLossy && d.Severity == "unsupported");
        public void Add(string code, string path, string message, string severity = "unsupported")
        {
            if (diagnostics.Count >= 200) { if (diagnostics.Count == 200) diagnostics.Add(new("diagnostic-limit", "error", "/", "More than 200 diagnostics; conversion stopped.")); return; }
            if (!diagnostics.Any(d => d.Code == code && d.Path == path && d.Message == message)) diagnostics.Add(new(code, severity, path, message));
        }
    }

    public static BuildingExchangeResult Import(string xml, string format, bool allowLossy = false)
    {
        var report = new Report();
        try
        {
            if (xml is null || xml.Length > MaxInputBytes || Encoding.UTF8.GetByteCount(xml) > MaxInputBytes)
                throw new ArgumentException("XML exceeds the 8 MiB input limit.");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaxInputBytes, MaxCharactersFromEntities = 1024, IgnoreComments = true, IgnoreProcessingInstructions = true };
            // Preflight before constructing a tree: character limits alone do not bound nesting or node overhead.
            using (var check = XmlReader.Create(new StringReader(xml), settings))
            {
                var elements = 0;
                while (check.Read())
                    if (check.Depth > MaxDepth || check.NodeType == XmlNodeType.Element && (++elements > MaxElements || check.AttributeCount > 32 || check.Name.Length > 128))
                        throw new ArgumentException("XML nesting, element or attribute limit exceeded.");
                    else if (check.NodeType == XmlNodeType.Element && check.MoveToFirstAttribute())
                    {
                        do { if (check.Name.Length > 128 || check.Value.Length > 4096) throw new ArgumentException("XML attribute length limit exceeded."); } while (check.MoveToNextAttribute());
                        check.MoveToElement();
                    }
            }
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var root = XDocument.Load(reader).Root ?? throw new ArgumentException("Missing XML root.");
            if (root.DescendantsAndSelf().Any(e => e.Name.NamespaceName.Length != 0 || e.Attributes().Any(a => a.IsNamespaceDeclaration || a.Name.NamespaceName.Length != 0)))
                throw new ArgumentException("XML namespaces are outside the supported exchange subset.");
            AuditAttributes(root, report);
            var building = Format(format) == "tbx" ? ReadTbx(root, report) : ReadTmx(root, report);
            if (building is not null)
            {
                building.Id = "exchange-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(xml)))[..24].ToLowerInvariant();
                // Structural validation only; access, installed resources and project placement are parent-owned checks.
                BuildingDesigner.Validate(building, requireAccess: false);
            }
            return new(report.Blocked(allowLossy) ? null : building, null, report.Diagnostics);
        }
        catch (Exception ex) when (ex is ArgumentException or XmlException or InvalidOperationException or OverflowException)
        {
            report.Add("invalid-input", "/", ex is XmlException ? "Malformed XML or prohibited DTD/entity." : ex.Message, "error");
            return new(null, null, report.Diagnostics);
        }
    }

    public static BuildingExchangeResult Export(Building building, string format, bool allowLossy = false)
    {
        var report = new Report();
        try
        {
            var kind = Format(format);
            if(building is null)throw new ArgumentException("A building draft is required.");
            BuildingDesigner.Validate(building, requireAccess: false);
            if (building.Design is null) throw new ArgumentException("An explicit building design is required; generate it before exchange.");
            var design = building.Design;
            if (kind == "tbx" && design.MinLevel != 0) throw new ArgumentException("TBX floors are ordered local levels starting at zero. Basement/signed level remapping is not implicit; use TMX.");
            if (design.Roof != "none" && !(kind == "tbx" && design.Roof == "flat" && FullIndoorRectangle(building)))
                report.Add("roof", "/design/roof", "Only TBX flat roofs over full indoor rectangles with an unchanged footprint on every floor are supported. Slopes, terraces, irregular roofs and TMX roof geometry require manual authoring; explicit lossy export omits them.");
            if (building.RequiredMods.Count != 0 || building.AssetSources.Count != 0 || building.Attribution.Count != 0)
                report.Add("dependencies", "/", "Mod dependencies, asset provenance and attribution are not embedded. Retain the source project and supply resources separately.");
            foreach (var (floor, z) in design.Floors.Select((f, i) => (f, i + design.MinLevel)))
            {
                if (floor.StaticModels.Count != 0) report.Add("static-models", $"/floor[{z}]", "Static models are omitted.");
                foreach (var obj in floor.Objects)
                {
                    if (obj.Kind == "stairs") report.Add("stairs", $"/floor[{z}]/objects", "Stair objects and their upper-floor cutouts are outside this subset; omitted.");
                    else if (BuildingObjects.Parts(obj).Any(p => p.Flags != 0)) report.Add("object-flags", $"/floor[{z}]/objects", "Custom navigation flags are not XML tile transforms and are omitted.");
                    if (obj.Kind != "stairs" && (obj.Kind != "asset" || BuildingObjects.Parts(obj).Length != 1))
                        report.Add("object-grouping", $"/floor[{z}]/objects", "Furniture type, orientation and multipart grouping are flattened into individual object tiles.");
                }
            }
            if (report.Blocked(allowLossy)) return new(null, null, report.Diagnostics);
            var root = kind == "tbx" ? WriteTbx(building, report) : WriteTmx(building, report);
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root.ToString(SaveOptions.None) + "\n";
            if (Encoding.UTF8.GetByteCount(xml) > MaxInputBytes) throw new ArgumentException("Export exceeds the 8 MiB exchange limit.");
            return new(null, report.Blocked(allowLossy) ? null : xml, report.Diagnostics);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            report.Add("invalid-building", "/", ex.Message, "error");
            return new(null, null, report.Diagnostics);
        }
    }

    static string Format(string format) => format?.ToLowerInvariant() is "tbx" or "tmx" ? format.ToLowerInvariant() : throw new ArgumentException("Format must be tbx or tmx.");
    static Building Empty(int width, int height) => new() { Name = "Imported building", Width = width, Height = height,
        FootprintOrigin = "authored", LevelsOrigin = "manual", Design = new() { Shape = "custom", Roof = "none" } };
    static string FloorResource(Building b, BuildingFloor f, int i)
    {
        var d = b.Design!;
        var style = d.Style.Length == 0 ? null : VisualStyles.All[d.Style];
        var room = f.Rooms[f.Cells[i].ToString()];
        return f.FloorTiles.GetValueOrDefault(i, f.FloorTile ?? (style is null ? d.Material == "wood" ? "floors_interior_tilesandwood_01_40" : "floors_interior_tilesandwood_01_0" : room is "bathroom" or "kitchen" ? style.WetFloor : style.Floor));
    }
    static string WallResource(Building b, BuildingFloor floor, BuildingEdge e)
    {
        var d = b.Design!; var style = d.Style.Length == 0 ? null : VisualStyles.All[d.Style];
        var a = BuildingDesigner.Cell(b, floor, e.X, e.Y);
        var other = BuildingDesigner.Cell(b, floor, e.X - (e.Edge == "w" ? 1 : 0), e.Y - (e.Edge == "n" ? 1 : 0));
        if (e.Kind == "wall" && (a == '.' || other == '.') && floor.Rooms.GetValueOrDefault((a == '.' ? other : a).ToString()) == "balcony")
            return e.WallTile ?? (e.Edge == "n" ? "carpentry_02_101" : "carpentry_02_100");
        return e.WallTile ?? WallCatalog.Tile(style?.Wall ?? "walls_exterior_house_01", style?.WallBase ?? (d.Material == "brick" ? 16 : d.Material == "plaster" ? 32 : 0), e.Kind, e.Edge == "n");
    }
    static string OpeningResource(Building b, BuildingEdge e)
    {
        var style = b.Design!.Style.Length == 0 ? null : VisualStyles.All[b.Design.Style];
        return e.OpeningTile ?? $"fixtures_{(e.Kind == "door" ? "doors" : "windows")}_01_{(e.Edge == "n" ? 1 : 0) + (e.Kind == "door" ? style?.DoorBase ?? 0 : style?.WindowBase ?? 0)}";
    }
    static uint[] Csv(string text, int length)
    {
        var tokens = text.Trim().Split(',', length + 1);
        if (tokens.Length != length) throw new ArgumentException($"CSV requires exactly {length} values.");
        return tokens.Select(t => uint.TryParse(t.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw new ArgumentException("Invalid unsigned CSV value.")).ToArray();
    }
    static void AuditAttributes(XElement root, Report report)
    {
        foreach (var e in root.DescendantsAndSelf())
        {
            string[] allowed = e.Name.LocalName switch
            {
                "building" => ["version", "width", "height", ..BuildingTiles],
                "room" => ["Name", "InternalName", "Color", ..RoomTiles],
                "floor" => ["level"], "tile_entry" => ["category"], "tiles" => ["layer"],
                "user_tiles" or "used_tiles" or "used_furniture" or "rooms" or "attributes" or "properties" => [],
                "furniture" => ["corners", "layer"], "entry" => ["orient", "grime"],
                "tile" => ["enum", "tile", "offset", "x", "y", "name", "gid"],
                "map" => ["version", "tiledversion", "orientation", "renderorder", "width", "height", "tilewidth", "tileheight", "infinite", "nextlayerid", "nextobjectid"],
                "tileset" => ["firstgid", "source", "name", "tilewidth", "tileheight", "tilecount", "columns", "spacing", "margin"],
                "image" => ["source", "width", "height", "trans"],
                "layer" or "objectgroup" => ["id", "name", "level", "width", "height", "x", "y", "offsetx", "offsety", "opacity", "visible"],
                "data" => ["encoding", "compression"], "property" => ["name", "value", "type"],
                "object" => root.Name == "building" ? ["type", "x", "y", "dir", "Tile", "length", "InteriorTile", "ExteriorTrim", "InteriorTrim", "FrameTile", "CurtainsTile", "ShuttersTile", "FurnitureTiles", "orient", "width", "height", "RoofType", "Depth", "CapTiles", "SlopeTiles", "TopTiles", "cappedW", "cappedN", "cappedE", "cappedS"] : ["id", "name", "type", "class", "x", "y", "width", "height", "rotation", "visible", "gid"],
                _ => []
            };
            foreach (var a in e.Attributes().Where(a => !allowed.Contains(a.Name.LocalName)))
                report.Add("attribute", "/" + e.Name + "/@" + a.Name, "Unsupported XML attribute is omitted.");
            string[] children = e.Name.LocalName switch
            {
                "building" => ["tile_entry", "user_tiles", "used_tiles", "used_furniture", "furniture", "room", "floor", "properties"],
                "floor" => ["object", "rooms", "tiles", "attributes"],
                "tile_entry" or "user_tiles" or "entry" => ["tile"], "furniture" => ["entry"],
                "properties" => ["property"], "map" => ["tileset", "layer", "objectgroup", "properties"],
                "tileset" => ["image"], "layer" => ["data"], "objectgroup" => ["object"], "data" => ["tile"],
                _ => []
            };
            UnknownChildren(e, report, children);
        }
    }
    static string CsvText(IEnumerable<uint> values) => string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));
    static void UnknownChildren(XElement element, Report report, params string[] supported)
    {
        foreach (var child in element.Elements().Where(e => !supported.Contains(e.Name.LocalName)))
            report.Add("element", "/" + element.Name + "/" + child.Name, "Unsupported XML element is omitted.");
    }
    static string RoomName(string name, Report report, string path)
    {
        if (BuildingDesigner.AllRoomNames.Contains(name)) return name;
        if (name == "emptyoutside") return "balcony";
        report.Add("room-name", path, $"Room name '{name[..Math.Min(100, name.Length)]}' is not supported; explicit lossy import uses hall.");
        return "hall";
    }
    static void Edge(Building b, BuildingFloor floor, int x, int y, string direction, string kind, string? wall = null, string? opening = null)
    {
        if (x < 0 || y < 0 || x > b.Width || y > b.Height || BuildingDesigner.Cell(b, floor, x, y) == '.' && BuildingDesigner.Cell(b, floor, x - (direction == "w" ? 1 : 0), y - (direction == "n" ? 1 : 0)) == '.')
            throw new ArgumentException("Wall/opening is outside the room footprint.");
        var edge = floor.Edges.FirstOrDefault(e => e.X == x && e.Y == y && e.Edge == direction);
        if (edge is null) { edge = new() { X = x, Y = y, Edge = direction }; floor.Edges.Add(edge); }
        edge.Kind = kind; edge.WallTile = wall ?? edge.WallTile; edge.OpeningTile = opening ?? edge.OpeningTile;
    }
    static void ObjectTile(Building b, BuildingFloor floor, int x, int y, string tile, Report report)
    {
        RequireObjectCell(b, floor, x, y);
        if (floor.Objects.Any(o => o.X == x && o.Y == y)) { report.Add("stacked-object", "/floor/tiles", "Multiple object tiles at the same coordinate cannot be represented and extra tiles are omitted."); return; }
        if (floor.Objects.Count >= 512) throw new ArgumentException("More than 512 object tiles per floor.");
        floor.Objects.Add(new() { Kind = "asset", X = x, Y = y, Asset = tile, Parts = [new(0, 0, tile)] });
    }

    static void RequireObjectCell(Building b, BuildingFloor floor, int x, int y)
    {
        if (x < 0 || y < 0 || x >= b.Width || y >= b.Height || floor.Cells[y * b.Width + x] == '.')
            throw new ArgumentException($"Furniture part ({x},{y}) is outside the room footprint; east/south wall padding is not furniture space. Move the complete object inside before exchange.");
    }

    static bool FullIndoorRectangle(Building b) => b.Design!.Floors.All(f =>
        f.Cells.All(c => c != '.' && f.Rooms[c.ToString()] != "balcony"));

    static void UnsupportedOverlay(Report report, string path) => report.Add("wall-overlay", path,
        "Wall overlay orientation and west-facing mirror state cannot be represented by this exchange subset. The source is retained; bake a verified directional sprite before exchange, or explicitly accept lossy omission.");
}
