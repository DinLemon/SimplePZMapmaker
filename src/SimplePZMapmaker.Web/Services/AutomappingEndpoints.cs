using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

/// <summary>Stateless catalogs and previews; application/editor integration is explicit.</summary>
public static class AutomappingEndpoints
{
    public sealed record PreviewRequest(MapProject Project, AutomappingOptions Options, JsonElement? Catalog = null);
    public sealed record ApplyRequest(MapProject Project, AutomappingPlan Plan, JsonElement? Catalog = null);

    // The caller owns loading the trusted bundled catalog. An explicit stream fallback
    // lets the host integrate this without changing the Core embedded resources first.
    public static void MapAutomappingEndpoints(this WebApplication app, StreetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        app.MapGet("/api/automapping/catalog", () => new
        {
            catalog = catalog.Snapshot(), fingerprint = catalog.Fingerprint,
            maximumBytes = StreetCatalog.MaximumBytes, maximumArea = Automapping.MaximumArea,
            maximumDecorations = Automapping.MaximumDecorations
        });
        app.MapPost("/api/automapping/catalog/validate", async (HttpRequest request, CancellationToken cancellation) =>
        {
            try
            {
                if (request.ContentLength > StreetCatalog.MaximumBytes)
                    return Results.Json(new { error = "Street catalog exceeds 1 MiB." }, statusCode: 413);
                using var input = new MemoryStream();
                var bytes = new byte[8192];
                int read;
                while ((read = await request.Body.ReadAsync(bytes.AsMemory(0,
                    (int)Math.Min(bytes.Length, StreetCatalog.MaximumBytes + 1 - input.Length)), cancellation)) > 0)
                {
                    input.Write(bytes, 0, read);
                    if (input.Length > StreetCatalog.MaximumBytes)
                        return Results.Json(new { error = "Street catalog exceeds 1 MiB." }, statusCode: 413);
                }
                input.Position = 0;
                var next = catalog.Update(input);
                return Results.Ok(new { catalog = next.Snapshot(), fingerprint = next.Fingerprint });
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        app.MapPost("/api/automapping/preview", (PreviewRequest request, GameAssetCatalog assets) =>
        {
            try { return Results.Ok(Preview(request, catalog, assets)); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        app.MapPost("/api/automapping/apply", (ApplyRequest request, GameAssetCatalog assets) =>
        {
            try { return Results.Ok(new { project = Apply(request, catalog, assets) }); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
    }

    private static StreetCatalog SelectCatalog(JsonElement? user, StreetCatalog bundled)
    {
        if (user is null || user.Value.ValueKind == JsonValueKind.Null) return bundled;
        var raw = user.Value.GetRawText();
        if (raw.Length > StreetCatalog.MaximumBytes) throw new ArgumentException("Street catalog exceeds 1 MiB.");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        // A validated custom document travels with each request; one browser can never
        // replace the global catalog or another editor's preview.
        return bundled.Update(input);
    }

    private static Func<string, AutomappingResource> Resolve(GameAssetCatalog assets)
    {
        if (!assets.Ready) throw new ArgumentException("Load Project Zomboid game resources before previewing street automapping.");
        return tile =>
        {
            var asset = assets.Get(tile);
            if (asset.Floor || asset.Properties.ContainsKey("water"))
                throw new ArgumentException("Street overlays require a dry overlay sprite, not a floor tile: " + tile);
            return new(GameAssetCatalog.Flags(asset), asset.ModId ?? "game", asset.Requirements);
        };
    }

    public static AutomappingPlan Preview(PreviewRequest request, StreetCatalog bundled, GameAssetCatalog assets)
    {
        if (request is null || request.Project is null || request.Options is null)
            throw new ArgumentException("An active map section and automapping options are required.");
        var catalog = SelectCatalog(request.Catalog, bundled);
        var plan = Automapping.Preview(request.Project, catalog, request.Options, Resolve(assets));
        // Check only proposed additions; unrelated authored objects are retained verbatim.
        // Incomplete/missing mod sprite grids fail here, before a preview can be applied.
        foreach (var decoration in plan.Decorations) AssetRules.CheckParts(assets, decoration.Asset, decoration.Parts);
        return plan;
    }

    public static MapProject Apply(ApplyRequest request, StreetCatalog bundled, GameAssetCatalog assets)
    {
        if (request is null || request.Project is null || request.Plan is null)
            throw new ArgumentException("An active map section and street preview are required.");
        var catalog = SelectCatalog(request.Catalog, bundled);
        var candidate = Automapping.Apply(request.Project, catalog, request.Plan, Resolve(assets));
        foreach (var decoration in request.Plan.Decorations) AssetRules.CheckParts(assets, decoration.Asset, decoration.Parts);
        ResourceDependencies.Validate(candidate.RequiredMods, candidate.AssetSources);
        return candidate;
    }
}
