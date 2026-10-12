# Import resilience and appearance UX0.4.1

User reported504 during real map loading and unclear environment/area style selection while0.4.0 release was still a draft. Hold publication; deliver both fixes as0.4.1 without rewriting published Git tags.

- Sequential bounded Overpass failover on transient statuses/timeouts/network failures, bounded per-attempt/overall deadlines, cache, useful source/fallback warnings, reject partial runtime-error JSON, preserve cancel/size/rate limits; fake-provider regression and real requested-location check.
- Prominent appearance entry/dialog with labeled visual environment presets and explicit scope: entire map or a rectangle drawn on the map. Native scoped theme regions preserve exact manual surfaces/decor/buildings; automatic building skins optional. Persist region bounds/theme with a project-format5 gate, retain4-and-earlier reading/templates3. History/import/preview/export share regions.
- Visible selection/instructions/current-applied state, no silent map replacement on error, meaningful status. Real browser region/global/import failures/cancellation and native checks; one independent followup delta review, consolidated fixes, fresh package/release/source/CI checks and running server.
