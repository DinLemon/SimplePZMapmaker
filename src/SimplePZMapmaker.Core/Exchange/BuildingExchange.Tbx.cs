using System.Xml.Linq;

namespace SimplePZMapmaker.Core.Exchange;

public static partial class BuildingExchange
{
    static readonly string[] BuildingTiles = ["ExteriorWall", "ExteriorWallTrim", "Door", "DoorFrame", "Window", "Curtains", "Shutters", "Stairs", "RoofCap", "RoofSlope", "RoofTop", "GrimeWall"];
    static readonly string[] RoomTiles = ["InteriorWall", "InteriorWallTrim", "Floor", "GrimeFloor", "GrimeWall", "Ceiling"];

    static XElement WriteTbx(Building b, Report report)
    {
        var root = new XElement("building", A("version", 4), A("width", b.Width), A("height", b.Height));
        foreach (var name in BuildingTiles) root.Add(A(name, 0));
        var entries = new List<XElement>();
        int Entry(string category, string tile, params string[] enums)
        {
            var element = new XElement("tile_entry", A("category", category), enums.Select(e => new XElement("tile", A("enum", e), A("tile", Tile(tile)))));
            var existing = entries.FindIndex(e => XNode.DeepEquals(e, element));
            if (existing >= 0) return existing + 1;
            entries.Add(element); return entries.Count;
        }
        var roomElements = new List<XElement>(); var floors = new List<XElement>();
        var allTiles = b.Design!.Floors.SelectMany(f => Enumerable.Range(0, f.Cells.Length).Where(i => f.Cells[i] != '.').Select(i => FloorResource(b, f, i))
            .Concat(f.Objects.Where(o => o.Kind != "stairs").SelectMany(BuildingObjects.Parts).Select(p => p.Tile))).Select(Tile).Distinct().Order(StringComparer.Ordinal).ToArray();
        var userIds = allTiles.Select((t, i) => (t, i)).ToDictionary(p => p.t, p => (uint)p.i + 1);
        foreach (var f in b.Design.Floors)
        {
            var rooms = new Dictionary<char, uint>();
            foreach (var pair in f.Rooms.OrderBy(r => r.Key, StringComparer.Ordinal).Where(r => f.Cells.Contains(r.Key[0])))
            {
                var room = new XElement("room", A("Name", pair.Value), A("InternalName", pair.Value == "balcony" ? "emptyoutside" : pair.Value), A("Color", "160 180 200"));
                foreach (var name in RoomTiles) room.Add(A(name, 0));
                roomElements.Add(room); rooms[pair.Key[0]] = (uint)roomElements.Count;
            }
            var floor = new XElement("floor");
            foreach (var edge in f.Edges.OrderBy(e => e.Y).ThenBy(e => e.X).ThenBy(e => e.Edge, StringComparer.Ordinal))
            {
                if (edge.Kind == "open") continue;
                var orient = edge.Edge == "n" ? "North" : "West";
                var wall = Entry("exterior_walls", WallResource(b, f, edge), orient, orient + "Door", orient + "Window");
                // WallObject direction describes the line: W runs west-to-east (a north edge).
                floor.Add(new XElement("object", A("type", "wall"), A("x", edge.X), A("y", edge.Y), A("dir", edge.Edge == "n" ? "W" : "N"),
                    A("length", 1), A("Tile", wall), A("InteriorTile", 0), A("ExteriorTrim", 0), A("InteriorTrim", 0)));
                if (edge.Kind is "door" or "window")
                    floor.Add(new XElement("object", A("type", edge.Kind), A("x", edge.X), A("y", edge.Y), A("dir", edge.Edge.ToUpperInvariant()),
                        A("Tile", Entry(edge.Kind == "door" ? "doors" : "windows", OpeningResource(b, edge), orient)),
                        edge.Kind == "door" ? new[] { A("FrameTile", 0) } : new[] { A("CurtainsTile", 0), A("ShuttersTile", 0) }));
                if (edge.OverlayTile is not null) UnsupportedOverlay(report, "/floor/edges");
            }
            // BuildingEd FlatTop/Three places the surface one floor above this object.
            // Restrict to a uniform indoor rectangle: no guessed terraces, cutouts or bounding-box fill.
            if (ReferenceEquals(f, b.Design.Floors[^1]) && b.Design.Roof == "flat" && FullIndoorRectangle(b))
                floor.Add(new XElement("object", A("type", "roof"), A("x", 0), A("y", 0), A("width", b.Width), A("height", b.Height),
                    A("RoofType", "FlatTop"), A("Depth", "Three"), A("cappedW", "false"), A("cappedN", "false"), A("cappedE", "false"), A("cappedS", "false"),
                    A("CapTiles", 0), A("SlopeTiles", 0), A("TopTiles", Entry("roof_tops", b.Design.FlatRoofTile ?? "floors_interior_tilesandwood_01_40", "West1", "West2", "West3", "North1", "North2", "North3"))));
            floor.Add(new XElement("rooms", CsvText(f.Cells.Select(c => c == '.' ? 0 : rooms[c]))));
            var surface = new uint[(b.Width + 1) * (b.Height + 1)];
            for (var i = 0; i < f.Cells.Length; i++) if (f.Cells[i] != '.') surface[(i / b.Width) * (b.Width + 1) + i % b.Width] = userIds[Tile(FloorResource(b, f, i))];
            floor.Add(new XElement("tiles", A("layer", "Floor"), CsvText(surface)));
            // Native user tile layers are (width+1)*(height+1), not the room grid size.
            var objectLayers = new List<uint[]>();
            foreach (var obj in f.Objects.Where(o => o.Kind != "stairs")) foreach (var p in BuildingObjects.Parts(obj))
            {
                var x = checked(obj.X + p.Dx); var y = checked(obj.Y + p.Dy);
                RequireObjectCell(b, f, x, y);
                var index = y * (b.Width + 1) + x;
                var layer = objectLayers.FirstOrDefault(l => l[index] == 0);
                if (layer is null) { layer = new uint[surface.Length]; objectLayers.Add(layer); }
                layer[index] = userIds[Tile(p.Tile)];
            }
            if (objectLayers.Count > 4) report.Add("stacked-object", "/floor/objects", "More than four stacked furniture tile layers; extra layers omitted.");
            foreach (var (layer, index) in objectLayers.Take(4).Select((l, i) => (l, i)))
                floor.Add(new XElement("tiles", A("layer", index == 0 ? "Furniture" : "Furniture" + (index + 1)), CsvText(layer)));
            floors.Add(floor);
        }
        root.Add(entries, new XElement("user_tiles", allTiles.Select(t => new XElement("tile", A("tile", t)))), roomElements, floors);
        return root;
    }

    static Building ReadTbx(XElement root, Report report)
    {
        if (root.Name != "building") throw new ArgumentException("TBX requires a building root.");
        Int(root, "version", 4, 7); // v4 writer structure, unchanged references in readers through v7.
        var b = Empty(Int(root, "width", 2, MaxDimension), Int(root, "height", 2, MaxDimension));
        UnknownChildren(root, report, "tile_entry", "user_tiles", "used_tiles", "used_furniture", "furniture", "room", "floor", "properties");
        var entries = root.Elements("tile_entry").ToArray();
        if (entries.Length > 8192) throw new ArgumentException("More than 8192 tile entries.");
        var entryTiles = entries.Select(e =>
        {
            if (S(e, "category") is not ("floors" or "exterior_walls" or "interior_walls" or "doors" or "windows" or "roof_tops"))
                report.Add("tile-category", "/tile_entry/@category", "Tile category is outside the supported floors/walls/doors/windows subset.");
            UnknownChildren(e, report, "tile");
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in e.Elements("tile"))
            {
                if (S(t, "offset", "0,0") != "0,0") report.Add("tile-offset", "/tile_entry/tile", "Tile entry offsets are unsupported.");
                var tile = S(t, "tile");
                if (tile.Length != 0 && tile != "none") dict.Add(S(t, "enum"), Tile(tile));
            }
            return dict;
        }).ToArray();
        string? Resolve(XElement owner, string attribute, string shape)
        {
            var index = Int(owner, attribute, 0, entries.Length, 0);
            if (index == 0) return null;
            var expected = attribute switch { "Floor" => "floors", "ExteriorWall" => "exterior_walls", "InteriorWall" => "interior_walls", "Door" => "doors", "Window" => "windows", _ => S(owner, "type") == "wall" ? "exterior_walls" : S(owner, "type") == "door" ? "doors" : "windows" };
            if (S(entries[index - 1], "category") != expected) throw new ArgumentException("Referenced tile entry has the wrong category.");
            var found = entryTiles[index - 1].GetValueOrDefault(shape);
            if (found is null) report.Add("tile-shape", "/" + owner.Name + "/@" + attribute, "Referenced entry has no supported " + shape + " tile.");
            return found;
        }
        foreach (var attr in BuildingTiles.Where(n => n is not ("ExteriorWall" or "Door" or "Window")))
            if (Int(root, attr, 0, entries.Length, 0) != 0) report.Add("building-palette", "/building/@" + attr, "Additional building palette/trim/roof settings are not retained.");
        ReadProperties(root.Element("properties"), report);
        var roomDefs = root.Elements("room").ToArray();
        if (roomDefs.Length > 2240) throw new ArgumentException("Too many room definitions.");
        foreach (var room in roomDefs)
        {
            UnknownChildren(room, report);
            foreach (var attr in RoomTiles.Where(n => n is not ("InteriorWall" or "Floor")))
                if (Int(room, attr, 0, entries.Length, 0) != 0) report.Add("room-decoration", "/room/@" + attr, "Room ceiling, trim and grime settings are omitted.");
            if (S(room, "Name") != S(room, "InternalName")) report.Add("room-display-name", "/room/@Name", "Display name is omitted; the internal room name is retained.");
        }
        var userContainers = root.Elements("user_tiles").ToArray();
        if (userContainers.Length > 1) throw new ArgumentException("Duplicate user_tiles.");
        var users = userContainers.SelectMany(e => e.Elements("tile")).Select(e => Tile(S(e, "tile"))).ToArray();
        if (users.Length > 32768) throw new ArgumentException("Too many user tiles.");
        var floorDefs = root.Elements("floor").ToArray();
        if (floorDefs.Length is < 1 or > 32) throw new ArgumentException("TBX supports 1..32 local floors, starting at zero.");
        if ((long)floorDefs.Length * b.Width * b.Height + (long)floorDefs.Sum(f => f.Elements("tiles").Count()) * (b.Width + 1) * (b.Height + 1) > MaxDecodedTiles)
            throw new ArgumentException("Decoded grids exceed one million tile/room values.");
        var roofObjects = new List<(XElement Object, int Level)>();
        foreach (var element in floorDefs)
        {
            if (element.Attribute("level") is not null) report.Add("floor-level", "/floor/@level", "TBX uses element order; explicit level attributes are unsupported.");
            UnknownChildren(element, report, "object", "rooms", "tiles", "attributes");
            var roomsElements = element.Elements("rooms").ToArray();
            if (roomsElements.Length != 1) throw new ArgumentException("Each floor requires exactly one rooms grid.");
            var indices = Csv(roomsElements[0].Value, b.Width * b.Height);
            var used = indices.Where(i => i != 0).Distinct().Order().ToArray();
            if (used.Length is < 1 or > 35 || used.Any(i => i > roomDefs.Length)) throw new ArgumentException("Invalid room references or more than 35 rooms on a floor.");
            var codes = used.Select((id, i) => (id, code: RoomCodes[i])).ToDictionary(p => p.id, p => p.code);
            var f = new BuildingFloor { Cells = new(indices.Select(i => i == 0 ? '.' : codes[i]).ToArray()) };
            foreach (var id in used) f.Rooms[codes[id].ToString()] = RoomName(S(roomDefs[id - 1], "InternalName"), report, "/room/@InternalName");
            b.Design!.Floors.Add(f);
            f.Edges = BuildingDesigner.AutoEdges(b, f, b.Design.Floors.Count - 1);
            var automatic = new Dictionary<(int, int, string), (XElement Owner, string Attribute)>();
            foreach (var edge in f.Edges)
            {
                var a = BuildingDesigner.Cell(b, f, edge.X, edge.Y);
                var other = BuildingDesigner.Cell(b, f, edge.X - (edge.Edge == "w" ? 1 : 0), edge.Y - (edge.Edge == "n" ? 1 : 0));
                var roomId = indices[(a == '.' ? edge.Y - (edge.Edge == "n" ? 1 : 0) : edge.Y) * b.Width + (a == '.' ? edge.X - (edge.Edge == "w" ? 1 : 0) : edge.X)];
                edge.Kind = "wall";
                var owner = a == '.' || other == '.' ? root : roomDefs[roomId - 1];
                var attribute = a == '.' || other == '.' ? "ExteriorWall" : "InteriorWall";
                automatic[(edge.X, edge.Y, edge.Edge)] = (owner, attribute);
                edge.WallTile = Resolve(owner, attribute, edge.Edge == "n" ? "North" : "West");
                if (a != '.' && other != '.')
                {
                    var otherIndex = (edge.Y - (edge.Edge == "n" ? 1 : 0)) * b.Width + edge.X - (edge.Edge == "w" ? 1 : 0);
                    if (Int(roomDefs[indices[otherIndex] - 1], "InteriorWall", 0, entries.Length, 0) != Int(owner, "InteriorWall", 0, entries.Length, 0))
                        report.Add("wall-faces", "/room/@InteriorWall", "Different materials on opposite room faces cannot both be represented; the south/east face is retained.");
                }
                if (edge.WallTile is null) edge.Kind = "open";
            }
            f.Edges.RemoveAll(e => e.Kind == "open");
            for (var i = 0; i < indices.Length; i++) if (indices[i] > 0 && Resolve(roomDefs[indices[i] - 1], "Floor", "Floor") is { } tile) f.FloorTiles[i] = tile;
            // Explicit line walls precede openings, independently of document order.
            var objects = element.Elements("object").ToArray();
            if (objects.Length > 16384) throw new ArgumentException("Too many floor objects.");
            var explicitWalls = new HashSet<(int, int, string)>();
            var explicitOpenings = new HashSet<(int, int, string)>();
            foreach (var obj in objects.OrderBy(o => S(o, "type") == "wall" ? 0 : 1))
            {
                var type = S(obj, "type"); var x = Int(obj, "x", 0, b.Width); var y = Int(obj, "y", 0, b.Height); var dir = S(obj, "dir");
                if (type is "wall" or "door" or "window")
                {
                    if (dir is not ("N" or "W")) { report.Add("object-direction", "/floor/object/@dir", "Only north and west directions are supported."); continue; }
                    var direction = type == "wall" ? dir == "W" ? "n" : "w" : dir.ToLowerInvariant();
                    var shape = direction == "n" ? "North" : "West";
                    if (type == "wall")
                    {
                        var length = Int(obj, "length", 1, MaxDimension);
                        var tile = Resolve(obj, "Tile", shape);
                        foreach (var attr in new[] { "InteriorTile", "ExteriorTrim", "InteriorTrim" }) if (Int(obj, attr, 0, entries.Length, 0) != 0) report.Add("wall-interior", "/floor/object/@" + attr, "Separate interior-facing wall tiles and trim are omitted.");
                        for (var i = 0; i < length; i++)
                        {
                            var xx = x + (direction == "n" ? i : 0); var yy = y + (direction == "w" ? i : 0);
                            if (!explicitWalls.Add((xx, yy, direction))) throw new ArgumentException("Overlapping explicit wall objects.");
                            automatic.Remove((xx, yy, direction));
                            Edge(b, f, xx, yy, direction, tile is null ? "open" : "wall", tile);
                        }
                    }
                    else
                    {
                        if (!explicitOpenings.Add((x, y, direction))) throw new ArgumentException("Overlapping opening objects.");
                        var opening = Resolve(obj, "Tile", shape) ?? Resolve(root, type == "door" ? "Door" : "Window", shape);
                        var edge = f.Edges.FirstOrDefault(e => e.X == x && e.Y == y && e.Edge == direction);
                        if (edge is not null && automatic.TryGetValue((x, y, direction), out var palette))
                        {
                            edge.WallTile = Resolve(palette.Owner, palette.Attribute, shape + (type == "door" ? "Door" : "Window"));
                        }
                        if (opening is null) report.Add("opening-tile", "/floor/object", "Opening has no representable sprite; lossy import uses the editor default.");
                        Edge(b, f, x, y, direction, type, opening: opening);
                        foreach (var attr in new[] { "FrameTile", "CurtainsTile", "ShuttersTile" }) if (Int(obj, attr, 0, entries.Length, 0) != 0) report.Add("opening-decoration", "/floor/object/@" + attr, "Door frames, curtains and shutters are omitted.");
                    }
                }
                else if (type == "furniture")
                {
                    var furniture = root.Elements("furniture").ToArray();
                    var index = Int(obj, "FurnitureTiles", 0, furniture.Length - 1);
                    var entry = furniture[index].Elements("entry").SingleOrDefault(e => S(e, "orient") == S(obj, "orient"));
                    if (entry is null) throw new ArgumentException("Missing furniture orientation.");
                    var overlay = S(furniture[index], "layer", "Furniture").StartsWith("WallOverlay", StringComparison.Ordinal);
                    if (overlay) UnsupportedOverlay(report, "/furniture/@layer");
                    else if (S(furniture[index], "layer", "Furniture") != "Furniture") report.Add("furniture-layer", "/furniture/@layer", "Furniture layer is flattened to object tiles.");
                    report.Add("object-grouping", "/floor/object", "Native furniture grouping and grime settings are flattened to individual object tiles.");
                    foreach (var tile in entry.Elements("tile"))
                    {
                        var xx = x + Int(tile, "x", 0, 7); var yy = y + Int(tile, "y", 0, 7);
                        RequireObjectCell(b, f, xx, yy);
                        if (!overlay) ObjectTile(b, f, xx, yy, Tile(S(tile, "name")), report);
                    }
                }
                else if (type == "roof")
                {
                    var width = Int(obj, "width", 1, MaxDimension); var height = Int(obj, "height", 1, MaxDimension);
                    if (x + width > b.Width || y + height > b.Height) throw new ArgumentException("Roof object overhang crosses the building envelope; enlarge the envelope explicitly before exchange.");
                    roofObjects.Add((obj, b.Design.Floors.Count - 1));
                }
                else report.Add("object-type", "/floor/object", "Unsupported TBX object type: " + type[..Math.Min(type.Length, 80)]);
            }
            var layers = element.Elements("tiles").ToArray();
            if (layers.Length > 32) throw new ArgumentException("More than 32 user layers on one floor.");
            if (layers.Select(e => S(e, "layer")).Distinct().Count() != layers.Length) throw new ArgumentException("Duplicate TBX user tile layers.");
            foreach (var layer in layers)
            {
                var data = Csv(layer.Value, (b.Width + 1) * (b.Height + 1));
                var tiles = data.Select(i => i == 0 ? null : i <= users.Length ? users[i - 1] : throw new ArgumentException("Invalid user tile reference.")).ToArray();
                ReadTiles(b, f, S(layer, "layer"), tiles, b.Width + 1, report);
            }
            foreach (var attributes in element.Elements("attributes")) if (attributes.Value.Any(c => c is not ('0' or ',' or ' ' or '\r' or '\n' or '\t')))
                report.Add("square-attributes", "/floor/attributes", "Square attributes are omitted.");
            foreach (var i in Enumerable.Range(0, f.Cells.Length).Where(i => f.Cells[i] != '.' && !f.FloorTiles.ContainsKey(i)))
            { report.Add("missing-floor-tile", "/floor", "Room cells without floor sprites use the editor default on lossy import."); break; }
        }
        if (roofObjects.Count > 0)
        {
            var (roof, level) = roofObjects[0];
            var top = Int(roof, "TopTiles", 0, entries.Length, 0);
            var shapes = new[] { "West1", "West2", "West3", "North1", "North2", "North3" };
            var tiles = top == 0 ? [] : entryTiles[top - 1].Values.Distinct(StringComparer.Ordinal).ToArray();
            if (roofObjects.Count == 1 && level == floorDefs.Length - 1 && FullIndoorRectangle(b) &&
                S(roof, "RoofType") == "FlatTop" && S(roof, "Depth") == "Three" &&
                Int(roof, "x", 0, b.Width) == 0 && Int(roof, "y", 0, b.Height) == 0 &&
                Int(roof, "width", 1, b.Width) == b.Width && Int(roof, "height", 1, b.Height) == b.Height &&
                new[] { "cappedW", "cappedN", "cappedE", "cappedS" }.All(n => S(roof, n) is "false" or "0") &&
                Int(roof, "CapTiles", 0, entries.Length, 0) == 0 && Int(roof, "SlopeTiles", 0, entries.Length, 0) == 0 &&
                top > 0 && S(entries[top - 1], "category") == "roof_tops" && tiles.Length == 1 && shapes.All(entryTiles[top - 1].ContainsKey))
            { b.Design!.Roof = "flat"; b.Design.FlatRoofTile = tiles[0]; }
            else report.Add("roof", "/floor/object", "Only one uncapped FlatTop/Three roof covering the full indoor rectangle on the last floor, with a uniform roof_tops entry, is supported. Other roof geometry is omitted only with explicit lossy import.");
        }
        return b;
    }

    static void ReadProperties(XElement? properties, Report report)
    {
        if (properties?.HasElements == true) report.Add("properties", "/properties", "Custom properties (including loot and basement-access metadata) are omitted.");
    }
}
