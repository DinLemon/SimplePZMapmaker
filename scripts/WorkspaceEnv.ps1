$taskRoot=Split-Path -Parent $PSScriptRoot
$env:PZMAP_WORKSPACE=$taskRoot
$env:TEMP=Join-Path $taskRoot 'work\tmp'
$env:TMP=$env:TEMP
$env:DOTNET_CLI_HOME=Join-Path $taskRoot 'work\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $taskRoot 'work\nuget'
$env:NUGET_HTTP_CACHE_PATH=Join-Path $taskRoot 'work\nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH=Join-Path $taskRoot 'work\nuget-plugins'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:npm_config_cache=Join-Path $taskRoot 'work\npm-cache'
$env:PLAYWRIGHT_BROWSERS_PATH=Join-Path $taskRoot 'work\browsers'
New-Item -ItemType Directory -Path $env:TEMP,$env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES -Force | Out-Null
$localSettings=Join-Path $taskRoot 'work\local-settings.json'
if(Test-Path -LiteralPath $localSettings){
 $settings=Get-Content -LiteralPath $localSettings -Raw | ConvertFrom-Json
 if(-not $env:PZMAP_GAME_PATH -and $settings.gamePath){$env:PZMAP_GAME_PATH=$settings.gamePath}
 if(-not $env:PZMAP_WORKSHOP_ROOT -and $settings.workshopRoot){$env:PZMAP_WORKSHOP_ROOT=$settings.workshopRoot}
}
