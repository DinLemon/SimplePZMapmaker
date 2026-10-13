# Windows distribution0.7

The win-x64 package is self-contained: extract the whole archive and run Start.cmd. No SDK or separately installed ASP.NET Core Runtime is needed. Keep the bundled DLLs and licenses folder next to the application.

Publication pins RuntimeFrameworkVersion10.0.12, matching Microsoft's [.NET10 release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json) checked2026-10-13. Runtime packages and their notices are downloaded into the project's work/nuget directory. The test launcher sets DOTNET_ROOT to a nonexistent G: directory and disables multilevel lookup to verify the bundled runtime.

Building source still requires.NET10 SDK. Do not overwrite a running executable; close only the server belonging to this workspace before rebuilding artifacts/app. Work, temporary files, browser profiles, caches and recovery versions remain in the G: workspace. Extracted game/mod artwork, models and PBF/DEM caches are excluded from the distribution.
