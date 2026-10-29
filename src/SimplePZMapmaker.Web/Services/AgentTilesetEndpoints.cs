using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace SimplePZMapmaker.Web.Services;

public static class AgentTilesetEndpoints
{
    public const int MaximumRequestBytes = AgentTilesetStore.MaximumBase64Characters + 128 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    public sealed record CreateRequest(string SessionToken, long ExpectedRevision, AgentTilesetSpec Spec, string Scope = "map", string? MemberId = null);
    public sealed record UploadRequest(long ExpectedDraftRevision, string PngBase64);
    public sealed record BuildRequest(long ExpectedDraftRevision);
    public sealed record AttachRequest(long ExpectedRevision, long ExpectedDraftRevision);

    public static void MapAgentTilesetEndpoints(this WebApplication app)
    {
        app.MapGet("/api/agent/tilesets/capabilities", () => AgentTilesetStore.Capabilities());
        app.MapGet("/api/agent/tilesets/{token}", (string token, AgentTilesetStore store) => Run(() => Results.Json(store.List(token))));
        app.MapGet("/api/agent/tilesets/{token}/{draftId}", (string token, string draftId, AgentTilesetStore store) => Run(() => Results.Json(store.Read(token, draftId))));
        app.MapPost("/api/agent/tilesets", (HttpRequest request, AgentTilesetStore store, CancellationToken cancellation) => Body<CreateRequest>(request, r => Results.Json(store.Create(r.SessionToken, r.ExpectedRevision, r.Spec, r.Scope, r.MemberId)), cancellation));
        app.MapPost("/api/agent/tilesets/{token}/{draftId}/frames/{frameIndex:int}", (string token, string draftId, int frameIndex, HttpRequest request, AgentTilesetStore store, CancellationToken cancellation) => Body<UploadRequest>(request, r => Results.Json(store.Upload(token, draftId, r.ExpectedDraftRevision, frameIndex, r.PngBase64)), cancellation));
        app.MapGet("/api/agent/tilesets/{token}/{draftId}/frames/{frameIndex:int}.png", (string token, string draftId, int frameIndex, AgentTilesetStore store) => Run(() => Results.File(store.Preview(token, draftId, frameIndex), "image/png")));
        app.MapGet("/api/agent/tilesets/{token}/{draftId}/frames/{frameIndex:int}/metadata", (string token, string draftId, int frameIndex, AgentTilesetStore store) => Run(() => Results.Json(store.PreviewInfo(token, draftId, frameIndex))));
        app.MapPost("/api/agent/tilesets/{token}/{draftId}/build", (string token, string draftId, HttpRequest request, AgentTilesetStore store, CancellationToken cancellation) => Body<BuildRequest>(request, r => Results.Json(store.Build(token, draftId, r.ExpectedDraftRevision)), cancellation));
        app.MapGet("/api/agent/tilesets/{token}/{draftId}/mod.zip", (string token, string draftId, AgentTilesetStore store) => Run(() => Results.File(store.Download(token, draftId), "application/zip", store.Read(token, draftId).Spec.ModId + ".zip")));
        app.MapPost("/api/agent/tilesets/{token}/{draftId}/attach", (string token, string draftId, HttpRequest request, AgentTilesetStore store, CancellationToken cancellation) => Body<AttachRequest>(request, r => Results.Json(store.Attach(token, draftId, r.ExpectedRevision, r.ExpectedDraftRevision)), cancellation));
    }

    static IResult Run(Func<IResult> action)
    {
        try { return action(); }
        catch (AgentRevisionConflict e) { return Results.Json(new { error = e.Message, currentRevision = e.Current }, statusCode: 409); }
        catch (Exception e) when (e is ArgumentException or JsonException or BadHttpRequestException) { return Results.BadRequest(new { error = e.Message }); }
    }

    static async Task<IResult> Body<T>(HttpRequest request, Func<T, IResult> action, CancellationToken cancellation)
    {
        try
        {
            var limit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = MaximumRequestBytes;
            if (request.ContentLength > MaximumRequestBytes) return Results.StatusCode(413);
            using var buffer = new MemoryStream(); var chunk = new byte[16 * 1024];
            while (true)
            {
                var read = await request.Body.ReadAsync(chunk, cancellation); if (read == 0) break;
                if (buffer.Length + read > MaximumRequestBytes) return Results.StatusCode(413);
                buffer.Write(chunk, 0, read);
            }
            var value = JsonSerializer.Deserialize<T>(buffer.ToArray(), Json) ?? throw new ArgumentException("Request body is required.");
            return Run(() => action(value));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or BadHttpRequestException) { return Results.BadRequest(new { error = e.Message }); }
    }
}
