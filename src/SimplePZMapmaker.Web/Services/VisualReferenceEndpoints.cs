using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public static class VisualReferenceEndpoints
{
    const int MaximumJsonBytes = 512 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public sealed record TargetRequest(long Revision, VisualTargetRequest Target);
    public sealed record SearchRequest(long Revision, VisualTargetRequest Target, int RadiusMetres = 150, string Provider = "mapillary");
    public sealed record ImportRequest(long Revision, VisualTargetRequest Target, string SourceId, string Provider = "mapillary");
    public sealed record OwnUploadRequest(long Revision, VisualTargetRequest Target, OwnPhotoProvenance Provenance, string Base64, string MimeType);
    public sealed record ObservationRequest(long Revision, string ReceiptId, VisualObservations Observations);
    public sealed record ProposalRequest(long Revision, string ObservationId, Building Draft, DesignConstraints Constraints, string InteriorMode = "preserve");

    public static void MapVisualReferenceEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/visual-references");
        routes.MapGet("/capabilities", (McpReferenceTools tools) => tools.Capabilities());
        routes.MapGet("/{session}", (string session, McpReferenceTools tools) => Run(() => tools.List(session)));
        routes.MapPost("/{session}/target", (string session, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            Body<TargetRequest>(request, ct, r => tools.Target(session, r.Revision, r.Target)));
        routes.MapPost("/{session}/list", (string session, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            Body<TargetRequest>(request, ct, r => tools.List(session, r.Revision, r.Target)));
        routes.MapPost("/{session}/mapillary/search", (string session, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            BodyAsync<SearchRequest>(request, ct, async r => await tools.Search(session, r.Revision, r.Target, r.Provider, r.RadiusMetres, ct)));
        routes.MapPost("/{session}/mapillary/import", (string session, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            BodyAsync<ImportRequest>(request, ct, async r => await tools.Import(session, r.Revision, r.Target, r.SourceId, r.Provider, ct)));
        // Keep uploads behind the application's same-origin JSON mutation gate.
        routes.MapPost("/{session}/own", (string session, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            BodyAsync<OwnUploadRequest>(request, ct, r => Task.FromResult<object>(tools.Upload(session, r.Revision, r.Target, r.Base64, r.MimeType, r.Provenance)), 12 * 1024 * 1024));
        routes.MapGet("/{session}/{id}/image", (string session, string id, HttpContext context, VisualReferenceStore store) =>
        {
            try
            {
                var image = store.ReadImage(session, id);
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                return Results.File(image.Bytes, image.Reference.MimeType);
            }
            catch (Exception e) when (Handled(e)) { return Error(e); }
        });
        routes.MapPost("/{session}/{id}/bind", (string session, string id, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            Body<TargetRequest>(request, ct, r => tools.Bind(session, r.Revision, id, r.Target)));
        routes.MapGet("/{session}/{id}/observations", (string session, string id, VisualReferenceStore store) => Run(() => store.ListObservations(session, id)));
        routes.MapPost("/{session}/{id}/observations", (string session, string id, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            Body<ObservationRequest>(request, ct, r => tools.Observe(session, r.Revision, id, r.ReceiptId, r.Observations)));
        routes.MapPost("/{session}/propose", (string session, HttpRequest request, McpReferenceTools tools, CancellationToken ct) =>
            Body<ProposalRequest>(request, ct, r => tools.Propose(session, r.Revision, r.ObservationId, r.Draft, r.Constraints, r.InteriorMode)));
    }
    static bool Handled(Exception e) => e is ArgumentException or JsonException or AgentRevisionConflict or VisualReferenceCapabilityException or BadHttpRequestException or IOException;
    static IResult Error(Exception error) => error switch
    {
        AgentRevisionConflict revision => Results.Json(new { error = revision.Message, currentRevision = revision.Current }, statusCode: 409),
        VisualReferenceCapabilityException capability => Results.Json(new { error = capability.Message, capability = "mapillary-or-design" }, statusCode: 503),
        JsonException => Results.Json(new { error = "Invalid reference JSON; check supported fields and types." }, statusCode: 400),
        BadHttpRequestException bad => Results.Json(new { error = "Reference request is invalid or exceeds its byte budget." }, statusCode: bad.StatusCode),
        IOException => Results.Json(new { error = "Reference workspace data is unavailable." }, statusCode: 400),
        _ => Results.Json(new { error = error.Message }, statusCode: 400)
    };
    static IResult Run(Func<object> action) { try { return Results.Json(action()); } catch (Exception e) when (Handled(e)) { return Error(e); } }
    static Task<IResult> Body<T>(HttpRequest request, CancellationToken cancellation, Func<T, object> action) =>
        BodyAsync<T>(request, cancellation, value => Task.FromResult(action(value)));
    static async Task<IResult> BodyAsync<T>(HttpRequest request, CancellationToken cancellation, Func<T, Task<object>> action, int maximumBytes = MaximumJsonBytes)
    {
        try
        {
            if (!request.HasJsonContentType()) return Results.Json(new { error = "Expected application/json." }, statusCode: 415);
            if (request.ContentLength > maximumBytes) return Results.Json(new { error = "Reference JSON exceeds its byte budget." }, statusCode: 413);
            using var content = new StreamContent(request.Body);
            var bytes = await VisualReferenceStore.Limited(content, maximumBytes, cancellation);
            var body = JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new ArgumentException("Missing reference request.");
            return Results.Json(await action(body));
        }
        catch (Exception e) when (Handled(e)) { return Error(e); }
    }
}
