param([string]$MapArchive='examples/Pilna/Pilna-B42.zip',[string]$GamePath='',[switch]$Basements,[switch]$NoStairs,[ValidateRange(-32,-1)][int]$BasementMinLevel=-16,[string]$TileModArchive='',[switch]$Ramps,[switch]$FuelPumps)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
if([string]::IsNullOrWhiteSpace($GamePath)){$GamePath=$env:PZMAP_GAME_PATH}
if([string]::IsNullOrWhiteSpace($GamePath)){throw 'Specify -GamePath or set PZMAP_GAME_PATH to your Build 42 installation.'}
$GamePath=[IO.Path]::GetFullPath($GamePath)
if(-not(Test-Path -LiteralPath (Join-Path $GamePath 'jre64\bin\java.exe')) -or -not(Test-Path -LiteralPath (Join-Path $GamePath 'projectzomboid.jar'))){throw 'Installed game JRE/JAR not found at the supplied game path.'}
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
$archivePath=if([IO.Path]::IsPathRooted($MapArchive)){$MapArchive}else{Join-Path $taskRoot $MapArchive}
Expand-Archive -LiteralPath $archivePath -DestinationPath $extracted
$mapFolder=(Get-ChildItem -LiteralPath $extracted -Recurse -File -Filter '*.lotheader' | Select-Object -First 1).Directory.FullName
if(-not $mapFolder){throw 'Archive contains no native map.'}
$probeOptions=@(if($Basements){'--basements',$BasementMinLevel.ToString()}elseif($NoStairs){'no-stairs'})
if($Ramps){$probeOptions+='--ramps'}
if($FuelPumps){$probeOptions+='--fuel-pumps'}

if(-not[string]::IsNullOrWhiteSpace($TileModArchive)){
 $tileArchivePath=if([IO.Path]::IsPathRooted($TileModArchive)){$TileModArchive}else{Join-Path $taskRoot $TileModArchive}
 $tileExtract=Join-Path $probeRoot ('mods\tiles-'+[Guid]::NewGuid().ToString('N'))
 Expand-Archive -LiteralPath $tileArchivePath -DestinationPath $tileExtract
 $infos=@(Get-ChildItem -LiteralPath $tileExtract -Recurse -File -Filter mod.info | Where-Object {$_.Directory.Name -eq '42'})
 if($infos.Count -ne 1){throw 'Resource probe expects one Build 42 tile mod archive (42/mod.info).'}
 $registrations=@();$numbers=@{}
 foreach($line in [IO.File]::ReadAllLines($infos[0].FullName)){
  if($line -notmatch '^tiledef=([A-Za-z0-9_]+)\s+(\d{1,4})\s*$'){continue}
  $name=$Matches[1];$number=[int]$Matches[2]
  if($number -lt 100 -or $number -gt 8190 -or $numbers.ContainsKey($number)){throw 'Invalid or duplicate tile definition number in resource mod.'}
  $numbers[$number]=$true;$definition=Join-Path $infos[0].Directory.FullName ('media\'+$name+'.tiles')
  if(-not(Test-Path -LiteralPath $definition) -or (Get-Item -LiteralPath $definition).Length -gt 32MB){throw 'Resource tile definition missing or too large.'}
  $registrations+=($number.ToString()+"`t"+$definition)
 }
 if($registrations.Count -lt 1 -or $registrations.Count -gt 16){throw 'Resource probe needs 1-16 registered tile definitions.'}
 $manifest=Join-Path $tileExtract 'tiledefs.tsv';[IO.File]::WriteAllLines($manifest,$registrations,[Text.UTF8Encoding]::new($false))
 $probeOptions+=@('--tiledefs',$manifest)
}
Push-Location -LiteralPath $GamePath
try {
 & $java @javaOptions -cp ($classes+';'+$gameJar) NativeGameProbe $mapFolder (Join-Path $probeRoot 'cache') $GamePath @probeOptions
 if($LASTEXITCODE -ne 0){throw 'Installed game load/room/stair probe failed.'}
} finally {Pop-Location}
Write-Host 'Native game classes loaded headers, chunks, checked room levels and staircase sprite properties. Character walking/visual playthrough is a separate check.'
