param([int]$Port=48768,[switch]$SkipBrowser,[string]$ApplicationDirectory='artifacts\app')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
$qaRoot=Join-Path $taskRoot 'work\actions-smoke'
$exe=Join-Path ([IO.Path]::GetFullPath($ApplicationDirectory,$taskRoot)) 'SimplePZMapmaker.Web.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Publish the application before running the startup check.'}
New-Item -ItemType Directory -Path $qaRoot -Force | Out-Null
$saved=@{}
foreach($key in @('PZMAP_WORKSPACE','PZMAP_GAME_PATH','PZMAP_DISABLE_GAME_DISCOVERY','PZMAP_WORKSHOP_ROOT','PZMAP_LISTEN_URL','PZMAP_TEST_URL','DOTNET_ROOT','DOTNET_MULTILEVEL_LOOKUP')){$saved[$key]=[Environment]::GetEnvironmentVariable($key)}
$process=$null
try {
 $env:PZMAP_WORKSPACE=$qaRoot
 $env:PZMAP_GAME_PATH=Join-Path $qaRoot 'no-game-installed'
 $env:PZMAP_DISABLE_GAME_DISCOVERY='1'
 $env:PZMAP_WORKSHOP_ROOT=''
 $env:PZMAP_LISTEN_URL="http://127.0.0.1:$Port"
 $env:PZMAP_TEST_URL=$env:PZMAP_LISTEN_URL
 $env:DOTNET_ROOT=Join-Path $qaRoot 'no-dotnet-installed'
 $env:DOTNET_MULTILEVEL_LOOKUP='0'
 $process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $qaRoot 'server.log') -RedirectStandardError (Join-Path $qaRoot 'server-errors.log')
 $state=$null
 for($i=0;$i -lt 80;$i++) {
  if($process.HasExited){throw 'Published application exited before becoming ready.'}
  try{$state=Invoke-RestMethod -Uri ($env:PZMAP_TEST_URL+'/api/environment') -TimeoutSec 1;break}catch{Start-Sleep -Milliseconds 250}
 }
 if(-not $state -or $state.version -ne '0.12.0' -or -not $state.selfContained -or $state.gameFound -or [IO.Path]::GetFullPath($state.workspace) -ne [IO.Path]::GetFullPath($qaRoot)){throw 'Unexpected published application environment.'}
 $state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qaRoot 'environment.json')
 $map=Invoke-RestMethod -Uri ($env:PZMAP_TEST_URL+'/api/generate') -Method Post -ContentType application/json -Body '{"kind":"blank","size":256,"name":"Actions startup check"}'
 $json=$map|ConvertTo-Json -Depth 48 -Compress
 $validation=Invoke-RestMethod -Uri ($env:PZMAP_TEST_URL+'/api/validate') -Method Post -ContentType application/json -Body $json
 if(-not $validation.valid){throw 'Generated project did not validate.'}
 $archive=Join-Path $qaRoot 'actions-smoke-B42.zip'
 Invoke-WebRequest -Uri ($env:PZMAP_TEST_URL+'/api/export') -Method Post -ContentType application/json -Body $json -OutFile $archive | Out-Null
 node (Join-Path $PSScriptRoot 'verify-native-map.mjs') $archive
 if($LASTEXITCODE -ne 0){throw 'Published application exported an invalid native map.'}
 $nextMap=$json|ConvertFrom-Json -Depth 48
 $nextMap.worldX=$map.worldX+1
 $world=@{format='SimplePZWorld';version=1;id='11000000000000000000000000000011';name='Offline shared infrastructure';maps=@(@{id='11000000000000000000000000000012';project=$map},@{id='11000000000000000000000000000013';project=$nextMap});buildings=@()}
 $feature=@{id='startup-shared-bridge';kind='bridge';width=4;level=1;access='stairs';guardRails=$true;points=@(@{x=$map.worldX*256+240;y=$map.worldY*256+100},@{x=$map.worldX*256+280;y=$map.worldY*256+100})}
 $shared=Invoke-RestMethod -Uri ($env:PZMAP_TEST_URL+'/api/world/linear/put') -Method Post -ContentType application/json -Body (@{world=$world;feature=$feature}|ConvertTo-Json -Depth 48 -Compress)
 if($shared.world.linearFeatures.Count -ne 1){throw 'Offline shared bridge was lost.'}
 $sharedArchive=Join-Path $qaRoot 'offline-shared-bridge-B42.zip'
 Invoke-WebRequest -Uri ($env:PZMAP_TEST_URL+'/api/world/export') -Method Post -ContentType application/json -Body ($shared.world|ConvertTo-Json -Depth 48 -Compress) -OutFile $sharedArchive | Out-Null
 node (Join-Path $PSScriptRoot 'verify-native-map.mjs') $sharedArchive
 if($LASTEXITCODE -ne 0){throw 'Offline shared bridge native export failed.'}
 if(-not $SkipBrowser){
  node (Join-Path $PSScriptRoot 'verify-actions-browser.mjs')
  if($LASTEXITCODE -ne 0){throw 'Published application browser check failed.'}
 }
 Write-Output 'PASS published self-contained application starts without an installed game or shared .NET runtime, serves the editor and exports a native map.'
} finally {
 if($process -and -not $process.HasExited){Stop-Process -Id $process.Id; $process.WaitForExit()}
 foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key])}
}
