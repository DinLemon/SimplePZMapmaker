param([string]$GamePath='./game-installation',[string]$ModelArchive='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WorkspaceEnv.ps1')
$probeRoot=Join-Path $taskRoot 'work\static-model-game-probe'
$classes=Join-Path $probeRoot 'classes'
New-Item -ItemType Directory -Path $classes -Force | Out-Null
$compiler=Join-Path $taskRoot 'work\game-smoke\ecj.jar'
if(-not(Test-Path -LiteralPath $compiler)){Invoke-WebRequest -Uri 'https://repo.maven.apache.org/maven2/org/eclipse/jdt/ecj/3.44.0/ecj-3.44.0.jar' -OutFile $compiler}
if((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne '14C46DDC2E511C46C998DA728F6A80F13A689C920F3832402F7AD150F6A6188D'){throw 'ECJ checksum mismatch.'}
$java=Join-Path $GamePath 'jre64\bin\java.exe'
$jar=Join-Path $GamePath 'projectzomboid.jar'
$options=@(('-Duser.home='+$probeRoot),('-Djava.io.tmpdir='+$env:TEMP))
& $java @options -jar $compiler -25 -cp $jar -d $classes (Join-Path $PSScriptRoot 'StaticModelGameProbe.java')
if($LASTEXITCODE -ne 0){throw 'Static model API probe compile failed.'}
$probeArgs=@()
if($ModelArchive){
 $archivePath=[IO.Path]::GetFullPath((Join-Path $taskRoot $ModelArchive))
 if(-not $archivePath.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Model archive must be inside the workspace.'}
 $archive=[IO.Compression.ZipFile]::OpenRead($archivePath)
 try{foreach($suffix in @('.lua','.txt')){
  $entries=@($archive.Entries | Where-Object {if($suffix -eq '.lua'){$_.FullName -match '/media/lua/server/SPZM[^/]+\.lua$'}else{$_.FullName -match '/media/scripts/SPZM[^/]+\.txt$'}})
  if($entries.Count -ne 1 -or $entries[0].Length -gt 32MB){throw 'Expected one bounded original model glue/proxy script.'}
  $target=Join-Path $probeRoot ('static-model-export'+$suffix)
  $reader=[IO.StreamReader]::new($entries[0].Open());try{[IO.File]::WriteAllText($target,$reader.ReadToEnd(),[Text.UTF8Encoding]::new($false))}finally{$reader.Dispose()}
  $probeArgs+=,$target
 }}finally{$archive.Dispose()}
}
& $java @options -cp ($classes+';'+$jar) StaticModelGameProbe @probeArgs
if($LASTEXITCODE -ne 0){throw 'Installed static model placement API verification failed.'}
