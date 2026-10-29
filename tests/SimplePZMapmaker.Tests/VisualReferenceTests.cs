using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class VisualReferenceTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("visual references validate real PNG bytes, checksum, dimensions and budgets", () =>
        {
            var png = Png(); var info = VisualReferenceStore.ValidateImage(png, "image/png");
            Check(info.Width == 2 && info.Height == 2, "PNG dimensions changed.");
            Reject(() => VisualReferenceStore.ValidateImage(png, "image/jpeg"));
            var corrupt = png.ToArray(); corrupt[29] ^= 1;
            Reject(() => VisualReferenceStore.ValidateImage(corrupt, "image/png"));
            Reject(() => VisualReferenceStore.ValidateImage(png[..^8], "image/png"));
            Reject(() => VisualReferenceStore.ValidateImage(Png(8193, 1), "image/png"));
            Reject(() => VisualReferenceStore.ValidateImage(new byte[VisualReferenceStore.MaximumImageBytes + 1], "image/png"));
            Reject(() => VisualReferenceStore.ValidateImage("<svg/>"u8.ToArray(), "image/svg+xml"));
        });
        yield return ("visual references accept genuine JPEG and reject truncation and forged dimension headers", () =>
        {
            var jpeg = Convert.FromBase64String(Jpeg);
            var info = VisualReferenceStore.ValidateImage(jpeg, "image/jpeg");
            Check(info.Width == 2 && info.Height == 3, "JPEG dimensions lost.");
            Reject(() => VisualReferenceStore.ValidateImage(jpeg[..^2], "image/jpeg"));
            Reject(() => VisualReferenceStore.ValidateImage(jpeg.Concat(new byte[] { 1 }).ToArray(), "image/jpeg"));
            var huge = jpeg.ToArray(); var frame = Enumerable.Range(0, jpeg.Length - 1).First(i => jpeg[i] == 255 && jpeg[i + 1] == 192);
            huge[frame + 7] = 0x7f; huge[frame + 8] = 0xff;
            Reject(() => VisualReferenceStore.ValidateImage(huge, "image/jpeg"));
        });
        yield return ("visual references persist immutable own bytes and provenance across restart", () => WithStore((store, root) =>
        {
            var target = Target(); var bytes = Png();
            var r = store.UploadOwn(Token, target, bytes, "image/png", new("Photographer", "own-photo", null, 0, 0, 0));
            bytes[0] = 0;
            var restored = new VisualReferenceStore(root);
            Check(restored.ReadImage(Token, r.Id).Bytes.SequenceEqual(Png()), "Stored image changed.");
            Check(restored.Read(Token, r.Id).Author == "Photographer", "Author lost.");
            Check(restored.List(Token).Single().Sha256 == r.Sha256, "Digest lost.");
            Reject(() => restored.Read(new string('b', 64), r.Id));
            Reject(() => restored.Read(Token, "../outside"));
            var file = Directory.GetFiles(Path.Combine(root, "work", "visual-references"), "*.png", SearchOption.AllDirectories).Single();
            File.WriteAllBytes(file, Png(3, 2));
            Reject(() => restored.ReadImage(Token, r.Id));
        }));
        yield return ("visual references resolve source IDs and centred map coordinates without changing geodata", () =>
        {
            var p = Project(); p.Bounds = new() { West = 44, East = 44.001, South = 55, North = 55.001 };
            var before = JsonSerializer.Serialize(p);
            var binding = VisualReferenceStore.ResolveMap(p, 3, new(SourceId: "way/123"));
            var geographic = VisualReferenceStore.ResolveMap(p, 3, new(Longitude: binding.Longitude, Latitude: binding.Latitude));
            Check(geographic.BuildingId == p.Buildings[0].Id && binding.BaseRevision == 3, "Wrong map building.");
            Check(before == JsonSerializer.Serialize(p), "Binding mutated factual geodata.");
        });
        yield return ("visual references bind shared buildings across rotated world seams", () =>
        {
            var a = Project(); a.Buildings.Clear(); a.WorldX = 100; a.WorldY = 100;
            var b = NeighborhoodPlanner.Copy(a); b.WorldX = 101;
            var shared = new Building { X = 100 * 256 + 250, Y = 100 * 256 + 30, Width = 12, SourceId = "way/shared" };
            var w = new WorldProject { Maps = [new() { Project = a }, new() { Project = b }], Buildings = [shared],
                GeoReference = new() { Bounds = new() { West = 44, East = 44.01, South = 55, North = 55.01 }, RotationDegrees = 13 } };
            var binding = VisualReferenceStore.ResolveWorld(w, 4, new(Scope: "world", SourceId: "way/shared"));
            var point = VisualReferenceStore.ResolveWorld(w, 4, new(Scope: "world", Longitude: binding.Longitude, Latitude: binding.Latitude));
            Check(point.BuildingId == shared.Id && point.SharedBuilding, "Seam building lost.");
            Check(Math.Abs(point.WorldX!.Value - (shared.X + shared.Width / 2d)) < .00001, "Wrong global tile coordinate.");
        });
        yield return ("visual references require image receipt and keep observations separate and uncertain", () => WithStore((store, _) =>
        {
            var r = store.UploadOwn(Token, Target(), Png(), "image/png", new("Author", "own-photo"));
            var observations = new VisualObservations { WallMaterial = new("brick", .8, "visible", "Only the front facade is visible.") };
            Reject(() => store.Observe(Token, r.Id, new string('a', 32), observations));
            var view = store.View(Token, r.Id);
            var o = store.Observe(Token, r.Id, view.ReceiptId, observations);
            Check(o.Observations.Floors.Value is null && o.Observations.Floors.Confidence is null, "Unknown floors invented.");
            Check(o.Source.Sha256 == r.Sha256 && o.Source.CapturedAt is null, "Source provenance invented.");
            Reject(() => store.Observe(Token, r.Id, view.ReceiptId, new() { Roof = new("flat", 1.1, "visible") }));
            Reject(() => store.Observe(Token, r.Id, view.ReceiptId, new() { Roof = new("flat", .9, "hidden") }));
        }));
        yield return ("visual references reject unsupported providers and missing token is an explicit capability", () => WithStore((store, _) =>
        {
            Reject(() => VisualReferenceStore.RequireProvider("google"));
            Reject(() => VisualReferenceStore.RequireProvider("yandex"));
            var old = Environment.GetEnvironmentVariable("PZMAP_MAPILLARY_TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("PZMAP_MAPILLARY_TOKEN", null);
                Check(!store.Capabilities().Mapillary.Available, "Missing token advertised as available.");
                try { store.SearchMapillary(0, 0).GetAwaiter().GetResult(); throw new Exception("Missing capability was hidden."); }
                catch (VisualReferenceCapabilityException e) { Check(e.Message.Contains("PZMAP_MAPILLARY_TOKEN"), "Unreadable capability."); }
            }
            finally { Environment.SetEnvironmentVariable("PZMAP_MAPILLARY_TOKEN", old); }
        }));
        yield return ("visual references reject CDN SSRF and nonpublic addresses", () =>
        {
            foreach (var url in new[] { "http://scontent.xx.fbcdn.net/a.jpg", "https://fbcdn.net.evil.test/a", "https://127.0.0.1/a", "https://scontent.xx.fbcdn.net:444/a", "https://u:p@scontent.xx.fbcdn.net/a", "https://graph.mapillary.com/a?access_token=secret" })
                Reject(() => VisualReferenceStore.ValidateDownloadUri(url));
            VisualReferenceStore.ValidateDownloadUri("https://scontent.xx.fbcdn.net/mly/a.jpg?oe=abc");
            foreach (var ip in new[] { "127.0.0.1", "10.1.1.1", "169.254.1.2", "192.168.2.2", "::1", "fc00::1", "::ffff:127.0.0.1" })
                Check(!VisualReferenceStore.IsPublicAddress(IPAddress.Parse(ip)), "SSRF address accepted: " + ip);
        });
        yield return ("visual references MCP returns SDK image content with exact source bytes", () => WithStore((store, root) =>
        {
            var maps = new AgentProjectStore(Path.Combine(root, "maps")); var worlds = new AgentWorldStore(Path.Combine(root, "worlds"));
            var s = maps.Create(Project()); var tools = new McpReferenceTools(store, maps, worlds);
            var r = tools.Upload(s.Token, s.Revision, new(BuildingId: s.Project.Buildings[0].Id), Convert.ToBase64String(Png()), "image/png", new("Author", "own-photo"));
            var result = tools.View(s.Token, r.Id);
            var content = result.Content.OfType<ImageContentBlock>().Single();
            Check(content.DecodedData.Span.SequenceEqual(Png()) && content.MimeType == "image/png", "No real MCP image.");
        }));
        yield return ("visual references checked-design drafts never mutate source and stale apply is rejected", () => WithStore((store, root) =>
        {
            var maps = new AgentProjectStore(Path.Combine(root, "maps")); var worlds = new AgentWorldStore(Path.Combine(root, "worlds"));
            var tasks = new AgentDesignStore(Path.Combine(root, "designs")); var assets = new GameAssetCatalog(root, ""); var models = new StaticModelCatalog(assets);
            var checkedDesign = new McpDesignTools(maps, worlds, tasks, new NativeLightingService(root, assets, models), assets, models);
            var tools = new McpReferenceTools(store, maps, worlds, checkedDesign);
            var p = Project(); var building = p.Buildings[0]; building.Design = BuildingDesigner.Generate(building, 1, 42, "rect");
            building.Design.Floors[0].Objects.Clear();
            var session = maps.Create(p); var original = JsonSerializer.Serialize(session.Project);
            var reference = tools.Upload(session.Token, 1, new(BuildingId: building.Id), Convert.ToBase64String(Png()), "image/png", new("Author", "own-photo"));
            var view = store.View(session.Token, reference.Id); var observations = tools.Observe(session.Token, 1, reference.Id, view.ReceiptId,
                new() { WallMaterial = new("brick", .8, "visible") });
            var draft = WorldAssembler.CloneBuilding(building); draft.Design!.Material = "brick";
            var constraints = new DesignConstraints { X = 96, Y = 96, Width = 32, Height = 32, RequireLighting = false };
            tools.Propose(session.Token, 1, observations.Id, draft, constraints);
            Check(maps.Read(session.Token).Revision == 1 && JsonSerializer.Serialize(maps.Read(session.Token).Project) == original, "Proposal overwrote source.");
            var task = tasks.Read(session.Token, tasks.List(session.Token).Single());
            Check(task.Candidate.Buildings.Single().Design!.Material == "brick" && task.Objective.Contains(observations.Id), "Evidence/draft not staged.");
            Check(!task.Passed && task.TaskRevision == 1, "Unchecked draft advertised as passing.");
            Reject(() => checkedDesign.Commit(session.Token, task.Id, 1));
            var invented = WorldAssembler.CloneBuilding(building); invented.Design!.Floors.Add(NeighborhoodPlanner.Copy(invented.Design.Floors[0]));
            Reject(() => tools.Propose(session.Token, 1, observations.Id, invented, constraints));
            var partition = building.Design.Floors[0].Edges.First(e => BuildingDesigner.Cell(building, building.Design.Floors[0], e.X, e.Y) != '.' &&
                BuildingDesigner.Cell(building, building.Design.Floors[0], e.X - (e.Edge == "w" ? 1 : 0), e.Y - (e.Edge == "n" ? 1 : 0)) != '.');
            var removedPartition = WorldAssembler.CloneBuilding(draft);
            removedPartition.Design!.Floors[0].Edges.RemoveAll(e => e.X == partition.X && e.Y == partition.Y && e.Edge == partition.Edge);
            Reject(() => tools.Propose(session.Token, 1, observations.Id, removedPartition, constraints));
            var changedPartition = WorldAssembler.CloneBuilding(draft);
            var changedEdge = changedPartition.Design!.Floors[0].Edges.Single(e => e.X == partition.X && e.Y == partition.Y && e.Edge == partition.Edge);
            changedEdge.Kind = partition.Kind == "open" ? "wall" : "open";
            Reject(() => tools.Propose(session.Token, 1, observations.Id, changedPartition, constraints));
            Reject(() => tools.Propose(session.Token, 1, observations.Id, draft, constraints, "observed"));
            tools.Propose(session.Token, 1, observations.Id, invented, constraints, "inferred");
            Check(tasks.List(session.Token).Select(id => tasks.Read(session.Token, id)).Any(t => t.Objective.Contains("explicitly inferred")), "Inferred layout not labelled.");
            var edited = WorldAssembler.CloneBuilding(draft); edited.SourceLevels = "fabricated";
            Reject(() => tools.Propose(session.Token, 1, observations.Id, edited, constraints));
            try { checkedDesign.Update(session.Token, task.Id, 0, [draft]); throw new Exception("Stale task revision accepted."); } catch (AgentRevisionConflict) { }
            checkedDesign.Check(session.Token, task.Id, 1).GetAwaiter().GetResult();
            task = tasks.Read(session.Token, task.Id); Check(task.Passed, "Access-only fixture did not pass checks.");
            maps.Mutate(session.Token, 1, map => map.Name = "Browser changed", "browser");
            try { checkedDesign.Commit(session.Token, task.Id, task.TaskRevision); throw new Exception("Stale source apply accepted."); } catch (AgentRevisionConflict) { }
            try { tools.Propose(session.Token, 1, observations.Id, draft, constraints); throw new Exception("Stale proposal accepted."); } catch (AgentRevisionConflict) { }
            Check(maps.Read(session.Token).Project.Buildings.Single().Design!.Material == building.Design.Material, "Stale apply changed building.");
        }));
        yield return ("visual references Graph search and selected-source import keep token and signed URLs server-only", () => WithStore((_, root) =>
        {
            const string secret = "MLY|fixture-only-not-a-real-token"; var calls = new List<string>();
            using var http = new HttpClient(new Handler(request =>
            {
                calls.Add(request.RequestUri!.AbsoluteUri);
                Check(!request.RequestUri.AbsoluteUri.Contains(secret), "Token placed in request URL.");
                if (request.RequestUri.Host == "graph.mapillary.com")
                {
                    Check(request.Headers.Authorization?.ToString() == "OAuth " + secret, "No Graph authorization header.");
                    var item = new { id = "123", creator = new { username = "Real contributor" }, geometry = new { type = "Point", coordinates = new[] { 0d, 0d } },
                        captured_at = 1600000000000L, compass_angle = 0, thumb_1024_url = "https://scontent.xx.fbcdn.net/mly/photo.jpg?oe=signed-private" };
                    return JsonResponse(request.RequestUri.AbsolutePath == "/images" ? new { data = new[] { item }, paging = new { next = "https://graph.mapillary.com/images?access_token=" + secret } } : (object)item);
                }
                Check(request.RequestUri.Host == "scontent.xx.fbcdn.net" && request.Headers.Authorization is null, "Credentials forwarded to image CDN.");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()) { Headers = { ContentType = new("image/png") } } };
            }));
            var store = ProviderStore(root, http, secret);
            var search = store.SearchMapillary(0, 0).GetAwaiter().GetResult();
            Check(search.Items.Single().SourceId == "123" && !search.Complete, "Provider coverage limitation lost.");
            var imported = store.ImportMapillary(Token, Target(), "123").GetAwaiter().GetResult();
            Check(imported.Author == "Real contributor" && imported.Heading == 0 && imported.CapturedAt?.Year == 2020 && imported.License == "CC BY-SA 4.0", "Source metadata lost.");
            Check(calls.Count == 3 && calls[1].Contains("/123?"), "Import did not requery actual selected source ID.");
            var returned = JsonSerializer.Serialize(new { search, imported });
            Check(!returned.Contains(secret) && !returned.Contains("signed-private") && !returned.Contains("thumb_1024_url"), "Private provider URL/token leaked.");
            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                Check(!File.ReadAllText(file).Contains(secret) && !File.ReadAllText(file).Contains("signed-private"), "Credentials persisted.");
        }));
        yield return ("visual references provider redirects, token-echo errors and hostile URLs fail closed", () => WithStore((_, root) =>
        {
            const string secret = "MLY|test-secret";
            foreach (var status in new[] { HttpStatusCode.Redirect, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests })
            {
                var count = 0; using var http = new HttpClient(new Handler(_ =>
                {
                    count++; return new(status) { Content = new StringContent(secret), Headers = { Location = new("https://evil.test/?token=" + secret) } };
                }));
                try { ProviderStore(root, http, secret).SearchMapillary(0, 0).GetAwaiter().GetResult(); throw new Exception("Provider failure hidden."); }
                catch (VisualReferenceCapabilityException e) { Check(!e.ToString().Contains(secret) && count == 1, "Redirect followed or token echoed."); }
            }
            using var hostile = new HttpClient(new Handler(_ => JsonResponse(new { id = "123", creator = new { username = "Contributor" },
                geometry = new { type = "Point", coordinates = new[] { 0d, 0d } }, thumb_1024_url = "https://127.0.0.1/private" })));
            Reject(() => ProviderStore(root, hostile, secret).ImportMapillary(Token, Target(), "123").GetAwaiter().GetResult());
            using var echo = new HttpClient(new Handler(_ => throw new HttpRequestException("https://graph.mapillary.com/?access_token=" + secret)));
            try { ProviderStore(root, echo, secret).SearchMapillary(0, 0).GetAwaiter().GetResult(); throw new Exception("Expected request failure."); }
            catch (VisualReferenceCapabilityException e) { Check(!e.ToString().Contains(secret), "Token leaked through inner exception."); }
            using var oversized = new ByteArrayContent(new byte[20]);
            Reject(() => VisualReferenceStore.Limited(oversized, 10, default).GetAwaiter().GetResult());
        }));
        yield return ("visual references real HTTP mount uploads own photo, serves original, returns revisions and capability", () => WithStore((store, root) =>
        {
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            var maps = new AgentProjectStore(Path.Combine(root, "maps")); var session = maps.Create(Project());
            builder.Services.AddSingleton(store); builder.Services.AddSingleton(maps); builder.Services.AddSingleton(new AgentWorldStore(Path.Combine(root, "worlds")));
            builder.Services.AddSingleton<McpReferenceTools>();
            var app = builder.Build(); app.MapVisualReferenceEndpoints(); app.StartAsync().GetAwaiter().GetResult();
            try
            {
                using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
                using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/visual-references/" + session.Token + "/own");
                upload.Content = JsonContent.Create(new VisualReferenceEndpoints.OwnUploadRequest(1, new(BuildingId: session.Project.Buildings[0].Id), new("HTTP photographer", "own-photo"), Convert.ToBase64String(Png()), "image/png"));
                using var response = client.SendAsync(upload).GetAwaiter().GetResult();
                Check(response.StatusCode == HttpStatusCode.OK, "JSON upload failed: " + response.Content.ReadAsStringAsync().Result);
                var reference = response.Content.ReadFromJsonAsync<VisualReference>().Result!;
                using var image = client.GetAsync(McpReferenceTools.ImageUrl(session.Token, reference.Id)).Result;
                Check(image.Content.ReadAsByteArrayAsync().Result.SequenceEqual(Png()) && image.Headers.CacheControl?.NoStore == true, "HTTP image changed/cached.");
                using var list = client.GetAsync("/api/visual-references/" + session.Token).Result; Check(list.IsSuccessStatusCode, "List failed.");
                using var stale = client.PostAsJsonAsync("/api/visual-references/" + session.Token + "/target", new VisualReferenceEndpoints.TargetRequest(2, new(BuildingId: session.Project.Buildings[0].Id))).Result;
                Check(stale.StatusCode == HttpStatusCode.Conflict, "HTTP stale revision not 409.");
                using var unsupported = client.PostAsJsonAsync("/api/visual-references/" + session.Token + "/mapillary/import", new VisualReferenceEndpoints.ImportRequest(1, new(BuildingId: session.Project.Buildings[0].Id), "123", "google")).Result;
                Check(unsupported.StatusCode == HttpStatusCode.BadRequest, "Unsupported provider accepted.");
                Check(maps.Read(session.Token).Revision == 1, "Reference HTTP endpoint mutated project.");
            }
            finally { app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }));
    }

    static VisualReferenceStore ProviderStore(string root, HttpClient http, string token) => (VisualReferenceStore)typeof(VisualReferenceStore)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(string), typeof(HttpClient), typeof(Func<string?>)], null)!
        .Invoke([root, http, (Func<string?>)(() => token)]);
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request)); }
    static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    const string Jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAADAAIDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDOooorxT7M/9k=";

    const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static MapProject Project() { var p = MapGenerator.Generate(new() { Kind = "blank" }); p.Buildings.Add(new() { X = 100, Y = 100, SourceId = "way/123" }); return p; }
    static VisualReferenceBinding Target() => VisualReferenceStore.ResolveMap(Project(), 1, new(SourceId: "way/123"));
    static void WithStore(Action<VisualReferenceStore, string> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "pz-visual-tests-" + Guid.NewGuid().ToString("N"));
        try { run(new(root), root); } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected validation rejection."); }

    internal static byte[] Png(int width = 2, int height = 2)
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); stream.Write(number);
            var body = Encoding.ASCII.GetBytes(name).Concat(data).ToArray(); stream.Write(body); uint crc = uint.MaxValue;
            foreach (var b in body) { crc ^= b; for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; }
            BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ uint.MaxValue); stream.Write(number);
        }
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 2; Chunk("IHDR", header);
        using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true)) z.Write(new byte[height * (1 + width * 3)]);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []); return stream.ToArray();
    }
}
