# Pilna geographic import and GitHub delivery

User authorized continuing development, importing the settlement linked at https://ru.wikipedia.org/wiki/Пильна and creating/uploading a GitHub repository. Existing carte blanche remains in force. All local work, including credentials handling, excludes new task files outside this repository; secrets never enter files or command output.

Nominatim identifies Pilna as OSM way158713814, bounds55.5362737..55.5780901 latitude and45.8833248..45.9421580 longitude. Live Overpass has now returned2378 features including1789 building outlines and439 highway ways, unlike the prior service outages. Capture source data with ODbL attribution and reproducible import recipe.

Deliver: maps up to2048 squares, geographically scaled road widths; choose an entire search result's bounds instead of only a small square around its center; show tile scale and source/import/skip counts. Store a real Pilna2048 editable example plus exported B42 mod, expose it in the local UI, and test source→import→save→reload→export with the actual dataset. Do not describe GIS buildings as exact real interiors or claim game playtesting.

Repositories: create DinLemon/SimplePZMapmaker privately by default, preserving local develop history; upload tracked source, docs, licensed examples and CI. Exclude work/cache/downloaded tools, user secrets, installed game files and incidental machine-specific runtime artifacts. Confirm the remote commit and example files after push. This is explicit user authorization to create the repository and push.

Performance: avoid quadratic generation when adding larger maps; bound HTTP/file sizes and retain raster complexity checks. Target map payload fits the existing24MB request and16MB project file limits. Existing smaller maps remain readable.
