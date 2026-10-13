# Windows runtime distribution

Release 0.7.2 uses generic public installation instructions. Local game/Workshop settings stay in ignored `work/` files or environment variables. Published application binaries omit project debug symbols; compiler source paths are mapped to a neutral prefix. Packaging checks source/archive contents against configured local installation paths. Documentation screenshots mask installation-path and session-key fields. This does not remove earlier commits or release files from Git history.

The Windows x64 package is self-contained: extract the whole archive and run `Start.cmd`. No SDK or separately installed ASP.NET Core Runtime is required. Keep the bundled DLLs and `licenses/` folder with the executable. Detailed release/source installation is in the root [README](../README.md).

Publication pins `RuntimeFrameworkVersion=10.0.12`, matching Microsoft's [.NET 10 release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json) checked on 2026-10-13. Runtime packages/notices are downloaded into the project's `work/nuget` directory. The distribution startup check points `DOTNET_ROOT` at a nonexistent test directory and disables multilevel lookup to verify the bundled runtime.

Building from source still requires a .NET 10 SDK. Do not overwrite a running executable: close only the server belonging to this workspace before rebuilding `artifacts/app`. Task temporary files, browser profiles, caches and recovery versions remain in the project workspace. Extracted game/mod artwork, models and PBF/DEM caches are excluded from the distribution. Documentation screenshots show the editor interface; they are not reusable game-resource atlases.
