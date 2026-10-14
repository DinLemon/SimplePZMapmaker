param([string]$GamePath=$env:PZMAP_GAME_PATH)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/WorkspaceEnv.ps1"
if(-not $GamePath){$GamePath=$env:PZMAP_GAME_PATH}
$probe=Join-Path $env:PZMAP_WORKSPACE 'work/gameplay-inspect'
$classes=Join-Path $probe 'classes'
New-Item -ItemType Directory -Path $classes -Force | Out-Null
$java=Join-Path $GamePath 'jre64/bin/java.exe'
$jar=Join-Path $GamePath 'projectzomboid.jar'
$compiler=Join-Path $env:PZMAP_WORKSPACE 'work/game-smoke/ecj.jar'
if((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne '14C46DDC2E511C46C998DA728F6A80F13A689C920F3832402F7AD150F6A6188D'){throw 'ECJ checksum mismatch.'}
$options=@('--enable-native-access=ALL-UNNAMED','-Djava.awt.headless=true',('-XX:ErrorFile='+$probe+'/hs_err_%p.log'),'-XX:-CreateCoredumpOnCrash',('-Duser.home='+$probe),('-Djava.io.tmpdir='+$env:TEMP))
& $java @options -jar $compiler -25 -cp $jar -d $classes (Join-Path $PSScriptRoot 'GameplayGameProbe.java')
if($LASTEXITCODE -ne 0){throw 'Native gameplay API probe compile failed.'}
$files=@(Get-ChildItem -LiteralPath $probe -File | Where-Object {$_.Name -match '^SPZM.*\.(lua|txt)$'} | ForEach-Object {$_.FullName})
& $java @options -cp ($classes+';'+$jar) GameplayGameProbe @files
if($LASTEXITCODE -ne 0){throw 'Installed native gameplay verification failed.'}
