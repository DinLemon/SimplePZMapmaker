using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Diagnostics;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class ProjectDoctorTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("doctor aggregates independent zone and cell failures without modifying input", () =>
        {
            var p = Blank();
            p.GameZones.Add(new() { X = 8, Y = 8, Width = 8, Height = 8 });
            p.GameZones.Add(new() { X = 9, Y = 9, Width = 8, Height = 8 });
            var before = JsonSerializer.Serialize(p);
            var report = ProjectDoctor.Analyze(new() { Project = p }, new()
            {
                InstalledMaps = [new("other", "mods", [(p.WorldX, p.WorldY)])]
            });
            Check(report.Findings.Any(f => f.Category == "zones" && f.Severity == "error"), "Zone failure lost");
            Check(report.Findings.Any(f => f.Category == "cell-conflicts" && f.Location?.CellX == p.WorldX), "Cell conflict location lost");
            Check(before == JsonSerializer.Serialize(p), "Doctor changed submitted authoring");
        });
        yield return ("doctor offline and cropped checks never claim complete coverage", () =>
        {
            var report = ProjectDoctor.Analyze(new() { Project = Blank() });
            Check(!report.Complete && report.Status != "pass", "Offline report passed completely");
            Check(report.Checks.Single(c => c.Category == "resources").Status == "unavailable", "Offline resources passed");
            Check(report.Checks.Single(c => c.Category == "cell-conflicts").Status == "unavailable", "Unscanned installed maps passed");
            Check(report.Checks.Single(c => c.Category == "paths").Status == "unsupported", "External source paths passed");
            Check(report.Checks.Single(c => c.Category == "access").Status == "partial", "Default crop claimed full map");
        });
        yield return ("doctor rejects unbounded cell dimensions without enumerating cells", () =>
        {
            var p = Blank(); p.Width = int.MaxValue; p.Height = int.MaxValue;
            var report = ProjectDoctor.Analyze(new() { Project = p }, new() { InstalledMaps = [] });
            Check(report.Checks.Single(c => c.Category == "cell-conflicts").Status == "fail", "Invalid dimensions reached cell enumeration");
        });
        yield return ("doctor isolates mutations from existing resource validators", () =>
        {
            var p = Blank(); var before = JsonSerializer.Serialize(p);
            var report = ProjectDoctor.Analyze(new() { Project = p }, new()
            {
                ValidateMapResources = copy => { copy.Name = "mutated"; copy.AssetSources["test_0"] = "mod"; }
            });
            Check(report.Checks.Single(c => c.Category == "resources").Status == "pass", "Resource adapter failed");
            Check(before == JsonSerializer.Serialize(p), "Resource validation mutated original");
        });
        yield return ("doctor aggregates existing seam warnings and stable report ordering", () =>
        {
            var a = Blank(); a.WorldX = a.WorldY = 0;
            var b = Blank(); b.WorldX = 1; b.WorldY = 0; b.Terrain[0] = Terrain.Water;
            var w = new WorldProject { Maps = [new() { Project = a }, new() { Project = b }] };
            var request = new ProjectDoctorRequest { World = w, MemberId = w.Maps[0].Id };
            var before = JsonSerializer.Serialize(w);
            var report = ProjectDoctor.Analyze(request);
            Check(report.Findings.Any(f => f.Category == "seams" && f.Message.Contains("surfaces differ")), "Existing seam diagnostics missing");
            Check(JsonSerializer.Serialize(report) == JsonSerializer.Serialize(ProjectDoctor.Analyze(request)), "Report is nondeterministic");
            Check(before == JsonSerializer.Serialize(w), "Seam check changed world");
        });
        yield return ("doctor reports sealed room with existing access coordinates", () =>
        {
            var p = Blank();
            var b = new Building { X = 8, Y = 8, Width = 12, Height = 10 };
            b.Design = BuildingDesigner.Generate(b, 1, 42, "rect");
            b.Design.Floors[0].Objects.Clear();
            foreach (var edge in b.Design.Floors[0].Edges.Where(e => e.Kind == "door")) edge.Kind = "wall";
            p.Buildings.Add(b);
            var report = ProjectDoctor.Analyze(new() { Project = p });
            Check(report.Findings.Any(f => f.Category == "access" && f.Code == "unreachable-room" && f.Location is {} location && location.BuildingId == b.Id && location.X >= b.X), "Sealed room not located");
        });
        yield return ("doctor rejects ambiguous scope and honors cancellation", () =>
        {
            Reject(() => ProjectDoctor.Analyze(new()));
            Reject(() => ProjectDoctor.Analyze(new() { Project = Blank(), World = new() }));
            using var stop = new CancellationTokenSource(); stop.Cancel();
            try { ProjectDoctor.Analyze(new() { Project = Blank() }, token: stop.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Canceled doctor ran");
        });
        yield return ("tile snapshot preserves numeric IDs geometry empty slots and SHA256", () =>
        {
            var bytes = Definition(); using var stream = new MemoryStream(bytes);
            var old = TileDefinitionReader.Read(stream); stream.Position = 0;
            var snapshot = TileDefinitionSnapshot.Read(stream, "fixture");
            var set = snapshot.Tilesets.Single();
            Check(set.NumericId == 902 && set.Columns == 2 && set.Rows == 1 && set.StoredTileCount == 1, "Structural metadata lost");
            Check(set.Tiles.Count == 2 && set.Tiles[1].Id == 1 && !set.Tiles[1].Stored, "Empty slot renumbered");
            Check(old.Select(t => t.Id).SequenceEqual(set.Tiles.Select(t => set.Name + "_" + t.Id)), "Existing reader integration diverged");
            Check(snapshot.Sha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "Wrong source hash");
        });
        yield return ("tile comparator keeps numeric slots and separates properties from structure", () =>
        {
            var first = Snapshot(902, new Dictionary<string, string> { ["custom"] = "old", ["gone"] = "" });
            var second = Snapshot(903, new Dictionary<string, string> { ["custom"] = "new", ["added"] = "" });
            var report = TileDefinitionComparer.Compare(first, second);
            Check(report.Differences.Any(d => d.Kind == "structure" && d.Field == "numericId" && d.Before == "902" && d.After == "903"), "Numeric identity change hidden");
            Check(report.Differences.Any(d => d.Kind == "property" && d.TileId == 10 && d.Field == "custom" && d.Before == "old" && d.After == "new"), "Property diff lost stable slot");
            Check(report.Differences.Any(d => d.Field == "gone" && d.After is null) && report.Differences.Any(d => d.Field == "added" && d.Before is null), "Absent property conflated with empty string");
            Check(JsonSerializer.Serialize(report) == JsonSerializer.Serialize(TileDefinitionComparer.Compare(first, second)), "Comparison order unstable");
        });
        yield return ("tile comparator reports removed records and unresolved snow and burnt references", () =>
        {
            var first = Snapshot(902, new());
            var second = Snapshot(902, new Dictionary<string, string> { ["SnowTile"] = "snow_3", ["BurntTile"] = "burnt_2" });
            var report = TileDefinitionComparer.Compare(first, second);
            Check(report.Findings.Count(f => f.Code == "missing-replacement") == 2, "Missing replacement refs not reported");
            report = TileDefinitionComparer.Compare(first, second, id => id == "snow_3");
            Check(report.Findings.Count(f => f.Code == "missing-replacement") == 1 && report.Findings[0].Message.Contains("burnt_2"), "Installed catalogue resolution ignored");
            var empty = new TileDefinitionSnapshot("empty", null, true, []);
            Check(TileDefinitionComparer.Compare(first, empty).Differences.Any(d => d.Kind == "tileset-removed"), "Removed sheet lost");
        });
        yield return ("tile comparison ignores input collection order and rejects duplicate tile slots", () =>
        {
            var a = Snapshot(902, new Dictionary<string, string> { ["z"] = "1", ["a"] = "2" });
            var b = Snapshot(902, new Dictionary<string, string> { ["a"] = "2", ["z"] = "1" });
            Check(TileDefinitionComparer.Compare(a, b).Differences.Count == 0, "Property order became a difference");
            var sheet = a.Tilesets.Single();
            Reject(() => TileDefinitionComparer.Compare(a, a with { Tilesets = [sheet with { Tiles = [sheet.Tiles[0], sheet.Tiles[0]] }] }));
        });
        yield return ("tile property only and version zero identity remain partial with installed resolver", () =>
        {
            var binary = Snapshot(902, new());
            var properties = TileDefinitionSnapshot.FromProperties("text", [("fixture_10", (IReadOnlyDictionary<string, string>)new Dictionary<string, string>())]);
            var report = TileDefinitionComparer.Compare(binary, properties, _ => true);
            Check(!report.Complete && report.Notes.Any(n => n.Contains("Structural comparison unavailable")), "Property-only snapshot certified numeric identity");
            var legacy = binary with { Tilesets = [binary.Tilesets[0] with { NumericId = null }] };
            Check(!TileDefinitionComparer.Compare(legacy, legacy, _ => true).Complete, "Missing v0 numeric identity certified complete");
            var same = TileDefinitionComparer.Compare(binary, binary, _ => true);
            Check(same.Complete && same.Status == "pass", "Complete equal structural snapshots did not pass");
        });
        yield return ("tile snapshot rejects truncation trailing bytes and unsupported version", () =>
        {
            var data = Definition();
            Reject(() => TileDefinitionSnapshot.Read(new MemoryStream(data[..^1])));
            Reject(() => TileDefinitionSnapshot.Read(new MemoryStream(data.Concat(new byte[] { 0 }).ToArray())));
            data[4] = 2;
            Reject(() => TileDefinitionSnapshot.Read(new MemoryStream(data)));
        });
        yield return ("doctor upload JSON base64 preserves binary metadata and rejects invalid and oversized decoded input", () =>
        {
            var bytes = Definition();
            var snapshot = ProjectDoctorEndpoints.ReadUpload(new(Convert.ToBase64String(bytes), "../fixture.tiles"));
            Check(snapshot.Source == "fixture.tiles" && snapshot.Tilesets[0].NumericId == 902, "JSON upload lost metadata or treated label as path");
            Reject(() => ProjectDoctorEndpoints.ReadUpload(new("!!!!")));
            Reject(() => ProjectDoctorEndpoints.ReadUpload(new("dGRl Zg==")));
            Reject(() => ProjectDoctorEndpoints.ReadUpload(new(Convert.ToBase64String(bytes[..^1]))));
            // Same maximum encoded length, but no padding means one extra decoded byte; must reject before decoding.
            Reject(() => ProjectDoctorEndpoints.ReadUpload(new(new string('A', ((TileDefinitionSnapshot.MaximumBytes + 2) / 3) * 4))));
        });
        yield return ("doctor JSON endpoints run behind JSON and same-origin guard", () => WithCatalog((assets, root) =>
        {
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(assets); builder.Services.AddSingleton<StaticModelCatalog>();
            var app = builder.Build();
            // The production guard contract: no binary exception is installed for the doctor.
            app.Use(async (http, next) =>
            {
                if (http.Request.Method == "POST" && (!http.Request.HasJsonContentType() ||
                    http.Request.Headers.TryGetValue("Origin", out var origin) && origin.ToString() != $"http://{http.Request.Host}"))
                { http.Response.StatusCode = 403; return; }
                try { await next(); } catch (ArgumentException error) { http.Response.StatusCode = 400; await http.Response.WriteAsJsonAsync(new { error = error.Message }); }
            });
            app.MapProjectDoctor(); app.StartAsync().GetAwaiter().GetResult();
            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                client.DefaultRequestHeaders.Add("Origin", address.TrimEnd('/'));
                var snapshot = ProjectDoctorEndpoints.ReadUpload(new(Convert.ToBase64String(Definition())));
                using var upload = client.PostAsJsonAsync("/api/project-doctor/tiles/read", new ProjectDoctorTileUpload(Convert.ToBase64String(Definition()))).GetAwaiter().GetResult();
                Check(upload.IsSuccessStatusCode, "JSON base64 upload blocked by host guard");
                var body = upload.Content.ReadFromJsonAsync<TileDefinitionSnapshot>().GetAwaiter().GetResult()!;
                Check(body.Sha256 == snapshot.Sha256 && body.Tilesets.Single().NumericId == 902, "Endpoint response lost snapshot metadata");
                using var binary = client.PostAsync("/api/project-doctor/tiles/read", new ByteArrayContent(Definition())).GetAwaiter().GetResult();
                Check(binary.StatusCode == HttpStatusCode.Forbidden, "Endpoint bypassed JSON guard");
                using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/project-doctor/tiles/read") { Content = JsonContent.Create(new ProjectDoctorTileUpload(Convert.ToBase64String(Definition()))) };
                foreign.Headers.Add("Origin", "https://foreign.example");
                using var blocked = client.SendAsync(foreign).GetAwaiter().GetResult(); Check(blocked.StatusCode == HttpStatusCode.Forbidden, "Foreign origin accepted");
                using var invalid = client.PostAsJsonAsync("/api/project-doctor/tiles/read", new ProjectDoctorTileUpload("!!!!")).GetAwaiter().GetResult();
                Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Malformed upload not rejected");
                using var compare = client.PostAsJsonAsync("/api/project-doctor/tiles/compare", new ProjectDoctorTileComparison(snapshot, snapshot)).GetAwaiter().GetResult();
                Check(compare.IsSuccessStatusCode && compare.Content.ReadFromJsonAsync<TileCompatibilityReport>().GetAwaiter().GetResult()!.Differences.Count == 0, "Compare endpoint failed");
                using var check = client.PostAsJsonAsync("/api/project-doctor/check", new ProjectDoctorRequest { Project = Blank() }).GetAwaiter().GetResult();
                Check(check.IsSuccessStatusCode && !check.Content.ReadFromJsonAsync<ProjectDoctorReport>().GetAwaiter().GetResult()!.Complete, "Project endpoint hid coverage limitations");
                using var catalogs = client.GetAsync("/api/project-doctor/tiles/catalogs").GetAwaiter().GetResult();
                var entries = catalogs.Content.ReadFromJsonAsync<JsonElement>().GetAwaiter().GetResult().GetProperty("catalogs");
                Check(entries.GetArrayLength() == 1, "Installed catalog endpoint omitted fixture");
                var id = entries[0].GetProperty("id").GetString();
                using var installed = client.GetAsync("/api/project-doctor/tiles/snapshot?catalogId=" + id).GetAwaiter().GetResult();
                Check(installed.IsSuccessStatusCode && !installed.Content.ReadFromJsonAsync<TileDefinitionSnapshot>().GetAwaiter().GetResult()!.HasStructure, "Text catalog invented structure");
                using var path = client.GetAsync("/api/project-doctor/tiles/snapshot?catalogId=../../secret").GetAwaiter().GetResult();
                Check(path.StatusCode == HttpStatusCode.BadRequest, "Arbitrary file selection accepted");
            }
            finally { app.StopAsync().GetAwaiter().GetResult(); app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }));
    }

    static MapProject Blank() => MapGenerator.Generate(new() { Kind = "blank" });
    static TileDefinitionSnapshot Snapshot(int id, Dictionary<string, string> properties) =>
        new("fixture", null, true, [new("fixture", id, "fixture.png", 16, 1, 11,
            Enumerable.Range(0, 16).Select(slot => new TileRecordSnapshot(slot, slot < 11, slot == 10 ? properties : new Dictionary<string, string>())).ToArray())]);
    static void WithCatalog(Action<GameAssetCatalog, string> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "pz-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "game", "media", "texturepacks"));
        File.WriteAllText(Path.Combine(root, "game", "media", "newtiledefinitions.tiles.txt"), "// fixture_0\n tile\n {\n solidfloor =\n }\n");
        try { run(new GameAssetCatalog(root, Path.Combine(root, "game")), root); }
        finally { Directory.Delete(root, true); }
    }
    static byte[] Definition()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, true);
        void Line(string value) { writer.Write(Encoding.Latin1.GetBytes(value)); writer.Write((byte)10); }
        writer.Write("tdef"u8); writer.Write(1); writer.Write(1); Line("fixture"); Line("fixture.png");
        writer.Write(2); writer.Write(1); writer.Write(902); writer.Write(1); writer.Write(1); Line("solidfloor"); Line("");
        return stream.ToArray();
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
}
