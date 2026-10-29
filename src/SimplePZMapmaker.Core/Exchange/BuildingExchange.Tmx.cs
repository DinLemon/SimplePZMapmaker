using System.Globalization;
using System.Xml.Linq;

namespace SimplePZMapmaker.Core.Exchange;

public static partial class BuildingExchange
{
    sealed record TileLayer(int Level, string Name, string?[] Tiles);
    static XElement WriteTmx(Building b, Report report)
    {
        var width = b.Width + 1; var height = b.Height + 1;
        var layers = new List<TileLayer>();
        foreach (var (floor, z) in b.Design!.Floors.Select((f, i) => (f, i + b.Design.MinLevel)))
        {
            var named = new Dictionary<string, string?[]>(StringComparer.Ordinal);
            void Put(string name, int x, int y, string tile)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) throw new ArgumentException("Tile coordinates cross the TMX building envelope.");
                if (name == "Furniture") RequireObjectCell(b, floor, x, y);
                var index = y * width + x;
                if (!named.TryGetValue(name, out var layer)) named[name] = layer = new string?[width * height];
                if (layer[index] is not null)
                {
                    var suffix = 2;
                    while (named.TryGetValue(name + suffix, out var extra) && extra[index] is not null) suffix++;
                    if (!named.TryGetValue(name + suffix, out layer)) named[name + suffix] = layer = new string?[width * height];
                }
                layer[index] = Tile(tile);
            }
            for (var i = 0; i < floor.Cells.Length; i++) if (floor.Cells[i] != '.') Put("Floor", i % b.Width, i / b.Width, FloorResource(b, floor, i));
            foreach (var edge in floor.Edges.OrderBy(e => e.Y).ThenBy(e => e.X).ThenBy(e => e.Edge, StringComparer.Ordinal))
            {
                if (edge.Kind == "open") continue;
                var wall = WallResource(b, floor, edge);
                if (!TryWall(wall, out var direction, out _) || direction != edge.Edge)
                    report.Add("wall-orientation", "/floor/edges", "TMX needs wall sprites with verified north/west orientation from the wall catalog: " + wall);
                Put("Walls", edge.X, edge.Y, wall);
                if (edge.Kind is "door" or "window") Put(edge.Kind == "door" ? "Doors" : "Windows", edge.X, edge.Y, OpeningResource(b, edge));
                if (edge.OverlayTile is not null) UnsupportedOverlay(report, "/floor/edges");
            }
            foreach (var obj in floor.Objects.Where(o => o.Kind != "stairs")) foreach (var part in BuildingObjects.Parts(obj)) Put("Furniture", obj.X + part.Dx, obj.Y + part.Dy, part.Tile);
            foreach (var name in new[] { "Floor", "Walls", "Walls2", "Doors", "Doors2", "Windows", "Windows2", "Furniture", "Furniture2", "Furniture3", "Furniture4" })
                if (named.Remove(name, out var data)) layers.Add(new(z, name, data));
            foreach (var name in named.Keys) report.Add("layer-overflow", "/floor", "Unsupported extra stacked tile layer " + name + " is omitted.");
        }
        var sheets = layers.SelectMany(l => l.Tiles).Where(t => t is not null).Select(t => t!).Distinct(StringComparer.Ordinal)
            .GroupBy(t => t[..t.LastIndexOf('_')]).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        var root = new XElement("map", A("version", "2.0"), A("orientation", "levelisometric"), A("width", width), A("height", height), A("tilewidth", 64), A("tileheight", 32));
        var gids = new Dictionary<string, uint>(StringComparer.Ordinal); uint first = 1;
        foreach (var sheet in sheets)
        {
            var indices = sheet.Select(t => int.Parse(t[(t.LastIndexOf('_') + 1)..], CultureInfo.InvariantCulture)).ToArray();
            if (indices.Max() >= 16384) throw new ArgumentException("Atlas tile index exceeds 16383 (bounded 8-column atlas).");
            var count = (indices.Max() / 8 + 1) * 8;
            root.Add(new XElement("tileset", A("firstgid", first), A("name", sheet.Key), A("tilewidth", 64), A("tileheight", 128), A("tilecount", count), A("columns", 8),
                new XElement("image", A("source", sheet.Key + ".png"), A("width", 512), A("height", count / 8 * 128))));
            foreach (var tile in sheet) gids[tile] = first + (uint)int.Parse(tile[(tile.LastIndexOf('_') + 1)..], CultureInfo.InvariantCulture);
            first += (uint)count;
        }
        foreach (var layer in layers) root.Add(new XElement("layer", A("name", layer.Name), A("level", layer.Level), A("width", width), A("height", height),
            new XElement("data", A("encoding", "csv"), CsvText(layer.Tiles.Select(t => t is null ? 0 : gids[t])))));
        var id = 1;
        foreach (var (floor, z) in b.Design.Floors.Select((f, i) => (f, i + b.Design.MinLevel)))
        {
            var group = new XElement("objectgroup", A("name", "RoomDefs"), A("level", z));
            foreach (var room in floor.Rooms.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                // One run per rectangle, sharing #id: the native RoomDefs convention groups these runs as one room.
                var roomId = id++;
                for (var y = 0; y < b.Height; y++) for (var x = 0; x < b.Width;)
                {
                    if (floor.Cells[y * b.Width + x] != room.Key[0]) { x++; continue; }
                    var start = x;
                    while (x < b.Width && floor.Cells[y * b.Width + x] == room.Key[0]) x++;
                    group.Add(new XElement("object", A("name", (room.Value == "balcony" ? "emptyoutside" : room.Value) + "#" + roomId), A("type", "room"),
                        A("x", start * 64), A("y", y * 32), A("width", (x - start) * 64), A("height", 32)));
                }
            }
            root.Add(group);
        }
        return root;
    }

    static Building ReadTmx(XElement root, Report report)
    {
        if (root.Name != "map") throw new ArgumentException("TMX requires a map root.");
        if (S(root, "version") is not ("1.0" or "1.1" or "1.2" or "1.3" or "1.4" or "1.5" or "1.6" or "1.7" or "1.8" or "1.9" or "1.10" or "2.0")) throw new ArgumentException("Unsupported TMX version (Tiled 1.0..1.10 or TileZed 2.0 supported).");
        var orientation = S(root, "orientation");
        if (orientation is not ("levelisometric" or "isometric" or "orthogonal")) throw new ArgumentException("Unsupported TMX orientation.");
        Int(root, "infinite", 0, 0, 0);
        if (S(root, "renderorder", "right-down") != "right-down") report.Add("render-order", "/map/@renderorder", "Only right-down render ordering is retained.");
        if (root.Descendants("chunk").Any() || root.Descendants("group").Any()) throw new ArgumentException("Infinite chunks and nested layer groups are unsupported.");
        var width = Int(root, "width", 3, MaxDimension + 1); var height = Int(root, "height", 3, MaxDimension + 1);
        var tw = Int(root, "tilewidth", 1, 256); var th = Int(root, "tileheight", 1, 256);
        if (tw != 64 || th != 32) throw new ArgumentException("Building TMX requires 64x32 map tiles.");
        // BuildingEd's native map envelope includes one extra east/south tile for wall edges.
        var b = Empty(width - 1, height - 1);
        UnknownChildren(root, report, "tileset", "layer", "objectgroup", "properties");
        ReadProperties(root.Element("properties"), report);
        var sheets = new List<(uint First, uint Count, string Name)>();
        var tilesets = root.Elements("tileset").ToArray();
        if (tilesets.Length > 512) throw new ArgumentException("More than 512 tilesets.");
        foreach (var ts in tilesets)
        {
            if (ts.Attribute("source") is not null) throw new ArgumentException("External TSX tilesets are unsupported; inline the tileset first. No file or URL is resolved.");
            var first = (uint)Int(ts, "firstgid", 1, 0x0fffffff);
            var name = S(ts, "name"); Tile(name + "_0");
            var image = ts.Elements("image").SingleOrDefault() ?? throw new ArgumentException("Atlas image tilesets are required.");
            if (ts.Elements("tile").Any(t => t.Element("image") is not null)) throw new ArgumentException("Image collection tilesets are unsupported.");
            Int(ts, "tilewidth", 64, 64); Int(ts, "tileheight", 128, 128);
            Int(ts, "spacing", 0, 0, 0); Int(ts, "margin", 0, 0, 0);
            var columns = Int(ts, "columns", 8, 8, Int(image, "width", 64, 262144) / 64);
            var count = (uint)Int(ts, "tilecount", 1, 1_000_000, columns * (Int(image, "height", 128, 262144) / 128));
            if (first + count > 0x10000000 || sheets.Any(s => first < s.First + s.Count && s.First < first + count) || sheets.Any(s => s.Name == name)) throw new ArgumentException("Overlapping GID ranges or duplicate tileset names.");
            UnknownChildren(ts, report, "image");
            sheets.Add((first, count, name));
        }
        string? Resolve(uint gid)
        {
            if (gid == 0) return null;
            if ((gid & 0xf0000000) != 0) throw new ArgumentException("TMX GID flip/rotation bits are unsupported; bake transforms in the source editor.");
            var sheet = sheets.SingleOrDefault(s => gid >= s.First && gid - s.First < s.Count);
            if (sheet.Name is null) throw new ArgumentException("GID references an undeclared tile.");
            return Tile(sheet.Name + "_" + (gid - sheet.First));
        }
        var elements = root.Elements("layer").Concat(root.Elements("objectgroup")).ToArray();
        if (elements.Length > MaxLayers) throw new ArgumentException("More than 1024 layers.");
        if ((long)root.Elements("layer").Count() * width * height > MaxDecodedTiles) throw new ArgumentException("Decoded grids exceed one million tile values.");
        var levels = elements.Select(e => LayerLevel(e).Level).Distinct().Order().ToArray();
        if (levels.Length == 0 || levels[0] > 0 || levels[^1] < 0 || levels.Length != levels[^1] - levels[0] + 1) throw new ArgumentException("Contiguous signed floors containing ground level 0 are required (-32..31).");
        if (orientation == "isometric" && levels.Length > 1) throw new ArgumentException("Multilevel legacy isometric floor offsets are unsupported. Convert to levelisometric first.");
        b.Design!.MinLevel = levels[0];
        foreach (var z in levels)
        {
            var floor = new BuildingFloor { Cells = new('.', b.Width * b.Height) }; var cells = floor.Cells.ToCharArray();
            b.Design.Floors.Add(floor);
            var roomIds = new Dictionary<string, char>(StringComparer.Ordinal);
            foreach (var group in root.Elements("objectgroup").Where(e => LayerLevel(e).Level == z))
            {
                CheckLayerTransform(group);
                if (LayerLevel(group).Name != "RoomDefs") { report.Add("object-layer", "/objectgroup", "Only rectangular RoomDefs object groups are supported."); continue; }
                UnknownChildren(group, report, "object");
                foreach (var obj in group.Elements("object"))
                {
                    if (S(obj, "visible", "1") != "1") report.Add("hidden-room", "/objectgroup/object/@visible", "Room visibility is not retained.");
                    if (S(obj, "type", S(obj, "class")) != "room" || obj.HasElements || obj.Attribute("gid") is not null || S(obj, "rotation", "0") != "0")
                    { report.Add("room-shape", "/objectgroup/object", "Only unrotated rectangular type=room objects are supported."); continue; }
                    var name = S(obj, "name");
                    var split = name.LastIndexOf('#');
                    if (split < 1) throw new ArgumentException("RoomDefs names require InternalName#id.");
                    if (!roomIds.TryGetValue(name, out var code))
                    {
                        if (roomIds.Count == 35) throw new ArgumentException("More than 35 rooms on one floor.");
                        roomIds[name] = code = RoomCodes[roomIds.Count]; floor.Rooms[code.ToString()] = RoomName(name[..split], report, "/RoomDefs/object/@name");
                    }
                    var sx = orientation == "isometric" ? th : tw;
                    var x = Pixel(obj, "x", sx); var y = Pixel(obj, "y", th);
                    var w = Pixel(obj, "width", sx); var h = Pixel(obj, "height", th);
                    if (w < 1 || h < 1 || x + w > b.Width || y + h > b.Height) throw new ArgumentException("Room rectangle crosses the building envelope or its reserved east/south wall border.");
                    for (var yy = y; yy < y + h; yy++) for (var xx = x; xx < x + w; xx++)
                    {
                        var i = yy * b.Width + xx;
                        if (cells[i] != '.' && cells[i] != code) throw new ArgumentException("Room rectangles overlap.");
                        cells[i] = code;
                    }
                }
            }
            floor.Cells = new(cells);
            if (!cells.Any(c => c != '.')) throw new ArgumentException("Every imported floor needs rectangular RoomDefs; room semantics are not inferred from tile colors.");
            // An absent wall sprite is an absent edge, not an authored passage object.
            // Inventing open edges around the envelope would incorrectly forbid furniture beside them.
            floor.Edges = [];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in root.Elements("layer").Where(e => LayerLevel(e).Level == z).OrderBy(e => LayerOrder(LayerLevel(e).Name)))
            {
                CheckLayerTransform(element);
                var name = LayerLevel(element).Name;
                if (!seen.Add(name)) throw new ArgumentException("Duplicate tile layer on a floor.");
                Int(element, "width", width, width); Int(element, "height", height, height);
                UnknownChildren(element, report, "data");
                var data = element.Elements("data").SingleOrDefault() ?? throw new ArgumentException("Layer data required.");
                if (data.Attribute("compression") is not null) throw new ArgumentException("Compressed layer data is unsupported.");
                uint[] gids;
                switch (S(data, "encoding"))
                {
                    case "csv": if (data.HasElements) throw new ArgumentException("CSV data cannot contain elements."); gids = Csv(data.Value, width * height); break;
                    case "": if (data.Elements().Any(e => e.Name != "tile")) throw new ArgumentException("Invalid XML tile data."); gids = data.Elements("tile").Select(t => (uint)Int(t, "gid", 0, int.MaxValue)).ToArray(); if (gids.Length != width * height) throw new ArgumentException("Invalid XML tile grid length."); break;
                    default: throw new ArgumentException("Only CSV and unencoded XML tile data are supported; base64/compression are rejected.");
                }
                ReadTiles(b, floor, name, gids.Select(Resolve).ToArray(), width, report);
            }
            if (cells.Select((c, i) => c != '.' && !floor.FloorTiles.ContainsKey(i)).Any(v => v)) report.Add("missing-floor-tile", "/layer/Floor", "Room cells without floor sprites use the editor default on lossy import.");
            if (floor.Edges.Any(e => e.Kind is "door" or "window" && e.OpeningTile is null)) report.Add("missing-opening-tile", "/layer", "Opening walls without door/window sprites use the editor default on lossy import.");
        }
        return b;
    }

    static int Pixel(XElement e, string name, int scale)
    {
        if (!decimal.TryParse(S(e, name), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || v < 0 || v > 65536 || v % scale != 0) throw new ArgumentException("Room coordinates must be nonnegative integer tile multiples.");
        return (int)(v / scale);
    }
    static (int Level, string Name) LayerLevel(XElement e)
    {
        var name = S(e, "name"); var split = name.IndexOf('_'); int? prefix = null;
        if (split > 0 && int.TryParse(name[..split], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z)) { prefix = z; name = name[(split + 1)..]; }
        if (e.Parent is { } parent && S(parent, "version") == "2.0")
        {
            if (prefix.HasValue) throw new ArgumentException("TileZed 2.0 uses bare layer names with required level attributes; legacy name prefixes require version 1.x.");
            return (Int(e, "level", -32, 31), name);
        }
        var level = Int(e, "level", -32, 31, prefix ?? 0);
        if ((prefix ?? 0) != level) throw new ArgumentException("TMX 1.x levels come from name prefixes; a level attribute must agree with that prefix.");
        return (level, name);
    }
    static void CheckLayerTransform(XElement e)
    {
        foreach (var name in new[] { "x", "y", "offsetx", "offsety" })
            if (S(e, name, "0") != "0") throw new ArgumentException("Layer offsets/transforms are unsupported.");
        if (S(e, "opacity", "1") != "1" || S(e, "visible", "1") != "1") throw new ArgumentException("Hidden/translucent layers require explicit baking before import.");
    }
    static int LayerOrder(string name) => name.StartsWith("Floor", StringComparison.Ordinal) ? 0 : name.StartsWith("Walls", StringComparison.Ordinal) ? 1 : name.StartsWith("Doors", StringComparison.Ordinal) || name.StartsWith("Windows", StringComparison.Ordinal) ? 2 : 3;
    static bool TryWall(string tile, out string direction, out string kind)
    {
        direction = ""; kind = "wall";
        if (WallCatalog.Find(tile) is not { } set) return false;
        if (tile == set.North || tile == set.NorthDoor || tile == set.NorthWindow) direction = "n";
        else if (tile == set.West || tile == set.WestDoor || tile == set.WestWindow) direction = "w";
        else return false;
        if (tile == set.NorthDoor || tile == set.WestDoor) kind = "door";
        else if (tile == set.NorthWindow || tile == set.WestWindow) kind = "window";
        return true;
    }
    static void ReadTiles(Building b, BuildingFloor f, string name, string?[] tiles, int stride, Report report)
    {
        if (name.StartsWith("WallOverlay", StringComparison.Ordinal) && tiles.Any(t => t is not null)) { UnsupportedOverlay(report, "/tiles/" + name); return; }
        if (name is not ("Floor" or "Walls" or "Walls2" or "Doors" or "Doors2" or "Windows" or "Windows2" or "Furniture" or "Furniture2" or "Furniture3" or "Furniture4"))
        { if (tiles.Any(t => t is not null)) report.Add("tile-layer", "/tiles/" + name[..Math.Min(80, name.Length)], "Nonempty tile layer is outside the exchange subset and omitted."); return; }
        for (var i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] is not { } tile) continue;
            var x = i % stride; var y = i / stride;
            if (name == "Floor")
            {
                if (x >= b.Width || y >= b.Height || f.Cells[y * b.Width + x] == '.') { report.Add("outside-floor", "/tiles/Floor", "Floor sprites outside room cells are omitted."); continue; }
                f.FloorTiles[y * b.Width + x] = tile;
            }
            else if (name.StartsWith("Walls", StringComparison.Ordinal))
            {
                if (TryWall(tile, out var direction, out var kind))
                {
                    if (f.Edges.Any(e => e.X == x && e.Y == y && e.Edge == direction && e.WallTile is not null)) throw new ArgumentException("Overlapping wall sprites on the same edge.");
                    Edge(b, f, x, y, direction, kind, tile);
                }
                else if (WallCatalog.Sets.FirstOrDefault(s => s.NorthWest == tile) is { } corner)
                { report.Add("corner-sprite", "/tiles/" + name, "Combined corner sprite is decomposed into north/west wall edges."); Edge(b, f, x, y, "n", "wall", corner.North); Edge(b, f, x, y, "w", "wall", corner.West); }
                else report.Add("wall-tile", "/tiles/" + name, "Wall sprite has no verified edge orientation and is omitted: " + tile);
            }
            else if (name.StartsWith("Doors", StringComparison.Ordinal) || name.StartsWith("Windows", StringComparison.Ordinal))
            {
                var kind = name.StartsWith("Doors", StringComparison.Ordinal) ? "door" : "window";
                var candidates = f.Edges.Where(e => e.X == x && e.Y == y && e.Kind == kind).ToArray();
                var prefix = kind == "door" ? "fixtures_doors_01_" : "fixtures_windows_01_";
                string? direction = tile.StartsWith(prefix, StringComparison.Ordinal) ? int.Parse(tile[prefix.Length..], CultureInfo.InvariantCulture) % 2 == 1 ? "n" : "w" : candidates.Length == 1 ? candidates[0].Edge : null;
                if (direction is null) report.Add("opening-orientation", "/tiles/" + name, "Opening sprite orientation is ambiguous and is omitted: " + tile);
                else Edge(b, f, x, y, direction, kind, opening: tile);
            }
            else ObjectTile(b, f, x, y, tile, report);
        }
    }
}
