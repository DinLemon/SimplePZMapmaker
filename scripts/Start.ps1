param([switch]$NoBrowser)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
$env:PZMAP_WORKSPACE=$taskRoot
$url='http://127.0.0.1:48765'
$running=$false
try{$state=Invoke-RestMethod -Uri "$url/api/environment" -TimeoutSec 2}catch{$state=$null}
if($state -and $state.version){
 if([IO.Path]::GetFullPath($state.workspace).TrimEnd('\') -ne [IO.Path]::GetFullPath($taskRoot).TrimEnd('\')){throw 'Port 48765 is occupied by SimplePZMapmaker from another directory. Stop that instance first.'}
 if($state.version -ne '0.3.0'){throw 'A stale SimplePZMapmaker server is running. Stop SimplePZMapmaker.Web.exe and run Start.cmd again to load version 0.3.'}
 $running=$true
}
if(-not $running){
 $exe=Join-Path $taskRoot 'artifacts\app\SimplePZMapmaker.Web.exe'
 if(-not(Test-Path -LiteralPath $exe)){& (Join-Path $PSScriptRoot 'publish.ps1');if($LASTEXITCODE -ne 0){throw 'Publish failed.'}}
 $logs=Join-Path $taskRoot 'work\logs'
 New-Item -ItemType Directory -Path $logs -Force | Out-Null
 Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'server.log') -RedirectStandardError (Join-Path $logs 'server-errors.log') | Out-Null
 for($i=0;$i -lt 40;$i++){
  Start-Sleep -Milliseconds 250
  try{$state=Invoke-RestMethod -Uri "$url/api/environment" -TimeoutSec 1;if($state.version -eq '0.3.0'){$running=$true;break}}catch{}
 }
 if(-not $running){throw "Server did not start. See $logs"}
}
if(-not $NoBrowser){Start-Process $url}
