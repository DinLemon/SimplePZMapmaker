using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class AgentTilesetTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("agent tileset pixels properties ZIP and catalog round trip", () => WithFixture(f =>
        {
            var spec = Spec(); var initial = JsonSerializer.Serialize(f.Maps.Read(f.Token).Project);
            var d = f.Store.Create(f.Token, 1, spec);
            Check(d.Frames[0].Properties.ContainsKey("WallN") && d.Frames[1].Properties.ContainsKey("collideW"), "Vanilla properties were not copied");
            // CopyFrom is a snapshot: later source-catalog edits must not change the draft's physical behavior.
            File.WriteAllText(f.VanillaDefinitions, "// vanilla_wall_0\ntile\n{\nsolid=\n}\n");
            f.Assets.Refresh(f.Game, false);
            d = Upload(f, d, 0, 210, 40); d = Upload(f, d, 1, 20, 190);
            var preview = Png.Decode(f.Store.Preview(f.Token, d.Id, 0));
            Check(preview.Rgba[(4 * 128 + 5) * 4] == 210 && preview.Rgba[3] == 0, "Preview invented or changed uploaded pixels");
            var toolPreview = new McpTilesetTools(f.Store).Preview(f.Token, d.Id, 0);
            var image = toolPreview.Content.OfType<ImageContentBlock>().Single();
            Check(image?.MimeType == "image/png" && image.DecodedData.Span.SequenceEqual(f.Store.Preview(f.Token, d.Id, 0)), "MCP preview is not actual image content");
            d = f.Store.Build(f.Token, d.Id, d.Revision);
            var nativePreview = f.Store.PreviewInfo(f.Token, d.Id, 0);
            Check(nativePreview.Origin == "validated-native-pack" && nativePreview.Properties.ContainsKey("WallN") && !f.Assets.HasActiveMods, "Build preview bypassed the native loader or activated a mod before attach");
            Check(d.State == "built" && d.Sha256?.Length == 64 && f.Store.List(f.Token).Single().Id == d.Id, "Missing build result");
            Check(initial == JsonSerializer.Serialize(f.Maps.Read(f.Token).Project) && f.Maps.Read(f.Token).Revision == 1, "Draft/build mutated source");
            using var zip = new ZipArchive(new MemoryStream(f.Store.Download(f.Token, d.Id)));
            Check(zip.GetEntry("agent_test/42/mod.info") is not null && zip.GetEntry("agent_test/42/agent-tileset.json") is not null && zip.Entries.All(e => !e.FullName.Contains("media/maps/")), "Not a separate B42 tile mod");
            using var pack = new MemoryStream(); using (var input = zip.GetEntry("agent_test/42/media/texturepacks/agent_sign_01.pack")!.Open()) input.CopyTo(pack);
            pack.Position = 0; var page = TexturePackIndex.Read(pack, "agent_sign_01.pack").Single();
            pack.Position = page.Offset; var atlasPng = new byte[page.Length]; pack.ReadExactly(atlasPng); var atlas = Png.Decode(atlasPng);
            foreach (var sprite in page.Sprites)
            {
                var source = Png.Decode(f.Store.Preview(f.Token, d.Id, int.Parse(sprite.Name.Split('_')[^1])));
                for (var y = 0; y < sprite.Height; y++) for (var x = 0; x < sprite.Width; x++)
                    Check(source.Rgba.AsSpan(((sprite.OffsetY + y) * source.Width + sprite.OffsetX + x) * 4, 4).SequenceEqual(atlas.Rgba.AsSpan(((sprite.Y + y) * atlas.Width + sprite.X + x) * 4, 4)), "Pack atlas pixel mismatch");
            }
            using var tiles = new MemoryStream(); using (var input = zip.GetEntry("agent_test/42/media/agent_sign_01.tiles")!.Open()) input.CopyTo(tiles);
            tiles.Position = 0; var defs = TileDefinitionReader.Read(tiles);
            Check(defs[0].Properties.ContainsKey("WallN") && !defs[0].Properties.ContainsKey("solid") && defs[1].Properties.ContainsKey("collideW"), "Native tile properties drifted");
            var attached = f.Store.Attach(f.Token, d.Id, 1, d.Revision); Check(attached.Revision == 2, "Attach revision did not advance");
            var p = f.Maps.Read(f.Token).Project;
            Check(p.RequiredMods.Contains("agent_test") && p.AssetSources["agent_sign_01_0"] == "agent_test", "Map dependencies missing");
            var tile = f.Assets.Get("agent_sign_01_0"); Check(tile.ModId == "agent_test" && tile.Sprite is not null && tile.Page is not null && tile.Properties.ContainsKey("WallN"), "Actual sprite not registered");
            Check(f.Assets.Page(tile.Page!).AsSpan().SequenceEqual(atlasPng), "Catalog did not expose the built pack image");
            Check(f.Assets.ResolveRequirements([tile.Id]).Single().ModId == "agent_test", "Catalog dependency does not resolve");
            // Independent catalog restart uses only persisted validated attachments, without changing game installation.
            var fresh = new GameAssetCatalog(Path.Combine(f.Root, "fresh"), f.Game);
            var restored = new AgentTilesetStore(f.Drafts, f.Maps, f.Worlds, fresh);
            Check(restored.RestoreDiagnostics.Count == 0 && fresh.Get(tile.Id).ModId == "agent_test", "Attached mod did not survive service restart");
        }));

        yield return ("agent tileset unused attachment survives validation and project reload without trusting wrong owners", () => WithFixture(f =>
        {
            OrdinaryMod(f);
            var blank = f.Maps.Read(f.Token).Project;
            // Only synthetic floor definitions needed by this exact blank map; no installed-game dependency.
            File.AppendAllText(f.VanillaDefinitions, string.Join("\n", NativeScene.ResourceIds(blank).Select(id => "// " + id + "\ntile\n{\nsolidfloor=\n}\n")));
            f.Assets.Refresh(f.Game, false);
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d); d = f.Store.Build(f.Token, d.Id, d.Revision);
            f.Store.Attach(f.Token, d.Id, 1, d.Revision);
            var project = f.Maps.Read(f.Token).Project;
            Check(!NativeScene.ResourceIds(project).Any(id => id.StartsWith("agent_sign_01_", StringComparison.Ordinal)), "Fixture placed a generated tile before testing its unused attachment");
            project.RequiredMods.Add("ordinary_fixture"); project.AssetSources["ordinary_fixture_tiles_0"] = "ordinary_fixture";
            AssetRules.Project(project, f.Assets);
            Check(project.RequiredMods.SequenceEqual(new[] { "agent_test" }) && !project.AssetSources.ContainsKey("ordinary_fixture_tiles_0"), "Validation removed the pending attachment or retained an ordinary unused mod");
            Check(project.AssetSources.GetValueOrDefault("agent_sign_01_0") == "agent_test" && project.AssetSources.GetValueOrDefault("agent_sign_01_1") == "agent_test", "Validation dropped an unplaced declared frame");

            var reloaded = JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(project, NativeMapExporter.JsonOptions), NativeMapExporter.JsonOptions)!;
            AssetRules.Project(reloaded, f.Assets);
            Check(reloaded.RequiredMods.SequenceEqual(new[] { "agent_test" }) && reloaded.AssetSources.GetValueOrDefault("agent_sign_01_0") == "agent_test" && reloaded.AssetSources.GetValueOrDefault("agent_sign_01_1") == "agent_test", "Project reload discarded pending frame ownership or dependency");

            var wrongOwner = NeighborhoodPlanner.Copy(reloaded);
            wrongOwner.AssetSources["vanilla_wall_0"] = "agent_test";
            Reject(() => AssetRules.Project(wrongOwner, f.Assets));
            var missingTile = NeighborhoodPlanner.Copy(reloaded);
            missingTile.AssetSources["agent_sign_01_999"] = "agent_test";
            Reject(() => AssetRules.Project(missingTile, f.Assets));
        }));

        yield return ("agent tileset saved selection restores ordinary and workspace mods in startup order", () => WithFixture(f =>
        {
            var ordinary = OrdinaryMod(f);
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d); d = f.Store.Build(f.Token, d.Id, d.Revision);
            f.Store.Attach(f.Token, d.Id, 1, d.Revision);
            f.Assets.ActivateMods(["ordinary_fixture", "agent_test"]);

            var fresh = new GameAssetCatalog(f.Root, f.Game, new GameInstallationDiscovery([], false));
            Check(fresh.Get("ordinary_fixture_tiles_0").ModId == "ordinary_fixture", "Workspace ID prevented ordinary mod restoration before the tileset store existed");
            var restored = new AgentTilesetStore(f.Drafts, f.Maps, f.Worlds, fresh);
            Check(restored.RestoreDiagnostics.Count == 0 && fresh.Get("agent_sign_01_0").ModId == "agent_test", "Workspace mod was not restored after native validation");
            fresh.Refresh(f.Game, false); fresh.DiscoverMods([ordinary], "42.0.0");
            fresh.ActivateMods(["ordinary_fixture", "agent_test"]);
            Check(fresh.Get("ordinary_fixture_tiles_0").ModId == "ordinary_fixture" && fresh.Get("agent_sign_01_0").ModId == "agent_test", "Refresh/discovery/activation lost an active resource source");
        }));

        yield return ("agent tileset disabled selection survives restart and explicit reattach enables it", () => WithFixture(f =>
        {
            var ordinary = OrdinaryMod(f);
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d); d = f.Store.Build(f.Token, d.Id, d.Revision);
            f.Store.Attach(f.Token, d.Id, 1, d.Revision);
            f.Assets.ActivateMods(["ordinary_fixture"]);

            var missingGame = new GameAssetCatalog(f.Root, "", new GameInstallationDiscovery([], false));
            var missingGameStore = new AgentTilesetStore(f.Drafts, f.Maps, f.Worlds, missingGame);
            Check(missingGameStore.RestoreDiagnostics.Count == 0, "Disabled package preferences require an installed game");
            Reject(() => missingGame.Get("agent_sign_01_0"));
            var fresh = new GameAssetCatalog(f.Root, f.Game, new GameInstallationDiscovery([], false));
            var restored = new AgentTilesetStore(f.Drafts, f.Maps, f.Worlds, fresh);
            Check(restored.RestoreDiagnostics.Count == 0, "Disabled workspace package was not discoverable");
            Reject(() => fresh.Get("agent_sign_01_0"));
            fresh.Refresh(f.Game, false); fresh.DiscoverMods([ordinary], "42.0.0");
            Reject(() => fresh.Get("agent_sign_01_0"));
            fresh.ActivateMods(["ordinary_fixture", "agent_test"]);
            Check(fresh.Get("agent_sign_01_0").ModId == "agent_test", "A disabled workspace mod could not be explicitly activated");
            fresh.ActivateMods(["ordinary_fixture"]);
            var block = Path.Combine(f.MapDirectory, f.Token + ".agent.json.tmp"); Directory.CreateDirectory(block);
            var failed = false;
            try { restored.Attach(f.Token, d.Id, 2, d.Revision); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed && f.Maps.Read(f.Token).Revision == 2, "Failed reattach changed the project");
            Reject(() => fresh.Get("agent_sign_01_0"));
            var afterFailure = new GameAssetCatalog(f.Root, f.Game, new GameInstallationDiscovery([], false));
            var failedRestore = new AgentTilesetStore(f.Drafts, f.Maps, f.Worlds, afterFailure);
            Check(failedRestore.RestoreDiagnostics.Count == 0 && afterFailure.Get("ordinary_fixture_tiles_0").ModId == "ordinary_fixture", "Failed reattach damaged catalog persistence");
            Reject(() => afterFailure.Get("agent_sign_01_0"));
            Directory.Delete(block);
            restored.Attach(f.Token, d.Id, 2, d.Revision);
            var reenabled = new GameAssetCatalog(f.Root, f.Game, new GameInstallationDiscovery([], false));
            var reenabledStore = new AgentTilesetStore(f.Drafts, f.Maps, f.Worlds, reenabled);
            Check(reenabledStore.RestoreDiagnostics.Count == 0 && reenabled.Get("agent_sign_01_0").ModId == "agent_test", "Explicit reattach was not persisted");
        }));

        yield return ("agent tileset legacy saved IDs do not prevent ordinary mod recovery", () => WithFixture(f =>
        {
            var ordinary = OrdinaryMod(f);
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d); d = f.Store.Build(f.Token, d.Id, d.Revision);
            f.Store.Attach(f.Token, d.Id, 1, d.Revision);
            File.WriteAllText(Path.Combine(f.Root, "work", "assets", "mods.json"), JsonSerializer.Serialize(new { roots = new[] { ordinary }, gameVersion = "42.0.0", ids = new[] { "ordinary_fixture", "agent_test" } }));
            var fresh = new GameAssetCatalog(f.Root, f.Game, new GameInstallationDiscovery([], false));
            Check(fresh.Get("ordinary_fixture_tiles_0").ModId == "ordinary_fixture", "Legacy workspace ID blocked normal mod restoration");
            var restored = new AgentTilesetStore(f.Drafts, f.Maps, f.Worlds, fresh);
            Check(restored.RestoreDiagnostics.Count == 0 && fresh.Get("agent_sign_01_0").ModId == "agent_test", "Legacy attachment did not recover");
        }));

        yield return ("agent tileset rejects missing invalid oversized and empty PNGs", () => WithFixture(f =>
        {
            var d = f.Store.Create(f.Token, 1, Spec());
            Reject(() => f.Store.Preview(f.Token, d.Id, 0)); Reject(() => f.Store.Build(f.Token, d.Id, d.Revision));
            Reject(() => f.Store.Upload(f.Token, d.Id, d.Revision, 0, "not base64"));
            Reject(() => f.Store.Upload(f.Token, d.Id, d.Revision, 0, new string('A', AgentTilesetStore.MaximumBase64Characters + 1)));
            Reject(() => f.Store.Upload(f.Token, d.Id, d.Revision, 0, Convert.ToBase64String(Png.Encode(new(128, 256, new byte[128 * 256 * 4])))));
            Reject(() => f.Store.Upload(f.Token, d.Id, d.Revision, 0, Convert.ToBase64String(Pixels(8, 8, 1, 2))));
            var bad = Pixels(128, 256, 1, 2); bad[29] ^= 1;
            Reject(() => f.Store.Upload(f.Token, d.Id, d.Revision, 0, Convert.ToBase64String(bad)));
            Reject(() => f.Store.Read("../x", d.Id)); Reject(() => f.Store.Read(f.Token, "../../outside"));
            Check(f.Store.Read(f.Token, d.Id).Revision == 1 && !f.Store.Read(f.Token, d.Id).Frames.Any(i => i.Uploaded), "Bad uploads changed draft");
        }));

        yield return ("agent tileset validates canonical IDs numbers provenance and orientations", () => WithFixture(f =>
        {
            var s = Spec();
            Reject(() => f.Store.Create(f.Token, 1, s with { ModId = "Bad-ID" }));
            Reject(() => f.Store.Create(f.Token, 1, s with { Tileset = "../bad" }));
            Reject(() => f.Store.Create(f.Token, 1, s with { FileNumber = 42 }));
            Reject(() => f.Store.Create(f.Token, 1, s with { Tileset = "vanilla_wall" }));
            Reject(() => f.Store.Create(f.Token, 1, s with { Provenance = s.Provenance with { Kind = "source-authored", OriginalAuthor = "Artist", CanRedistribute = false } }));
            Reject(() => f.Store.Create(f.Token, 1, s with { Provenance = s.Provenance with { License = "" } }));
            Reject(() => f.Store.Create(f.Token, 1, s with { Frames = [new("north", "n", CopyFrom: "missing_0")] }));
            Reject(() => f.Store.Create(f.Token, 1, s with { Frames = [new("north", "n", CopyFrom: "vanilla_wall_1")] }));
            Reject(() => f.Store.Create(f.Token, 1, s with { Frames = [new("north", Properties: new() { ["CustomName"] = "broken\rname" })] }));
            var d = f.Store.Create(f.Token, 1, s); Check(d.Frames.Select(i => i.TileId).SequenceEqual(new[] { "agent_sign_01_0", "agent_sign_01_1" }), "Tile IDs were guessed or renumbered");
            Reject(() => f.Store.Create(f.Token, 1, s with { ModId = "other_mod", Tileset = "other_tiles" }));
        }));

        yield return ("agent tileset inactive mod FileNumber and filename collisions", () => WithFixture(f =>
        {
            var root = Path.Combine(f.Root, "existing", "existing_mod"); Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "mod.info"), "id=existing_mod\nname=Existing\ntiledef=existing_tiles 777\npack=existing_pack\n");
            f.Assets.DiscoverMods([Path.GetDirectoryName(root)!], "42.0.0");
            Reject(() => f.Store.Create(f.Token, 1, Spec() with { FileNumber = 777 }));
            Reject(() => f.Store.Create(f.Token, 1, Spec() with { Tileset = "existing_tiles" }));
            Reject(() => f.Store.Create(f.Token, 1, Spec() with { Tileset = "existing_pack" }));
            Reject(() => f.Store.Create(f.Token, 1, Spec() with { ModId = "existing_mod" }));
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d);
            File.AppendAllText(Path.Combine(root, "mod.info"), "tiledef=late_collision 700\n"); f.Assets.DiscoverMods([Path.GetDirectoryName(root)!], "42.0.0");
            Reject(() => f.Store.Build(f.Token, d.Id, d.Revision));
            Check(f.Store.Read(f.Token, d.Id).State == "draft", "Collision produced a published build");
        }));

        yield return ("agent tileset revision guard and provider conflict preserve resources", () => WithFixture(f =>
        {
            var d = f.Store.Create(f.Token, 1, Spec()); var oldRevision = d.Revision;
            d = Upload(f, d, 0, 10, 20); Conflict(() => f.Store.Upload(f.Token, d.Id, oldRevision, 1, Convert.ToBase64String(Pixels(128, 256, 2, 3))));
            d = Upload(f, d, 1, 20, 10); d = f.Store.Build(f.Token, d.Id, d.Revision);
            f.Maps.Mutate(f.Token, 1, p => p.Name = "Browser edited", "browser");
            Conflict(() => f.Store.Attach(f.Token, d.Id, 1, d.Revision));
            Check(!f.Assets.HasActiveMods && f.Maps.Read(f.Token).Project.RequiredMods.Count == 0, "Stale attach registered resources");
            f.Maps.Mutate(f.Token, 2, p => { p.Version = Math.Max(6, p.Version); p.AssetSources["agent_sign_01_0"] = "another_mod"; }, "browser");
            Reject(() => f.Store.Attach(f.Token, d.Id, 3, d.Revision));
            Check(f.Maps.Read(f.Token).Revision == 3 && !f.Assets.HasActiveMods, "Provider conflict partially attached");
        }));

        yield return ("agent tileset failed session save rolls back catalog and attachment marker", () => WithFixture(f =>
        {
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d); d = f.Store.Build(f.Token, d.Id, d.Revision);
            var before = f.Assets.Count; var block = Path.Combine(f.MapDirectory, f.Token + ".agent.json.tmp"); Directory.CreateDirectory(block);
            var failed = false;
            try { f.Store.Attach(f.Token, d.Id, 1, d.Revision); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed && f.Maps.Read(f.Token).Revision == 1 && f.Maps.Read(f.Token).Project.RequiredMods.Count == 0, "Failed save changed project");
            Check(f.Assets.Count == before && !f.Assets.HasActiveMods && !File.Exists(Path.Combine(f.Drafts, f.Token, d.Id, "attached")), "Failed save left registered resources");
            Directory.Delete(block);
            Check(f.Store.Attach(f.Token, d.Id, 1, d.Revision).Revision == 2, "Valid retry failed after rollback");
        }));

        yield return ("agent tileset built frames and staged ZIP reject tampering", () => WithFixture(f =>
        {
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d); d = f.Store.Build(f.Token, d.Id, d.Revision);
            Reject(() => f.Store.Upload(f.Token, d.Id, d.Revision, 0, Convert.ToBase64String(Pixels(128, 256, 1, 2))));
            var stagedPack = Path.Combine(f.Drafts, f.Token, d.Id, "staged", "agent_test", "42", "media", "texturepacks", "agent_sign_01.pack");
            var bytes = File.ReadAllBytes(stagedPack); bytes[^1] ^= 1; File.WriteAllBytes(stagedPack, bytes);
            Reject(() => f.Store.Attach(f.Token, d.Id, 1, d.Revision));
            Check(f.Maps.Read(f.Token).Revision == 1 && !f.Assets.HasActiveMods, "Tampered files became active");
        }));

        yield return ("agent tileset concurrent attachment commits exactly one revision", () => WithFixture(f =>
        {
            var d = f.Store.Create(f.Token, 1, Spec()); d = UploadAll(f, d); d = f.Store.Build(f.Token, d.Id, d.Revision);
            var committed = 0; var rejected = 0;
            Parallel.For(0, 2, _ =>
            {
                try { f.Store.Attach(f.Token, d.Id, 1, d.Revision); Interlocked.Increment(ref committed); }
                catch (AgentRevisionConflict) { Interlocked.Increment(ref rejected); }
            });
            var p = f.Maps.Read(f.Token);
            Check(committed == 1 && rejected == 1 && p.Revision == 2 && p.Project.RequiredMods.Count(m => m == "agent_test") == 1, "Concurrent attach was not atomic");
            Check(f.Assets.ActiveSources().Count(s => s.RequiredModId == "agent_test" && s.Root.EndsWith("42")) == 1, "Concurrent attach duplicated resources");
        }));

        yield return ("agent tileset world attachment preserves unrelated members and shared metadata", () => WithFixture(f =>
        {
            var a = MapGenerator.Generate(new() { Kind = "blank", Size = 256 });
            var b = MapGenerator.Generate(new() { Kind = "blank", Size = 256 }); b.WorldX++;
            var world = new WorldProject { Maps = [new() { Project = a }, new() { Project = b }] };
            // Shared object exists before attaching and must retain its own provider/dependency metadata.
            world.Decorations.Add(new() { X = a.WorldX * 256 + 10, Y = a.WorldY * 256 + 10, Asset = "agent_sign_01_0", Parts = [new(0, 0, "agent_sign_01_0")] });
            var source = f.Worlds.Create(world); var member = source.World.Maps[0].Id;
            var d = f.Store.Create(source.Token, 1, Spec(), "world", member);
            for (var i = 0; i < 2; i++) d = f.Store.Upload(source.Token, d.Id, d.Revision, i, Convert.ToBase64String(Pixels(128, 256, (byte)(10 + i), 20)));
            d = f.Store.Build(source.Token, d.Id, d.Revision);
            Check(f.Worlds.Read(source.Token).Revision == 1, "Build mutated world");
            f.Store.Attach(source.Token, d.Id, 1, d.Revision); var result = f.Worlds.Read(source.Token);
            Check(result.Revision == 2 && result.World.Maps[0].Project.RequiredMods.Contains("agent_test") && result.World.Maps[1].Project.RequiredMods.Count == 0, "Wrong world member dependencies");
            Check(result.World.Decorations.Single().RequiredMods.Contains("agent_test") && result.World.Decorations.Single().AssetSources["agent_sign_01_0"] == "agent_test", "Shared metadata missing");
            var projected = WorldAssembler.ProjectSection(result.World, result.World.Maps[0]); Check(projected.AssetSources["agent_sign_01_0"] == "agent_test", "Shared provider lost in projection");
        }));

        yield return ("agent tileset HTTP endpoints upload preview ZIP and stale conflict", () => WithFixture(f => HttpFlow(f).GetAwaiter().GetResult()));
    }

    static async Task HttpFlow(Fixture f)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Services.AddSingleton(f.Store);
        builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<McpTilesetTools>();
        await using var app = builder.Build(); app.MapAgentTilesetEndpoints(); app.MapMcp("/mcp"); await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
            var caps = await client.GetFromJsonAsync<JsonElement>("/api/agent/tilesets/capabilities"); Check(!caps.GetProperty("creation").GetProperty("embeddedImageGeneration").GetBoolean(), "HTTP falsely advertises a generator");
            var create = await client.PostAsJsonAsync("/api/agent/tilesets", new AgentTilesetEndpoints.CreateRequest(f.Token, 1, Spec())); create.EnsureSuccessStatusCode();
            var d = (await create.Content.ReadFromJsonAsync<AgentTilesetDraft>())!;
            for (var i = 0; i < 2; i++)
            {
                var result = await client.PostAsJsonAsync($"/api/agent/tilesets/{f.Token}/{d.Id}/frames/{i}", new AgentTilesetEndpoints.UploadRequest(d.Revision, Convert.ToBase64String(Pixels(128, 256, (byte)(120 + i), 70))));
                result.EnsureSuccessStatusCode(); d = (await result.Content.ReadFromJsonAsync<AgentTilesetDraft>())!;
            }
            var png = await client.GetAsync(d.Frames[0].PreviewUrl); Check(png.Content.Headers.ContentType?.MediaType == "image/png" && Png.Decode(await png.Content.ReadAsByteArrayAsync()).Width == 128, "HTTP preview invalid");
            var build = await client.PostAsJsonAsync($"/api/agent/tilesets/{f.Token}/{d.Id}/build", new AgentTilesetEndpoints.BuildRequest(d.Revision)); build.EnsureSuccessStatusCode(); d = (await build.Content.ReadFromJsonAsync<AgentTilesetDraft>())!;
            await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri(client.BaseAddress!, "/mcp") }));
            var tools = await mcp.ListToolsAsync(); Check(tools.Count == 9 && tools.Any(t => t.Name == "tileset_mod_attach"), "MCP tool discovery/schema failed");
            var toolPreview = await mcp.CallToolAsync("tileset_frame_preview", new Dictionary<string, object?> { ["sessionToken"] = f.Token, ["draftId"] = d.Id, ["frameIndex"] = 0 });
            Check(toolPreview.IsError != true && toolPreview.Content.OfType<ImageContentBlock>().Single().DecodedData.Span.SequenceEqual(f.Store.Preview(f.Token, d.Id, 0)), "MCP wire preview is not native PNG image content");
            var metadata = JsonSerializer.Deserialize<JsonElement>(toolPreview.Content.OfType<TextContentBlock>().Single().Text);
            Check(metadata.GetProperty("Origin").GetString() == "validated-native-pack", "MCP wire preview bypassed native pack");
            var zipResponse = await client.GetAsync(d.DownloadUrl); Check(zipResponse.Content.Headers.ContentType?.MediaType == "application/zip", "ZIP download missing");
            using var zip = new ZipArchive(new MemoryStream(await zipResponse.Content.ReadAsByteArrayAsync())); Check(zip.Entries.Any(e => e.FullName.EndsWith(".pack")), "Downloaded ZIP has no pack");
            var attachUrl = $"/api/agent/tilesets/{f.Token}/{d.Id}/attach";
            var attach = await client.PostAsJsonAsync(attachUrl, new AgentTilesetEndpoints.AttachRequest(1, d.Revision)); attach.EnsureSuccessStatusCode();
            var stale = await client.PostAsJsonAsync(attachUrl, new AgentTilesetEndpoints.AttachRequest(1, d.Revision)); Check(stale.StatusCode == HttpStatusCode.Conflict, "HTTP stale attach is not 409");
            var oversized = await client.PostAsync("/api/agent/tilesets", new StringContent(new string('x', AgentTilesetEndpoints.MaximumRequestBytes + 1), Encoding.UTF8, "application/json")); Check(oversized.StatusCode == HttpStatusCode.RequestEntityTooLarge, "HTTP upload body not bounded");
        }
        finally { await app.StopAsync(); }
    }

    static string OrdinaryMod(Fixture f)
    {
        var root = Path.Combine(f.Root, "ordinary-mods"); var mod = Path.Combine(root, "ordinary_fixture");
        Directory.CreateDirectory(Path.Combine(mod, "media"));
        File.WriteAllText(Path.Combine(mod, "mod.info"), "id=ordinary_fixture\nname=Ordinary fixture\ntiledef=ordinary_fixture_tiles 799\n");
        File.WriteAllBytes(Path.Combine(mod, "media", "ordinary_fixture_tiles.tiles"), TilesetBuilder.Tiles("ordinary_fixture_tiles", [new() { ["solid"] = "" }]));
        f.Assets.DiscoverMods([root], "42.0.0"); f.Assets.ActivateMods(["ordinary_fixture"]);
        return root;
    }

    static AgentTilesetSpec Spec() => new("agent_test", "Agent test sprites", "agent_sign_01", 700,
        new("generated", "Test fixture, deterministic pixels", "AgentTilesetTests", "CC0-1.0", Generator: "test fixture", Prompt: "Two independent north and west fixture frames"),
        [new("north", "n", CopyFrom: "vanilla_wall_0"), new("west", "w", CopyFrom: "vanilla_wall_1")]);
    static AgentTilesetDraft Upload(Fixture f, AgentTilesetDraft d, int i, byte r, byte g) => f.Store.Upload(f.Token, d.Id, d.Revision, i, Convert.ToBase64String(Pixels(128, 256, r, g)));
    static AgentTilesetDraft UploadAll(Fixture f, AgentTilesetDraft d) { for (var i = 0; i < d.Spec.Frames.Length; i++) d = Upload(f, d, i, (byte)(30 + i), 40); return d; }
    static byte[] Pixels(int width, int height, byte r, byte g)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 3; y < height - 2; y++) for (var x = 4; x < width - 2; x++) { var i = (y * width + x) * 4; rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = (byte)(x % 255); rgba[i + 3] = (byte)(y % 2 == 0 ? 255 : 128); }
        return Png.Encode(new(width, height, rgba));
    }
    sealed class Fixture(string root)
    {
        public string Root { get; } = root;
        public string Game { get; } = Path.Combine(root, "game");
        public string VanillaDefinitions => Path.Combine(Game, "media", "newtiledefinitions.tiles.txt");
        public string MapDirectory => Path.Combine(Root, "maps");
        public string Drafts => Path.Combine(Root, "drafts");
        public AgentProjectStore Maps { get; private set; } = null!;
        public AgentWorldStore Worlds { get; private set; } = null!;
        public GameAssetCatalog Assets { get; private set; } = null!;
        public AgentTilesetStore Store { get; private set; } = null!;
        public string Token { get; private set; } = "";
        public void Initialize()
        {
            Directory.CreateDirectory(Path.Combine(Game, "media", "texturepacks"));
            File.WriteAllText(VanillaDefinitions, "// vanilla_wall_0\ntile\n{\nWallN=\ncollideN=\nFacing=N\n}\n// vanilla_wall_1\ntile\n{\nWallW=\ncollideW=\nFacing=W\n}\n");
            Maps = new(MapDirectory); Worlds = new(Path.Combine(Root, "worlds")); Assets = new(Root, Game, new GameInstallationDiscovery([], false));
            Store = new(Drafts, Maps, Worlds, Assets); Token = Maps.Create(MapGenerator.Generate(new() { Kind = "blank", Size = 256 })).Token;
        }
    }
    static void WithFixture(Action<Fixture> action)
    {
        var parent = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE") ?? Directory.GetCurrentDirectory(), "work", "agent-tileset-tests"));
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { var f = new Fixture(root); f.Initialize(); action(f); }
        finally { var full = ModRegistry.SafePath(root); if (!full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup"); Directory.Delete(full, true); }
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid tileset accepted"); }
    static void Conflict(Action action) { try { action(); } catch (AgentRevisionConflict) { return; } throw new Exception("Stale revision accepted"); }
}
