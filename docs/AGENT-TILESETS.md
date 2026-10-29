# Agent-created sprites and separate Build 42 tile mods

The agent draws missing artwork with its connected `image_gen` tool when available. The editor has no embedded image model, API keys, or implicit paid generation calls. Supplied licensed PNGs or an explicitly selected external image provider are also accepted. The editor validates pixels, creates a native `.pack` and `.tiles`, previews the resulting native resource, and attaches a **separate** tile mod dependency to a map or world member.

## Editor and service integration

Open **Connect agent** from the existing settings menu. Its visual-reference and sprite panels reuse the connected map/world session. Publish editor changes first; a diverged agent revision must be reviewed and received before attaching assets. Draft creation, PNG uploads, native previews and ZIP downloads leave the editor map unchanged. Attaching a mod changes the agent session; the normal review/receive controls apply it as one undoable operation.

The starter form accepts explicit mod/tileset names, an unused FileNumber, artwork provenance and either one object frame or separate N/W frames. Advanced JSON supports generated provenance, more frames, copied vanilla properties and exact physical flags. The connected agent can use the same operations through MCP without the browser form.

`AgentTilesetStore` is a workspace singleton. `Program.cs` registers the endpoints/MCP tools and restores attached workspace resources before saved projects are validated. Restore errors are reported in startup diagnostics. Paths come from the host, never from public API arguments. Resource restoration survives an app restart; it never installs into the real Zomboid profile.

HTTP errors distinguish invalid input, stale project/draft revisions and oversized uploads. The request reader bounds bodies without trusting Content-Length. A failed session save rolls back a newly activated catalog resource and attachment marker. Session URLs contain access keys; treat them like existing agent session URLs.

## Agent recipe

1. Call `tileset_capabilities`. Inspect the real resource catalog before deciding that artwork is missing. Retrieve exact existing vanilla IDs for `copyFrom`; do not guess them. Choose explicit new mod/tileset names and an unused FileNumber. A rejected collision means choose another identity, not overwrite an installed resource.
2. Read the map/world session and its revision. Call `tileset_draft_create` with that revision, source/license provenance and ordered frame declarations. A world draft requires its existing `memberId`. Creation reads an independent source snapshot and retains its base revision. It never modifies the source project.
3. If the connected agent has `image_gen`, use it. Set **`transparent_background=true`**. This is the agent's tool invocation, not an editor generation endpoint. Save the generated source PNG and the actual prompt/provider provenance. For source-authored artwork, retain the original author, source/reference and license, and declare permission to redistribute it.
4. Compose each result into its declared frame with normal image tools: proportional scale, crop, alpha padding and alignment are packaging operations. Preserve alpha. Do not stretch dimensions or rotate a bitmap into another isometric direction. The upload endpoint accepts completed native frames, not arbitrary file paths or automatic URL fetches.
5. Upload plain PNG base64 with `tileset_frame_upload`, using the latest **draft** revision after every upload. Use `tileset_frame_preview` to inspect real PNG image content. Missing, empty, malformed or unsupported images fail; no placeholder is substituted.
6. Call `tileset_mod_build`. This writes a separate B42 mod ZIP, stages it under the draft workspace directory using `NativeModPackage.Install`, discovers its `mod.info` using `ModRegistry`, and validates registered sprites/properties through `GameAssetCatalog`'s existing `MergeMods`, `TexturePackIndex.Read` and `TileDefinitionReader.Read` paths. It never uses the real Zomboid mods directory.
7. Call `tileset_frame_preview` **again after build**. This time the preview is reconstructed from the actual staged `.pack` atlas and native sprite offsets, not from the input PNG. MCP returns an `ImageContentBlock` plus native properties/provenance text. `origin=validated-native-pack` distinguishes this from `origin=uploaded-png`. This inspection works **before attach**, without activating a temporary global source.
8. `tileset_mod_download` verifies the ZIP and returns its URL, SHA256 and byte count. HTTP downloads it as `<modId>.zip`. Download the tile mod alongside the eventual map mod.
9. Call `tileset_mod_attach` explicitly with current `expectedRevision` and exact `expectedDraftRevision`. Attach registers the built resources and atomically stamps `RequiredMods`/`AssetSources` in the selected map/world member. It does not place objects. Place the **returned exact tile IDs** with existing map/building tools afterward. The ordinary native map exporter emits the mod dependency and does not embed the foreign `.pack`/`.tiles`.

### Image prompt

> Draw one [object/material] as a Project Zomboid Build 42 **2x** sprite in isometric game view, with an elevated 30-degree camera and upright verticals. True transparent alpha background. Target native frame **128x256**. Align the object's footprint with the floor plane and match the reference scale. Explicit view: **N** [or **W**, a separate generated frame]. No text, labels, ground plane, checkerboard, contact sheet or cast shadow outside the object. Use only supplied references with recorded provenance.

The normal native 2x frame is 128x256. Explicitly declared 64x128 frames are accepted for appropriate legacy assets; the editor never silently confuses their scale with 2x. Higher-resolution generator output is fine as source material, but must be composed into the declared upload dimensions. A directional wall/fence needs independent N and W frames, each with matching native physical flags. `copyFrom=...` can copy authentic `WallN`/`WallW`, collision, door, window and other tile properties. Direction/property disagreements are rejected. The API has no arbitrary rotate operation.

## MCP operations and HTTP equivalents

- `tileset_capabilities`: `GET /api/agent/tilesets/capabilities`.
- `tileset_draft_create`: `POST /api/agent/tilesets` with `{sessionToken, expectedRevision, spec, scope?, memberId?}`.
- `tileset_draft_list`: `GET /api/agent/tilesets/{token}`.
- `tileset_draft_read`: `GET /api/agent/tilesets/{token}/{draftId}`.
- `tileset_frame_upload`: `POST .../{draftId}/frames/{frameIndex}` with `{expectedDraftRevision, pngBase64}`.
- `tileset_frame_preview`: `GET .../{draftId}/frames/{frameIndex}.png`; native metadata is `GET .../{draftId}/frames/{frameIndex}/metadata`.
- `tileset_mod_build`: `POST .../{draftId}/build` with `{expectedDraftRevision}`.
- `tileset_mod_download`: `GET .../{draftId}/mod.zip`.
- `tileset_mod_attach`: `POST .../{draftId}/attach` with `{expectedRevision, expectedDraftRevision}`.

MCP argument names use `sessionToken`. HTTP POSTs use the records declared in `AgentTilesetEndpoints`. The capability manifest supplies prompts, dimensions and limits to tool consumers, and the browser controls use the same contract.

### Draft spec example

The following names are **new authored identities**, not claims that these resources already exist. FileNumber 701 is only an example: live collision checks must pass. Replace every source/reference/license value with its actual declaration.

```json
{
  "modId": "my_custom_signs",
  "name": "My custom signs",
  "tileset": "my_signs_01",
  "fileNumber": 701,
  "provenance": {
    "kind": "generated",
    "source": "Connected agent image_gen output",
    "reference": "Original generated PNG and its retained content hash",
    "license": "Actual declared usage/redistribution terms",
    "generator": "Actual connected image provider",
    "prompt": "Actual generation prompt, including transparency and the requested view"
  },
  "frames": [
    { "key": "north", "direction": "n", "width": 128, "height": 256,
      "properties": { "Facing": "N", "CustomName": "My sign" } },
    { "key": "west", "direction": "w", "width": 128, "height": 256,
      "properties": { "Facing": "W", "CustomName": "My sign" } }
  ]
}
```

The resulting IDs are exactly `my_signs_01_0` and `my_signs_01_1` in the declared order. They stay stable across uploads, previews, rebuild reads and attach. `copyFrom`, if provided, must be an exact tile currently supplied by vanilla (`Source=game`, no `ModId`). Its property dictionary is copied at draft creation and is not re-read later. Explicit properties override copied values. This snapshots physics without redistributing vanilla image pixels.

For source-authored artwork use `kind=source-authored`, `originalAuthor`, and `canRedistribute=true`, as well as source/reference/license. Generated artwork requires generator/prompt and cannot claim an original source author. Provenance is a declaration, not automatic legal verification. Draft→build is already authorized; there is no additional approval gate. The exported tile mod retains the spec and resolved property provenance in `42/agent-tileset.json`.

## Bounds, identity and publication

- Canonical mod IDs and tileset filenames: lowercase `[a-z][a-z0-9_]{2,63}`, with no trailing underscore. ASCII canonicalization avoids ambiguous filename variants. Caller-supplied names are validated, not silently renamed. FileNumber is 100–8190.
- Names, pack/tiledef registrations and FileNumbers are checked against existing discovered/active mod definitions, catalog tile prefixes and all workspace draft reservations. Checks repeat on build and activation, so newly introduced collisions cannot be bypassed with an old draft.
- Up to 16 drafts per session and 64 explicit frames per draft. Each PNG is at most 2 MiB; all normalized PNGs together at most 16 MiB. A frame is at most 512x1024 and 262144 pixels, with 2097152 pixels across the draft. The ZIP is capped at 32 MiB; native writer/reader bounds apply too.
- Uploads require declared dimensions, 8-bit RGB/RGBA, no interlace/animation, valid chunk CRCs and nonempty visible pixels. Use RGBA for transparency. Properties must be bounded single-line Latin-1, matching the native tiledef encoding. Control characters and lossy Unicode replacement are rejected.
- Built drafts are immutable. Missing frames prevent build; failed validation leaves the draft/source unchanged. Repeated build returns the same verified bytes/revision. Changing artwork requires a new draft and distinct reserved names/numbers.
- ZIP SHA256 and every normalized frame hash are recorded. Download/attach compare staged files with the trusted generated archive, refusing missing/tampered files. There are no API-supplied filesystem destinations, file uploads by path, or arbitrary remote image downloads.
- Attach delegates source modification to `AgentProjectStore.Mutate` / `AgentWorldStore.Mutate`: stale revisions fail before registration. A newly activated catalog registration and its persistence marker roll back if project validation or disk save fails. Previously active valid registrations are retained. Staged, independently downloadable artwork is retained on an attach failure.
- Draft `BaseRevision` records the source snapshot. Attach uses the caller's current explicit `expectedRevision`, so unrelated browser edits are preserved. It never replaces the project with an old copy.
- World attachment stamps the selected member. Shared buildings/decorations that actually use these IDs receive their own provider/dependency metadata. Unrelated members are not stamped; the world has no invented top-level RequiredMods field.
- Successful attachments have a local marker. Store construction revalidates marked ZIPs and restores their sources through the same native loader; restore failures are exposed in `RestoreDiagnostics`. These sources survive a catalog game refresh because they are active mod descriptors. Explicit user mod rescans/deactivation still control the general catalog; attach again at a fresh revision to restore a deliberately deactivated generated mod.
- No game process or mod scripts are executed. Staging is under the configured workspace directory, with checked cleanup paths. The editor never installs into `Zomboid/mods` as part of this pipeline.

## Verification

`tests/SimplePZMapmaker.Tests/AgentTilesetTests.cs` contains fourteen test groups: saved selection recovery, explicit deactivation, pending dependency persistence, complete PNG/atlas pixel and native property round trips, genuine MCP image content, immutability/tampering, licensing/name/direction validation, inactive and late mod collisions, stale revisions/provider conflicts, concurrent attachment, actual disk-save failure with resource rollback, world member/shared metadata and real Kestrel HTTP endpoints including 413/409 behavior.

A real connected-imagegen sample was exercised through HTTP create → upload → native build → pack-derived preview **before attach** → ZIP download → attach → catalog placement → separate native map export. Local artifacts are in `work/agent-tileset-generated-sample/`: `imagegen-source.png`, `west-128x256.png`, `native-pack-preview.png`, `agent_generated_sign_demo.zip`, `sample-map.pzmap.json`, `sample-map-mod.zip`, and `verification.json`. The report records pixel equality, native properties, hashes, source provenance and revisions. These are ignored workspace verification artifacts, not an installed or published game mod. The generated source was created by the connected built-in image tool; normal Pillow composition only scaled/padded the native frame while preserving alpha.

The sample establishes packaging and native reader/catalog compatibility. A B42 game-world playtest remains a separate integration check; no game installation was modified.

Actual editor UI verification on the local Release host passed own-photo upload/manual vision handoff, real PNG upload, byte-identical native-pack preview, separate ZIP download, catalog registration, session dependency review, explicit receive and Undo. Saving also preserves an explicitly attached mod before any sprite placement. The latest Release report is `work/agent-assets-live/aa068e7150e6/result.json`; that test resource was deactivated after verification. Reproduce with `node scripts/verify-agent-assets-live.mjs` against an isolated local host.
