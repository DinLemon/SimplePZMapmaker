param([string]$Output='artifacts/app')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
Push-Location $taskRoot
try {
 dotnet publish src/SimplePZMapmaker.Web -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.12 -p:DebugType=None -p:DebugSymbols=false -o $Output --nologo
 if($LASTEXITCODE -ne 0){throw 'dotnet publish failed.'}
 # The headless command line (simplepzmap.exe) ships next to the editor with the same self-contained runtime.
 dotnet publish src/SimplePZMapmaker.Cli -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.12 -p:DebugType=None -p:DebugSymbols=false -o $Output --nologo
 if($LASTEXITCODE -ne 0){throw 'dotnet publish of the command line failed.'}
 $publishRoot=[IO.Path]::GetFullPath((Join-Path $taskRoot $Output))
 foreach($name in @('SimplePZMapmaker.Core.pdb','SimplePZMapmaker.Web.pdb','simplepzmap.pdb')){
  $symbols=Join-Path $publishRoot $name
  if(Test-Path -LiteralPath $symbols){Remove-Item -LiteralPath $symbols}
 }
 $licenseRoot=Join-Path $publishRoot 'licenses'
 New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
 $config=Get-Content -LiteralPath (Join-Path $publishRoot 'SimplePZMapmaker.Web.runtimeconfig.json') -Raw | ConvertFrom-Json
 foreach($framework in $config.runtimeOptions.includedFrameworks){
  $package=$framework.name.ToLowerInvariant()+'.runtime.win-x64'
  $packageRoot=Join-Path $env:NUGET_PACKAGES ($package+'/'+$framework.version)
  $notices=@(Get-ChildItem -LiteralPath $packageRoot -File | Where-Object Name -Match '^(LICENSE|THIRD[-_ ]?PARTY[-_ ]?NOTICES|NOTICE)(\.|$)')
  if(-not $notices.Count){throw ('Runtime license notices not found: '+$package)}
  foreach($notice in $notices){Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseRoot ($package+'-'+$notice.Name)) -Force}
 }
 Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/licenses/MCP-SDK-LICENSE.txt') -Destination $licenseRoot -Force
 Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/licenses/README.md') -Destination (Join-Path $licenseRoot 'NOTICES.md') -Force
 Write-Host "Self-contained Windows application: $Output\SimplePZMapmaker.Web.exe; command line: $Output\simplepzmap.exe"
}finally{Pop-Location}
