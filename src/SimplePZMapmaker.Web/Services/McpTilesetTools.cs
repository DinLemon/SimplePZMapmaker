using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SimplePZMapmaker.Web.Services;

[McpServerToolType]
public sealed class McpTilesetTools(AgentTilesetStore tilesets)
{
    [McpServerTool(Name = "tileset_capabilities", ReadOnly = true, UseStructuredContent = true),
     Description("Recipe for agent-created sprites and separate Build 42 tile mods: dimensions, prompts, upload limits, provenance and dependency attachment. The app has NO embedded image generator or credentials. Use your own image tool/provider or supplied PNG artwork; do not invent installed tile IDs or directional rotations.")]
    public object Capabilities() => AgentTilesetStore.Capabilities();

    [McpServerTool(Name = "tileset_draft_create", UseStructuredContent = true),
     Description("Create an isolated sprite/tile-mod draft against an exact map/world revision. Supply explicit canonical modId, tileset, unused FileNumber, provenance/license and 1–64 ordered directional frames. Optional copyFrom must be an existing vanilla tile; properties are snapshotted. Does not change the source project. World scope requires memberId.")]
    public AgentTilesetDraft Create(string sessionToken, long expectedRevision, AgentTilesetSpec spec, string scope = "map", string? memberId = null)
        => tilesets.Create(sessionToken, expectedRevision, spec, scope, memberId);

    [McpServerTool(Name = "tileset_draft_list", ReadOnly = true, UseStructuredContent = true), Description("List this session's sprite drafts, exact tile IDs, uploaded frames, revision and ZIP URLs.")]
    public AgentTilesetDraft[] List(string sessionToken) => tilesets.List(sessionToken);

    [McpServerTool(Name = "tileset_draft_read", ReadOnly = true, UseStructuredContent = true), Description("Read a sprite draft and the actual snapshotted native properties of its explicit frames.")]
    public AgentTilesetDraft Read(string sessionToken, string draftId) => tilesets.Read(sessionToken, draftId);

    [McpServerTool(Name = "tileset_frame_upload", UseStructuredContent = true), Description("Upload a real PNG frame as plain base64, at most 2 MiB decoded. Exact declared dimensions, RGB/RGBA8, no interlace/animation, valid PNG checksums and visible pixels required. Uses expectedDraftRevision; never invokes an image generator. Built drafts are immutable.")]
    public AgentTilesetDraft Upload(string sessionToken, string draftId, long expectedDraftRevision, int frameIndex, string pngBase64)
        => tilesets.Upload(sessionToken, draftId, expectedDraftRevision, frameIndex, pngBase64);

    [McpServerTool(Name = "tileset_frame_preview", ReadOnly = true), Description("Return the actual validated uploaded PNG as MCP image content, with transparent canvas and original dimensions. Missing frames fail; no simulated previews or invented artwork.")]
    public CallToolResult Preview(string sessionToken, string draftId, int frameIndex)
        => new() { Content = [ImageContentBlock.FromBytes(tilesets.Preview(sessionToken, draftId, frameIndex), "image/png"),
            new TextContentBlock { Text = JsonSerializer.Serialize(tilesets.PreviewInfo(sessionToken, draftId, frameIndex)) }] };

    [McpServerTool(Name = "tileset_mod_build", UseStructuredContent = true), Description("Build all uploaded frames into a SEPARATE B42 mod ZIP (.pack/.tiles/mod.info/provenance), stage only inside workspace and validate through the real native package and asset readers. Returns a download URL and SHA256. No map changes or writes to Zomboid/mods. Fails on missing pixels, invalid native properties or catalog/FileNumber collisions.")]
    public AgentTilesetDraft Build(string sessionToken, string draftId, long expectedDraftRevision)
        => tilesets.Build(sessionToken, draftId, expectedDraftRevision);

    [McpServerTool(Name = "tileset_mod_download", ReadOnly = true, UseStructuredContent = true), Description("Verify the built ZIP and return its HTTP download URL, SHA256 and mod dependency. Download the separate tile mod alongside the map mod; the map does not embed its assets.")]
    public object Download(string sessionToken, string draftId)
    {
        var bytes = tilesets.Download(sessionToken, draftId); var d = tilesets.Read(sessionToken, draftId);
        return new { d.DownloadUrl, d.Sha256, d.Spec.ModId, bytes = bytes.Length, mimeType = "application/zip" };
    }

    [McpServerTool(Name = "tileset_mod_attach", UseStructuredContent = true), Description("Explicitly attach the validated built tile mod to its draft's map/world member using current expectedRevision and expectedDraftRevision. Atomically stamps RequiredMods/AssetSources and registers actual staged resources. Rejects stale revisions, conflicting providers or changed ZIP/files; rolls back new catalog registration when publication fails. Shared world metadata keeps its owning objects; no tile placement or game installation occurs.")]
    public AgentTilesetAttachment Attach(string sessionToken, string draftId, long expectedRevision, long expectedDraftRevision)
        => tilesets.Attach(sessionToken, draftId, expectedRevision, expectedDraftRevision);
}
