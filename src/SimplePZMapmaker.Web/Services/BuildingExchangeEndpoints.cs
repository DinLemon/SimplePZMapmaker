using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Exchange;

namespace SimplePZMapmaker.Web.Services;

/// <summary>Stateless previews only. The caller owns placement, history, resource checks and atomic replacement.</summary>
public static class BuildingExchangeEndpoints
{
    public const int MaxRequestBytes = 16 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    public sealed record ImportRequest(string Format, string Xml, bool AllowLossy = false);
    public sealed record ExportRequest(Building Building, string Format, bool AllowLossy = false);

    public static void MapBuildingExchangeEndpoints(this WebApplication app)
    {
        app.MapPost("/api/building/exchange/import", async (HttpRequest request, CancellationToken cancellation) =>
        {
            try
            {
                var body = await Read<ImportRequest>(request, cancellation);
                return Results.Json(BuildingExchange.Import(body.Xml, body.Format, body.AllowLossy));
            }
            catch (Exception e) when (e is ArgumentException or JsonException or BadHttpRequestException)
            { return Invalid(e); }
        });
        app.MapPost("/api/building/exchange/export", async (HttpRequest request, CancellationToken cancellation) =>
        {
            try
            {
                var body = await Read<ExportRequest>(request, cancellation);
                return Results.Json(BuildingExchange.Export(body.Building, body.Format, body.AllowLossy));
            }
            catch (Exception e) when (e is ArgumentException or JsonException or BadHttpRequestException)
            { return Invalid(e); }
        });
    }

    static IResult Invalid(Exception error) => Results.Json(new BuildingExchangeResult(null, null,
        [new("invalid-request", "error", "/", error is JsonException ? "Malformed exchange JSON." : error.Message)]), statusCode: error is BadHttpRequestException bad ? bad.StatusCode : 400);

    static async Task<T> Read<T>(HttpRequest request, CancellationToken cancellation)
    {
        if (!request.HasJsonContentType()) throw new BadHttpRequestException("Expected application/json.", 415);
        if (request.ContentLength > MaxRequestBytes) throw new BadHttpRequestException("Exchange request exceeds 16 MiB.", 413);
        using var bytes = new MemoryStream();
        var chunk = new byte[16384]; int count;
        while ((count = await request.Body.ReadAsync(chunk, cancellation)) > 0)
        {
            if (bytes.Length + count > MaxRequestBytes) throw new BadHttpRequestException("Exchange request exceeds 16 MiB.", 413);
            await bytes.WriteAsync(chunk.AsMemory(0, count), cancellation);
        }
        return JsonSerializer.Deserialize<T>(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), Json) ?? throw new ArgumentException("Exchange request is empty.");
    }
}
