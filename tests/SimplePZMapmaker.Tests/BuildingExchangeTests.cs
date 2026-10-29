using System.Text.Json;
using System.Xml.Linq;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Exchange;
using SimplePZMapmaker.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

static class BuildingExchangeTests
{
    // Independent fixtures transcribed from the upstream writer/reader contracts, not our exporter.
    const string Tbx = """
        <building version="4" width="2" height="2" ExteriorWall="2">
          <tile_entry category="floors"><tile enum="Floor" tile="floors_interior_tilesandwood_01_040"/></tile_entry>
          <tile_entry category="exterior_walls"><tile enum="West" tile="walls_exterior_house_01_0"/><tile enum="North" tile="walls_exterior_house_01_1"/><tile enum="NorthDoor" tile="walls_exterior_house_01_11"/></tile_entry>
          <tile_entry category="doors"><tile enum="North" tile="fixtures_doors_01_1"/></tile_entry>
          <user_tiles><tile tile="furniture_storage_01_0"/></user_tiles>
          <room Name="hall" InternalName="hall" Color="100 120 140" Floor="1" InteriorWall="0"/>
          <floor><object type="door" x="0" y="0" dir="N" Tile="3" FrameTile="0"/>
            <rooms>1,1,1,1</rooms><tiles layer="Furniture">0,0,0,0,1,0,0,0,0</tiles>
          </floor>
        </building>
        """;
    const string Tmx = """
        <map version="1.0" orientation="levelisometric" width="3" height="3" tilewidth="64" tileheight="32">
          <tileset firstgid="1" name="floors_interior_tilesandwood_01" tilewidth="64" tileheight="128" tilecount="64" columns="8"><image source="floors.png" width="512" height="1024"/></tileset>
          <tileset firstgid="65" name="walls_exterior_house_01" tilewidth="64" tileheight="128" tilecount="64" columns="8"><image source="walls.png" width="512" height="1024"/></tileset>
          <tileset firstgid="129" name="fixtures_doors_01" tilewidth="64" tileheight="128" tilecount="16" columns="8"><image source="doors.png" width="512" height="256"/></tileset>
          <tileset firstgid="145" name="furniture_storage_01" tilewidth="64" tileheight="128" tilecount="64" columns="8"><image source="storage.png" width="512" height="1024"/></tileset>
          <layer name="0_Floor" level="0" width="3" height="3"><data encoding="csv">41,41,0,41,41,0,0,0,0</data></layer>
          <layer name="0_Walls" level="0" width="3" height="3"><data encoding="csv">76,0,65,0,0,0,0,0,0</data></layer>
          <layer name="0_Doors" level="0" width="3" height="3"><data encoding="csv">130,0,0,0,0,0,0,0,0</data></layer>
          <layer name="0_Furniture" level="0" width="3" height="3"><data encoding="csv">0,0,0,0,145,0,0,0,0</data></layer>
          <objectgroup name="RoomDefs" level="0"><object name="hall#1" type="room" x="0" y="0" width="128" height="64"/></objectgroup>
        </map>
        """;

    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("building exchange reads independent native TBX and TMX fixtures", () =>
        {
            foreach (var (fixture, format) in new[] { (Tbx, "tbx"), (Tmx, "tmx") })
            {
                var result = BuildingExchange.Import(fixture, format); var b = Imported(result);
                Check(b.Width == 2 && b.Height == 2 && b.Design!.Floors.Single().Cells == "0000", "Room grid lost.");
                var f = b.Design!.Floors[0];
                Check(f.FloorTiles.Count == 4 && f.FloorTiles[0] == "floors_interior_tilesandwood_01_40", "Tile index/GID mapping failed.");
                Check(f.Objects.Single().Parts.Single().Tile == "furniture_storage_01_0", "Object tile lost.");
                var door = f.Edges.Single(e => e.X == 0 && e.Y == 0 && e.Edge == "n");
                Check(door.Kind == "door" && door.OpeningTile == "fixtures_doors_01_1" && door.WallTile == "walls_exterior_house_01_11", "Opening orientation/shape lost.");
                foreach (var target in new[] { "tbx", "tmx" })
                {
                    var xml = Exported(BuildingExchange.Export(b, target));
                    var again = Imported(BuildingExchange.Import(xml, target));
                    Check(Exported(BuildingExchange.Export(again, target)) == xml, "Canonical export/import/export is not deterministic.");
                    Check(BuildingExchange.Import(xml, target).Building!.Id == again.Id, "Import identity is nondeterministic.");
                }
            }
        });
        yield return ("building exchange native wall object line direction and padded tile grids", () =>
        {
            var b = Imported(BuildingExchange.Import(Tmx, "tmx"));
            var xml = Exported(BuildingExchange.Export(b, "tbx")); var root = XElement.Parse(xml);
            var north = root.Element("floor")!.Elements("object").Single(e => (string?)e.Attribute("type") == "wall" && (string?)e.Attribute("x") == "0");
            Check((string?)north.Attribute("dir") == "W", "North wall must use native W line direction.");
            Check(root.Element("floor")!.Elements("tiles").All(e => e.Value.Split(',').Length == 9), "User tile grid needs the extra wall border.");
        });
        yield return ("building exchange signed TMX levels and TBX basement rejection are explicit", () =>
        {
            var b = Imported(BuildingExchange.Import(Tmx, "tmx"));
            b.Design!.MinLevel = -1; b.Design.Floors.Add(JsonSerializer.Deserialize<BuildingFloor>(JsonSerializer.Serialize(b.Design.Floors[0]))!);
            var xml = Exported(BuildingExchange.Export(b, "tmx"));
            var copy = Imported(BuildingExchange.Import(xml, "tmx"));
            Check(copy.Design!.MinLevel == -1 && copy.Design.Floors.Count == 2 && xml.Contains("level=\"-1\"") && xml.Contains("version=\"2.0\""), "Signed level lost.");
            Check(!BuildingExchange.Export(b, "tbx", true).Success, "TBX silently shifted basement levels.");
        });
        yield return ("building exchange unsupported details block unless explicitly accepted and never mutate source", () =>
        {
            var b = Imported(BuildingExchange.Import(Tmx, "tmx")); b.Design!.Roof = "gabled";
            var before = JsonSerializer.Serialize(b);
            var blocked = BuildingExchange.Export(b, "tbx");
            Check(!blocked.Success && blocked.Diagnostics.Any(d => d.Code == "roof"), "Roof loss hidden.");
            Check(BuildingExchange.Export(b, "tbx", true).Success && before == JsonSerializer.Serialize(b), "Lossy export mutated source.");
            var unsupported = Tbx.Replace("</floor>", "<mystery value=\"unhandled\"/></floor>");
            Check(!BuildingExchange.Import(unsupported, "tbx").Success, "Unsupported element accepted implicitly.");
            Check(BuildingExchange.Import(unsupported, "tbx", true).Success, "Explicit partial import unavailable.");
            b.Design.Roof = "none"; b.Design.Floors[0].Rooms["0"] = "balcony";
            b.Design.Floors[0].Edges.Single(e => e.X == 2 && e.Y == 0 && e.Edge == "w").WallTile = null;
            var balcony = Exported(BuildingExchange.Export(b, "tbx"));
            Check(balcony.Contains("carpentry_02_100"), "Automatic balcony railing silently became a full wall.");
        });
        yield return ("building exchange rejects DTD entities oversized input depth dimensions and malformed references", () =>
        {
            foreach (var xml in new[] { "<!DOCTYPE building [<!ENTITY x SYSTEM 'file:///C:/secret'>]>" + Tbx,
                "<!DOCTYPE building SYSTEM 'https://invalid.example/tbx.dtd'>" + Tbx,
                Tbx.Replace("width=\"2\"", "width=\"65\""), Tbx.Replace("1,1,1,1", "1,1,99,1"),
                Tbx.Replace("1,1,1,1", "1,1,1"), Tbx.Replace("</floor>", "<x>" + string.Concat(Enumerable.Repeat("<x>", 20)) + string.Concat(Enumerable.Repeat("</x>", 20)) + "</x></floor>"),
                new string(' ', BuildingExchange.MaxInputBytes + 1), Tbx.Replace("version=\"4\"", "version=\"99\"") })
                Check(!BuildingExchange.Import(xml, "tbx", true).Success, "Unsafe XML accepted.");
        });
        yield return ("building exchange rejects external tilesets compression infinite chunks transforms and bad GIDs", () =>
        {
            var variants = new[] {
                Tmx.Replace("<tileset firstgid=\"1\"", "<tileset source=\"https://invalid.example/tiles.tsx\" firstgid=\"1\""),
                Tmx.Replace("encoding=\"csv\"", "encoding=\"csv\" compression=\"gzip\""),
                Tmx.Replace("<map ", "<map infinite=\"1\" "), Tmx.Replace("41,41,0,41,41", "2147483689,41,0,41,41"),
                Tmx.Replace("41,41,0,41,41", "999999,41,0,41,41"), Tmx.Replace("name=\"0_Floor\"", "name=\"0_Floor\" offsetx=\"1\""),
                Tmx.Replace("</map>", "<group name=\"nested\"/></map>"), Tmx.Replace("</data>", "<chunk x=\"0\" y=\"0\"/></data>"),
                Tmx.Replace("firstgid=\"65\"", "firstgid=\"2\""), Tmx.Replace("name=\"0_Floor\" level=\"0\"", "name=\"-1_Floor\" level=\"0\"") };
            foreach (var xml in variants) Check(!BuildingExchange.Import(xml, "tmx", true).Success, "Unsupported TMX structural feature accepted.");
        });
        yield return ("building exchange XML tile encoding preserves GIDs and rejects half tile rooms", () =>
        {
            var root = XElement.Parse(Tmx);
            foreach (var data in root.Descendants("data")) { var gids = data.Value.Split(','); data.RemoveAttributes(); data.ReplaceNodes(gids.Select(v => new XElement("tile", new XAttribute("gid", v)))); }
            Imported(BuildingExchange.Import(root.ToString(), "tmx"));
            Check(!BuildingExchange.Import(Tmx.Replace("width=\"128\" height=\"64\"", "width=\"127\" height=\"64\""), "tmx", true).Success, "Subtile room silently rounded.");
        });
        yield return ("building exchange native TileZed 2.0 uses bare names and mandatory signed level attributes", () =>
        {
            var native = Tmx.Replace("version=\"1.0\"", "version=\"2.0\"").Replace("name=\"0_", "name=\"");
            var b = Imported(BuildingExchange.Import(native, "tmx"));
            var xml = Exported(BuildingExchange.Export(b, "tmx"));
            Check(XElement.Parse(xml).Elements("layer").All(e => !((string)e.Attribute("name")!).StartsWith("0_")), "Legacy prefixes leaked into native 2.0 names.");
            Check(!BuildingExchange.Import(native.Replace(" level=\"0\"", ""), "tmx", true).Success, "Native level attribute is mandatory.");
        });
        yield return ("building exchange bounds decoded grids and reports unknown attributes", () =>
        {
            var root = XElement.Parse(Tmx); var layer = root.Element("layer")!;
            for (var i = 0; i < 238; i++) root.Add(new XElement(layer));
            root.SetAttributeValue("width", 65); root.SetAttributeValue("height", 65);
            Check(!BuildingExchange.Import(root.ToString(), "tmx", true).Success, "Decoded grid budget ignored.");
            Check(!BuildingExchange.Import(Tbx.Replace("<floor>", "<floor invented=\"value\">"), "tbx").Success, "Unknown attribute silently dropped.");
            Check(!BuildingExchange.Import(Tbx.Replace("Color=\"100 120 140\"", "Color=\"" + new string('x', 4097) + "\""), "tbx", true).Success, "Oversized XML attribute accepted.");
        });
        yield return ("building exchange HTTP preview endpoints bound requests and return atomic diagnostic results", () => VerifyHttp().GetAwaiter().GetResult());
    }
    static async Task VerifyHttp()
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        await using var app = builder.Build(); app.MapBuildingExchangeEndpoints(); await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var response = await client.PostAsJsonAsync("/api/building/exchange/import", new { format = "tbx", xml = Tbx });
            Check(response.IsSuccessStatusCode, "Import route unavailable.");
            var result = await response.Content.ReadFromJsonAsync<BuildingExchangeResult>(); var b = Imported(result!);
            using var exported = await client.PostAsJsonAsync("/api/building/exchange/export", new { format = "tmx", building = b });
            Exported((await exported.Content.ReadFromJsonAsync<BuildingExchangeResult>())!);
            using var blocked = await client.PostAsJsonAsync("/api/building/exchange/import", new { format = "tbx", xml = Tbx.Replace("<floor>", "<floor unknown=\"x\">") });
            var failure = await blocked.Content.ReadFromJsonAsync<BuildingExchangeResult>();
            Check(failure is { Success: false, Building: null } && failure.Diagnostics.Count > 0, "Partial candidate escaped on a blocked import.");
            using var invalid = await client.PostAsync("/api/building/exchange/import", new StringContent("not json"));
            Check(invalid.StatusCode == HttpStatusCode.UnsupportedMediaType, "Content type not checked.");
            using var oversize = new HttpRequestMessage(HttpMethod.Post, "/api/building/exchange/import") { Content = new StringContent(new string(' ', BuildingExchangeEndpoints.MaxRequestBytes + 1), System.Text.Encoding.UTF8, "application/json") };
            oversize.Headers.ExpectContinue = true;
            using var tooBig = await client.SendAsync(oversize);
            Check(tooBig.StatusCode == HttpStatusCode.RequestEntityTooLarge, "Request byte bound not enforced.");
        }
        finally { await app.StopAsync(); }
    }
    static Building Imported(BuildingExchangeResult result) => result.Building ?? throw new Exception("Import failed: " + JsonSerializer.Serialize(result.Diagnostics));
    static string Exported(BuildingExchangeResult result) => result.Xml ?? throw new Exception("Export failed: " + JsonSerializer.Serialize(result.Diagnostics));
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
