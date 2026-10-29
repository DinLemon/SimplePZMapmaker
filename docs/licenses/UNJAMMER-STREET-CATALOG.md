# PZ Mapping Tools street data attribution

The street catalog was transcribed from [Unjammer/PZ_Mapping_Tools](https://github.com/Unjammer/PZ_Mapping_Tools), pinned commit **70e8b77676183e579cf9f1f7100dff6c383e28cb**. It contains tile identifiers, geometric offsets, directions and layer names, not game images or Lua executable code.

Credit: Tim Baker for the original TileZed/WorldEd foundation; Alree / Unjammer for the maintained PZ Mapping Tools continuation; and the contributors identified in the upstream sources and [AUTHORS.txt](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/AUTHORS.txt). No individual authorship beyond those upstream notices is asserted for these particular data tables.

Inspected files:

- [TileZed/lua/tool-curb-data.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/tool-curb-data.lua)
- [TileZed/lua/parking-stall.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/parking-stall.lua)
- [TileZed/lua/tool-street-decoration-data.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/tool-street-decoration-data.lua)
- [TileZed/lua/tool-curb.lua](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/TileZed/lua/tool-curb.lua), consulted for edge semantics only.

The inspected Lua files have no separate per-file license header. Upstream [COPYING](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/COPYING) describes GPL version 2 for the editor source, with a later-version option only where a source file says so. This attribution therefore conservatively identifies these catalog inputs as **GPL-2.0**, without inventing a permissive or later-version exception. Upstream [THIRD_PARTY_NOTICES.txt](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/THIRD_PARTY_NOTICES.txt) distinguishes the editor source from separately licensed components. See the full [GPL version 2 text in this repository](../../LICENSE) and [upstream GPL-2.0.txt](https://github.com/Unjammer/PZ_Mapping_Tools/blob/70e8b77676183e579cf9f1f7100dff6c383e28cb/licenses/GPL-2.0.txt).

Local changes: JSON representation, schema/revision and provenance fields, declarative terrain-neighborhood rules, bounded validation and additive preview/apply behavior. The original curb near/far keys, drain directions, signed parking offsets and both parking layer names remain intact. The new C# and JavaScript implementation is independently written for SimplePZMapmaker under its repository license. Game art remains owned by its respective rights holders and must come from the user's game installation or registered mods.
