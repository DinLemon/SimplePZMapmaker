param([string]$MapArchive='examples/Pilna/Pilna-B42.zip',[string]$GamePath='./game-installation',[switch]$Basements)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
$probeRoot=Join-Path $taskRoot 'work\game-smoke'
$classes=Join-Path $probeRoot 'classes'
New-Item -ItemType Directory -Path $classes -Force | Out-Null
$compiler=Join-Path $probeRoot 'ecj.jar'
if(-not(Test-Path -LiteralPath $compiler)){Invoke-WebRequest -Uri 'https://repo.maven.apache.org/maven2/org/eclipse/jdt/ecj/3.44.0/ecj-3.44.0.jar' -OutFile $compiler}
if((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne '14C46DDC2E511C46C998DA728F6A80F13A689C920F3832402F7AD150F6A6188D'){throw 'ECJ compiler checksum mismatch.'}
$java=Join-Path $GamePath 'jre64\bin\java.exe'
$gameJar=Join-Path $GamePath 'projectzomboid.jar'
if(-not(Test-Path -LiteralPath $java) -or -not(Test-Path -LiteralPath $gameJar)){throw 'Installed game JRE/JAR not found.'}
$javaOptions=@('--enable-native-access=ALL-UNNAMED',('-Duser.home='+$probeRoot),('-Djava.io.tmpdir='+$env:TEMP),('-Djava.library.path='+$GamePath+'\natives;'+$GamePath+'\natives\win64;'+$GamePath))
& $java @javaOptions -jar $compiler -25 -cp $gameJar -d $classes (Join-Path $PSScriptRoot 'NativeGameProbe.java')
if($LASTEXITCODE -ne 0){throw 'Runtime probe compile failed.'}
$extracted=Join-Path $probeRoot ('mods\'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath (Join-Path $taskRoot $MapArchive) -DestinationPath $extracted
$mapFolder=(Get-ChildItem -LiteralPath $extracted -Recurse -File -Filter '*.lotheader' | Select-Object -First 1).Directory.FullName
if(-not $mapFolder){throw 'Archive contains no native map.'}
$probeOptions=if($Basements){@('--basements')}else{@()}
& $java @javaOptions -cp ($classes+';'+$gameJar) NativeGameProbe $mapFolder (Join-Path $probeRoot 'cache') $GamePath @probeOptions
if($LASTEXITCODE -ne 0){throw 'Installed game load/room/stair probe failed.'}
Write-Host 'Native game classes loaded headers, chunks, upper rooms and recognise staircase sprites. Character walking/visual playthrough is a separate check.'
