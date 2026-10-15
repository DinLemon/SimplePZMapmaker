# Agent-assisted building, neighbourhood and world design

Start the application, open a map and choose **Connect agent**, then connect the current map. Give your agent the endpoint `http://127.0.0.1:48765/mcp` and session key from the dialog. Download the sample MCP configuration to add a Streamable HTTP server to your client. The key is passed in the `sessionToken` tool argument, not stored in the client configuration. The editor does not alter Codex settings on the system drive.

The server uses the [official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk), version 2.2.0. The verification client is `tools/McpSmoke`; protocols 2026-07-28 and 2025-11-25 are supported. The endpoint is loopback-only and offers neither shell execution nor arbitrary filesystem operations.

## Map and building workflow

1. Call `project_summary` to get the current revision.
2. Discover real resources with `assets_search`, `assets_placement`, `assets_pair`, `models_search` and `models_placement`.
3. Enumerate original plans with `building_templates`. Create an isolated draft from a template, selected building or generator using `building_draft_create`. It does not change the map yet.
4. Use `building_draft_read` for rooms/levels and `building_draft_room`, `building_draft_edge`, `building_draft_object` to edit parts. `building_draft_replace` accepts a full multi-level/free-form design.
5. Use `building_draft_preview` for actual-sprite composition and `building_draft_validate` to check stairs, entrances and room access. Incomplete access is allowed in a draft, but not at commit/export.
6. Commit with `building_draft_commit`, supplying `expectedRevision`, placement and optionally the ID to replace.

`environment_place_sprite`, `environment_place_model`, `environment_paint` and `project_remove_object` also require the current revision. Stale commands retain the map: reread `project_summary` before retrying. `project_export` returns a verified native ZIP link.

The editor receives an agent revision in one undoable operation. Sending editor changes stops on a revision conflict and retains the manual map. Undo restores the preceding editor state after receiving agent changes. Uncommitted building drafts never appear in the editor. Closing a session revokes its key.

Map sessions/drafts live in `work/agent-sessions/`; map recovery is `work/recovery/`. These files contain resource references, not game artwork. Limits are 48 MB per map session, 32 saved sessions and 16 drafts per session. Share a key only with the agent authorized to change that project.

## Neighbourhoods and worlds (0.7)

In **Connect agent**, choose the current map or the entire world; add sections before connecting a world. Give the agent the URL, key, scope and revision. Map/world keys refer to separate sessions.

Before receiving changes, inspect the before/after comparison and object list, then explicitly receive the reviewed changes. If the local project or remote revision changes after review, generate a fresh comparison. Undo restores the previous project in one action.

The server exposes 55 tools. `building_template_read` returns the complete original plan. `world_buildings`, `world_building_read`, `world_building_replace` and `world_building_put` read/edit buildings, including shared buildings across sections. Shared positions use absolute game-tile coordinates; section origins use native cell coordinates (256×256 tiles per cell).

`world_section_generate`, `world_section_move`, `world_remove_object`, `world_paint_terrain` and `world_section_preview` edit/inspect worlds; `world_export` produces one combined mod. Section previews include the native parts of shared buildings.

`neighborhood_plan_create`/`neighborhood_plan_commit` and `world_plan_create`/`world_plan_commit` separate proposals from application. Constraints specify the area, allowed `templateIds`, minimum/maximum buildings, maximum levels, spacing, seed and roads. The building budget is distributed across selected sections while protecting water and existing objects. The `objective` describes the agent's task; the placement algorithm uses explicit constraints and selected templates. Unused budget is reported; an impossible minimum is rejected.

World sessions live in `work/agent-world-sessions/`, limited to 16 sessions of 128 MB each. Proposals live in `work/agent-plans/`, at most 16 per session, 12 MB each, and 128 buildings per assignment. Closing a session removes its proposals and revokes access. World 2 preserves shared buildings/DEM; map 7 and template 5 preserve native model behavior. Earlier formats remain readable.

## Verify the SDK client

With the application running, from the repository root:

```powershell
. ./scripts/WorkspaceEnv.ps1
dotnet run --project tools/McpSmoke
```

The check creates its own map/world sessions, commits a building draft and neighbourhood plans, verifies revision conflicts, a shared seam-crossing building and a native ZIP, then closes its sessions. It does not alter the user's editor project.

## Native lighting and gameplay-zone proposals (0.10.0)

Use `lighting_status`, `lighting_map_check`, `lighting_world_check` and `lighting_draft_check` for authentic installed LightingJNI results, signed levels -32..31, 8–64 tile crops and scene/library fingerprints. Checks are read-only, cancelable and revision-protected; no game session is started. `gameplay_catalog` supplies supported zones and literal registered vehicle/loot distributions. `gameplay_zones_read`, `gameplay_zones_plan`, `gameplay_zones_plan_read` and `gameplay_zones_plan_commit` separate exact 1–64 rectangle proposals from atomic revision-protected commits. World entries use a section memberId and local tile coordinates. Review the zone rectangles in Connect agent before receiving the updated session. See the [illustrated guide](EDITOR-EXPANSION.md#agent-lighting-and-gameplay-zones).

## Checked design tasks (0.11.0)

The following tools add a complete bounded proposal/check/repair workflow. A task has its own `taskRevision` and an immutable source `baseRevision`. Checking or repairing a task does not change the connected map/world.

1. Call `design_parametric_building` to generate actual apartment rooms and reusable entrance modules, or prepare a complete building draft with the existing building tools. Choose local tile coordinates inside the design rectangle. Apartment options count units **per entrance per floor**; there are at most eight units per floor because the native room code table holds 35 entries.
2. Call `design_task_create` with `sessionToken`, `expectedRevision`, `scope`, `constraints` and optional `replacements`. A world task also needs a section `memberId`. Existing and replacement building bounds must both fit inside the task rectangle, including exterior edges. Optional `neighborhood` constraints generate a bounded neighborhood in that candidate first. Shared buildings and infrastructure are included when a world candidate is checked.
3. Call `design_task_check` with its ID and exact task revision. Access checks include authored wall/window/door edges, full blocking footprints, locked model doors, dry ground and actual stair landings. The report states crop and node coverage. Unsafe upper edges are warnings; they remain visible for manual review.
4. Call `design_task_repair` to try the explicitly permitted changes. The loop can add doors through unedited walls and supported stairs, then selected registered native lights. It preserves furniture, door locks and geometry outside the rectangle. Set `allowEditedEdges` only when replacing manually authored facade edges is intended. A lamp requires `allowLights=true` and an explicit registered `lampModelId`; use resource discovery to choose the visible object.
5. Read `design_task_read` and inspect **Checked design proposals** in **Connect agent**. Review before/after images, the exact fixes, access coverage, lighting scenario and engine fingerprints. Recheck if either revision changed. `design_task_update` replaces bounded drafts and clears prior checks.
6. Call `design_task_commit` separately. It accepts only the latest passing complete check at the original source revision and revalidates assets. A changed lighting library/JAR/adapter requires another check. Then review and receive the new agent session revision through the existing editor controls. Receiving it is one undoable operation.

`design_access_check` also checks a source revision directly, with optional local `start`/`end` coordinates and signed Z levels. It changes neither the source nor a task.

Example constraints for a building near local tile 16,16:

```json
{
  "x": 0, "y": 0, "width": 96, "height": 96,
  "maximumIterations": 6, "maximumChanges": 10,
  "allowNewDoors": true, "allowStairs": true, "allowEditedEdges": false,
  "requireLighting": true,
  "lightingX": 8, "lightingY": 8, "lightingSize": 48, "lightingLevel": 0,
  "skyRgb": [0.2, 0.2, 0.2], "ambient": 0.2,
  "minimumLight": 0.15,
  "roomLightsPowered": true,
  "allowLights": false
}
```

Lighting covers **one explicit signed floor and crop**, not the entire world. Its minimum, average and dark fraction measure the native RGB mean on authored interior cells in that crop. Set `roomLightsPowered=false` to test authored point lights without the room-power assumption. Inaccessible drafts block the strict native scene calculation until repaired. Missing native libraries, an empty interior crop, incomplete access coverage or an unmet brightness threshold do not produce a passing result. `requireLighting=false` explicitly requests access-only checking; it is never displayed as a native-lighting pass.

Tasks use `work/agent-design-tasks/`: eight proposals per session, 48 MB per proposal and 192 MB per session. There are at most eight repair iterations per invocation, 16 accepted changes per task and 32 recorded checks. Cancellation or a stale revision leaves the connected source untouched. Closing the session removes its checked-design proposals. These are static authoring checks under stated assumptions; no game session or character traversal is used.

![Detailed source/proposal room geometry and recorded genuine lighting evidence](screenshots/23-agent-design.png)
