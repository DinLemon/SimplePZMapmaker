$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
Push-Location $taskRoot
try{
 dotnet run --project tests/SimplePZMapmaker.Tests
 if($LASTEXITCODE -ne 0){throw 'Core regression suite failed.'}
 node --test tests/*.test.mjs
 if($LASTEXITCODE -ne 0){throw 'Editor regression suite failed.'}
 dotnet build src/SimplePZMapmaker.Web -c Release --nologo
 if($LASTEXITCODE -ne 0){throw 'Application build failed.'}
 if($env:PZMAP_BROWSER_TESTS -eq '1'){
  node scripts/verify-browser.mjs
  if($LASTEXITCODE -ne 0){throw 'Browser checks failed.'}
  node scripts/verify-review.mjs
  if($LASTEXITCODE -ne 0){throw 'Browser cancellation checks failed.'}
  node scripts/verify-pilna.mjs
  if($LASTEXITCODE -ne 0){throw 'Pilna browser checks failed.'}
  node scripts/verify-building-workshop.mjs
  if($LASTEXITCODE -ne 0){throw 'Building workshop checks failed.'}
  node scripts/verify-building-review.mjs
  if($LASTEXITCODE -ne 0){throw 'Building review regressions failed.'}
  node scripts/verify-basements.mjs
  if($LASTEXITCODE -ne 0){throw 'Basement checks failed.'}
  node scripts/verify-assets.mjs
  if($LASTEXITCODE -ne 0){throw 'Installed resource/editor checks failed.'}
  node scripts/verify-appearance.mjs
  if($LASTEXITCODE -ne 0){throw 'Regional appearance/import UI checks failed.'}
  node scripts/verify-visual-workshop.mjs
  if($LASTEXITCODE -ne 0){throw 'Visual building workshop/preset checks failed.'}
  foreach($script in @('verify-world-api.mjs','verify-world-workshop.mjs','verify-environment-workshop.mjs','verify-static-models.mjs','verify-static-model-placement.mjs','verify-model-scale.mjs','verify-agent-session.mjs','verify-planning-ui.mjs','verify-import-quality.mjs','verify-building-usability.mjs','verify-native-preview.mjs')){
   node (Join-Path $PSScriptRoot $script)
   if($LASTEXITCODE -ne 0){throw ('World/mod/model/agent browser check failed: '+$script)}
  }
  dotnet run --project tools/McpSmoke
  if($LASTEXITCODE -ne 0){throw 'MCP official-client compatibility check failed.'}
 }
}finally{Pop-Location}
