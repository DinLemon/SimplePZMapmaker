param([string]$ArchiveName='SimplePZMapmaker-0.13.0-win-x64.zip')
$ErrorActionPreference='Stop'
if([IO.Path]::GetFileName($ArchiveName) -ne $ArchiveName -or -not $ArchiveName.EndsWith('.zip')){throw 'Choose a ZIP filename only.'}
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
$stage=Join-Path $taskRoot ('work\packages\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage,(Join-Path $stage 'artifacts'),(Join-Path $stage 'scripts') -Force | Out-Null
$published=Join-Path $taskRoot 'artifacts\app'
if(-not(Test-Path -LiteralPath (Join-Path $published 'SimplePZMapmaker.Web.exe'))-or-not(Test-Path -LiteralPath (Join-Path $published 'simplepzmap.exe'))){throw 'Run scripts/publish.ps1 before packaging.'}
Copy-Item -LiteralPath $published -Destination (Join-Path $stage 'artifacts\app') -Recurse
foreach($name in @('Start.cmd','README.md','LICENSE')){Copy-Item -LiteralPath (Join-Path $taskRoot $name) -Destination $stage}
foreach($name in @('Start.ps1','WorkspaceEnv.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage 'scripts')}
Copy-Item -LiteralPath (Join-Path $taskRoot 'examples') -Destination $stage -Recurse
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
# Keep README-linked guides, branding and notices in the portable download.
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs') -Force | Where-Object Name -ne 'superpowers' | ForEach-Object {
 Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage 'docs') -Recurse -Force
}
$archive=Join-Path $taskRoot ('artifacts\'+$ArchiveName)
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal -Force
Write-Host "Package: $archive"
Write-Host 'Includes the .NET runtime; extract and run Start.cmd.'
