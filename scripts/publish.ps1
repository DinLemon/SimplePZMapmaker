$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
Push-Location $taskRoot
try {
 dotnet publish src/SimplePZMapmaker.Web -c Release --self-contained false -o artifacts/app --nologo
 if($LASTEXITCODE -ne 0){throw 'dotnet publish failed.'}
 Write-Host "Application: $taskRoot\artifacts\app\SimplePZMapmaker.Web.exe"
}finally{Pop-Location}
