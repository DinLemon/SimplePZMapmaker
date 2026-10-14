$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
$stage=Join-Path $taskRoot ('work\packages\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage,(Join-Path $stage 'artifacts'),(Join-Path $stage 'scripts') -Force | Out-Null
$published=Join-Path $taskRoot 'artifacts\app'
if(-not(Test-Path -LiteralPath (Join-Path $published 'SimplePZMapmaker.Web.exe'))){throw 'Run scripts/publish.ps1 before packaging.'}
Copy-Item -LiteralPath $published -Destination (Join-Path $stage 'artifacts\app') -Recurse
foreach($name in @('Start.cmd','README.md','LICENSE')){Copy-Item -LiteralPath (Join-Path $taskRoot $name) -Destination $stage}
foreach($name in @('Start.ps1','WorkspaceEnv.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage 'scripts')}
Copy-Item -LiteralPath (Join-Path $taskRoot 'examples') -Destination $stage -Recurse
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
foreach($name in @('STATUS.md','EDITOR-TOOLS.md','PUBLICATION-PRIVACY.md','PILNA-STATUS.md','PROVENANCE.md','PROGRESS.md','BUILDINGS-REVIEW.md','HOUSING-REFERENCES.md','MOD-RESOURCES.md','STATIC-MODELS.md','MCP.md','WORLD-AND-ENVIRONMENT.md','RUNTIME.md')){Copy-Item -LiteralPath (Join-Path $taskRoot ('docs\'+$name)) -Destination (Join-Path $stage 'docs')}
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\screenshots') -Destination (Join-Path $stage 'docs') -Recurse
$archive=Join-Path $taskRoot 'artifacts\SimplePZMapmaker-0.9.1-win-x64.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal -Force
Write-Host "Package: $archive"
Write-Host 'Includes the .NET runtime; extract and run Start.cmd.'
