# Basements 0.3.1

Authorized addition: editable levels -1 through -16, with existing 1–16 above-ground floors. Keep all task files/caches on G:. Ground remains game z=0 and UI floor1; sorted floor arrays use a signed MinLevel and always include ground. Project format3 / template format2 prevent older readers silently moving basements above ground; read legacy projects1/2 and templates1.

- [x] Signed model/generator/validation, underground source tags, preservation and meaningful boundary/legacy/stair regressions.
- [x] Native minimum level, all signed chunks/room IDs, stair holes through ground, intact surface/roof/world map; independent and installed-game readback.
- [x] Workshop add/remove/copy basements, labels/ghost/history/templates, signed map views and placement; actual browser cycle down to -16.
- [x] Fresh review, fixes, full checks, package smoke, GitHub source/release/CI and restore running repository app.
