$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
Push-Location $taskRoot
try{
 dotnet run --project tests/SimplePZMapmaker.Tests
 if($LASTEXITCODE -ne 0){throw 'Core regression suite failed.'}
 node --test tests/editor.test.mjs
 if($LASTEXITCODE -ne 0){throw 'Editor regression suite failed.'}
 dotnet build src/SimplePZMapmaker.Web -c Release --nologo
 if($LASTEXITCODE -ne 0){throw 'Application build failed.'}
 if($env:PZMAP_BROWSER_TESTS -eq '1'){
  node scripts/verify-browser.mjs
  if($LASTEXITCODE -ne 0){throw 'Browser checks failed.'}
 }
}finally{Pop-Location}
