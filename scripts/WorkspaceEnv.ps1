$taskRoot=Split-Path -Parent $PSScriptRoot
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
