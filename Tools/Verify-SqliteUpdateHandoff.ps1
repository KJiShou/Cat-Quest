param(
 [Parameter(Mandatory=$true)][string]$Executable,
 [Parameter(Mandatory=$true)][string]$LegacyFixture,
 [string]$LegacyEvidence
)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe=(Resolve-Path -LiteralPath $Executable).Path
$build=[IO.Path]::GetDirectoryName($exe)
$fixture=(Resolve-Path -LiteralPath $LegacyFixture).Path
$identity=[IO.File]::ReadAllText((Join-Path $build 'catquest-build.json'))|ConvertFrom-Json
if(!$identity.development -or $identity.productName -notmatch '^CatQuest SQLite Verification( |$)' -or $identity.version -ne '1.3.1') {throw 'Requires the isolated SQLite Verification Development player version 1.3.1.'}
$legacy=[IO.File]::ReadAllText($fixture)|ConvertFrom-Json
if($legacy.version -lt 1 -or $legacy.version -gt 6 -or !$legacy.username -or @($legacy.tasks).Count -eq 0 -or $legacy.settings.notificationsEnabled -ne $false) {throw 'Supply a legacy TaskService-generated fixture with tasks, a name and disabled notifications.'}
if(@($legacy.focusSessions|Where-Object {$_.status -eq 0}).Count -gt 0 -or @($legacy.battles|Where-Object {$_.outcome -eq 0}).Count -gt 0) {throw 'The legacy fixture must have no running Focus or battle.'}
if($LegacyEvidence){$LegacyEvidence=(Resolve-Path -LiteralPath $LegacyEvidence).Path}
foreach($link in @(Get-ChildItem -LiteralPath $build -Recurse -Force | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0})) {throw 'Build inputs must not contain reparse points.'}
$folder=Join-Path $workspace ('Temp/SQLiteMigrationVerification/update-handoff-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
if(Test-Path -LiteralPath $folder){throw 'Verification requires a fresh directory.'}
$installed=Join-Path $folder 'installed old then new'
$release=Join-Path $folder 'release'
$work=Join-Path $folder 'updater work'
$save=Join-Path $folder 'isolated player save'
$published=Join-Path $folder 'package'
foreach($directory in @($folder,$installed,$release,$work,$save)){[IO.Directory]::CreateDirectory($directory)|Out-Null}
$utf8=[Text.UTF8Encoding]::new($false)
$checks=[Collections.Generic.List[string]]::new()
$owned=[Collections.Generic.List[Diagnostics.Process]]::new()
$main=$null;$parent=$null;$helper=$null;$failure=$null
$watch=[Diagnostics.Stopwatch]::StartNew()
$statePath=Join-Path $save 'smoke-result.json'
$installedExe=Join-Path $installed 'CatQuest.exe'
function Json($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 30),$utf8)}
function ReadJson($path){try{return [IO.File]::ReadAllText($path)|ConvertFrom-Json}catch{return $null}}
function Check($condition,$message){if(!$condition){throw $message};$checks.Add($message);Write-Host ('PASS: '+$message)}
function WaitFor([scriptblock]$condition,$name,[int]$seconds=60){
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 do{
  if($watch.Elapsed.TotalSeconds -gt 600){throw 'Overall handoff verification deadline exceeded.'}
  if(Test-Path -LiteralPath (Join-Path $save 'smoke-error.txt')){throw [IO.File]::ReadAllText((Join-Path $save 'smoke-error.txt'))}
  $value=& $condition;if($value){return $value};Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 throw ('Timed out: '+$name)
}
function Command($command){
 $path=Join-Path $save 'smoke-command.json'
 if(Test-Path -LiteralPath $path){throw 'Previous control-file command still pending.'}
 $command.requestId=[Guid]::NewGuid().ToString('N');$request=$command.requestId
 Json ($path+'.tmp') $command;[IO.File]::Move($path+'.tmp',$path)
 if($command.action -eq 'quit'){return}
 return WaitFor { $state=ReadJson $statePath;if($state -and $state.requestId -eq $request){return $state} } ('command '+$command.action) 30
}
function RememberOwnChildren {
 $parents=@($owned|ForEach-Object {$_.Id})
 foreach($candidate in @(Get-CimInstance Win32_Process -Filter "Name='CatQuest.exe'")) {
  if($candidate.ExecutablePath -and [string]::Equals($candidate.ExecutablePath,$installedExe,[StringComparison]::OrdinalIgnoreCase) -and $candidate.ParentProcessId -in $parents -and $candidate.ProcessId -notin $parents) {
   $process=Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
   if($process){$null=$process.Handle;$owned.Add($process)}
  }
 }
}
function StopMain {
 RememberOwnChildren
 # Get-Process adopts an existing process; retain its native handle while it is
 # alive so ExitCode remains available after the updater-owned child exits.
 $null=$main.Handle
 $exitPid=$main.Id;$exitStarted=$main.StartTime.ToUniversalTime().ToString('O')
 $null=Command @{action='quit'}
 $exited=$main.WaitForExit(15000)
 $exitCode=$null;if($exited){$main.WaitForExit();$exitCode=$main.ExitCode}
 Json (Join-Path $folder ('process-exit-'+$exitPid+'.json')) @{utc=[DateTime]::UtcNow.ToString('O');pid=$exitPid;startedUtc=$exitStarted;exitedWithinWait=$exited;exitCode=$exitCode;exitCodeAvailable=($null -ne $exitCode)}
 Check $exited 'New Unity player drains storage and exits within the bounded wait'
 Check ($null -ne $exitCode -and $exitCode -eq 0) ('New Unity player exits successfully; PID='+$exitPid+' exitCode='+$exitCode)
 foreach($process in $owned){if(!$process.HasExited -and $process.Id -ne $main.Id -and !$process.WaitForExit(5000)){throw 'An owned child process did not close.'}}
}
function Wallet($state){return "$($state.exp)/$($state.coins)/$($state.tickets)"}
try {
 Json (Join-Path $folder 'context.json') @{utc=[DateTime]::UtcNow.ToString('O');newExecutable=$exe;newExecutableSha256=(Get-FileHash -LiteralPath $exe).Hash;productName=$identity.productName;legacyFixture=$fixture;legacyFixtureSha256=(Get-FileHash -LiteralPath $fixture).Hash;legacyEvidence=$LegacyEvidence;oldProcess='Disposable HandoffFixture process, not an old Unity application';newProcess='Actual isolated Unity Development player';networkPublish=$false}
 Copy-Item -LiteralPath $fixture -Destination (Join-Path $save 'test-save.json')
 $legacyHash=(Get-FileHash -LiteralPath (Join-Path $save 'test-save.json')).Hash
 foreach($entry in Get-ChildItem -LiteralPath $build -Force){Copy-Item -LiteralPath $entry.FullName -Destination $release -Recurse}
 & (Join-Path $PSScriptRoot 'Updater/Build-Updater.ps1') -OutputDirectory $release
 & (Join-Path $PSScriptRoot 'Updater/Build-Updater.ps1') -OutputDirectory $installed
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 & $compiler /nologo /target:winexe /main:HandoffFixture "/out:$installedExe" /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll (Join-Path $workspace 'Assets/Scripts/Updates/UpdateManifest.cs') (Join-Path $PSScriptRoot 'Updater/Program.cs') (Join-Path $PSScriptRoot 'Updater/HandoffFixture.cs')
 if($LASTEXITCODE -ne 0){throw 'Disposable parent fixture compilation failed.'}
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Updater/CatQuestUpdater.exe.config') -Destination ($installedExe+'.config')
 [IO.File]::WriteAllText((Join-Path $installed 'UnityPlayer.dll'),'Old installer inventory fixture; never loaded by the parent stub.',$utf8)
 Json (Join-Path $installed 'catquest-build.json') @{version='1.2.1';development=$true;productName='Disposable legacy updater parent fixture'}
 $oldFiles=@(foreach($name in @('CatQuest.exe','CatQuest.exe.config','CatQuestUpdater.exe','CatQuestUpdater.exe.config','UnityPlayer.dll','catquest-build.json')){$path=Join-Path $installed $name;@{path=$name;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();size=(Get-Item -LiteralPath $path).Length}})
 Json (Join-Path $installed 'catquest-package.json') @{format=1;sqlite=$false;version='1.2.1';files=$oldFiles}
 [IO.File]::WriteAllText((Join-Path $installed 'personal.txt'),'Preserve unrelated installation file.',$utf8)
 $notes=Join-Path $folder 'notes.txt';[IO.File]::WriteAllText($notes,'Isolated SQLite updater verification. Local package only; not published.',$utf8)
 & (Join-Path $PSScriptRoot 'Publish-CatQuestPackage.ps1') -BuildDirectory $release -OutputDirectory $published -NotesFile $notes -AllowDevelopmentBuild
 Copy-Item -LiteralPath (Join-Path $published 'CatQuest-1.3.1-win-x64.zip') -Destination (Join-Path $work 'package.zip')
 Copy-Item -LiteralPath (Join-Path $published 'catquest-update.json') -Destination (Join-Path $work 'release.json')
 Copy-Item -LiteralPath (Join-Path $release 'CatQuestUpdater.exe') -Destination (Join-Path $work 'CatQuestUpdater.exe')
 Copy-Item -LiteralPath (Join-Path $release 'CatQuestUpdater.exe.config') -Destination (Join-Path $work 'CatQuestUpdater.exe.config')
 $parent=Start-Process -FilePath $installedExe -ArgumentList @(('"'+$work+'"'),('"'+$save+'"')) -WorkingDirectory $installed -WindowStyle Hidden -PassThru;$null=$parent.Handle;$owned.Add($parent)
 $helperPid=WaitFor {if(Test-Path -LiteralPath (Join-Path $work 'fixture-failure.txt')){throw [IO.File]::ReadAllText((Join-Path $work 'fixture-failure.txt'))};if($parent.HasExited -and $parent.ExitCode -ne 0){throw ('Disposable parent exited '+$parent.ExitCode)};if(Test-Path -LiteralPath (Join-Path $work 'helper.pid')){return [int][IO.File]::ReadAllText((Join-Path $work 'helper.pid'))}} 'real updater helper PID' 15
 $helper=Get-Process -Id $helperPid;$null=$helper.Handle;$owned.Add($helper)
 Check ([string]::Equals($helper.MainModule.FileName,(Join-Path $work 'CatQuestUpdater.exe'),[StringComparison]::OrdinalIgnoreCase) -and $helper.StartTime.ToUniversalTime() -ge $parent.StartTime.ToUniversalTime()) 'Updater helper identity belongs to this disposable parent'
 $null=WaitFor {if(Test-Path -LiteralPath (Join-Path $work 'result.txt')){return [IO.File]::ReadAllText((Join-Path $work 'result.txt'))}} 'actual updater install result' 120
 Check ([IO.File]::ReadAllText((Join-Path $work 'result.txt')) -match '^Installed 1\.3\.1\.') 'Real updater installs the 1.3.1 package over the 1.2.1 non-SQLite inventory'
 $candidate=WaitFor {@(Get-CimInstance Win32_Process -Filter "Name='CatQuest.exe'"|Where-Object {$_.ExecutablePath -eq $installedExe -and $_.ParentProcessId -eq $helperPid})|Select-Object -First 1} 'actual updated Unity restart' 30
 $main=Get-Process -Id $candidate.ProcessId;$null=$main.Handle;$owned.Add($main)
 Check ($candidate.CommandLine.Contains('--focus-smoke') -and $candidate.CommandLine.Contains($save)) 'Updater restarts the real Unity player with only the isolated save path'
 $state=WaitFor {$v=ReadJson $statePath;if($v -and $v.controlsBound -and $v.phase -eq 'Ready'){return $v}} 'new Unity initialized SQLite' 90
 Check ($state.appVersion -eq '1.3.1' -and $state.developmentBuild) 'Restarted player reports the expected actual build version'
 Check ($state.exp -eq [int]$legacy.experience -and $state.coins -eq [int]$legacy.fishCoins -and $state.tickets -eq [int]$legacy.tickets) 'Migration preserves all three legacy balances before the new operation'
 Check ((Test-Path -LiteralPath (Join-Path $save 'test-save.sqlite3')) -and (ReadJson (Join-Path $save 'test-save.sqlite3.migration.json')).phase -eq 'complete') 'New Unity migrates the supplied legacy JSON and marks SQLite complete'
 Check ((Get-FileHash -LiteralPath (Join-Path $save 'test-save.json')).Hash -eq $legacyHash) 'Update and migration retain the copied legacy JSON unchanged'
 Check (([IO.File]::ReadAllText((Join-Path $installed 'personal.txt'))) -eq 'Preserve unrelated installation file.') 'Installer preserves unrelated installation content'
 Check ((Get-FileHash -LiteralPath $installedExe).Hash -eq (Get-FileHash -LiteralPath $exe).Hash) 'Installed executable matches the tested actual Unity build'
 $initialWallet=Wallet $state
 # Five minutes earns nonzero currency under the existing low-level rounding rules.
 $null=Command @{action='configure';mode=2;focusMinutes=5;restMinutes=1}
 $null=Command @{action='start'}
 $null=WaitFor {$v=ReadJson $statePath;if($v.phase -eq 'Focusing'){return $v}} 'new session persisted' 30
 $null=Command @{action='advance';seconds=300}
 $settled=WaitFor {$v=ReadJson $statePath;if($v.phase -eq 'Finished'){return $v}} 'new session settlement committed' 30
 Check ($settled.exp -gt $state.exp) 'Updated player creates and settles a new rewarded Focus session in SQLite'
 $settledWallet=Wallet $settled
 Json (Join-Path $folder 'settled-state.json') $settled
 StopMain
 [IO.File]::WriteAllText((Join-Path $save 'test-save.json'),'Deliberately invalid legacy JSON after successful SQLite migration.',$utf8)
 foreach($name in @('smoke-result.json','smoke-command.json')){$path=Join-Path $save $name;if(Test-Path -LiteralPath $path){Move-Item -LiteralPath $path -Destination ($path+'.before-restart')}}
 $main=Start-Process -FilePath $installedExe -ArgumentList @('--focus-smoke','--smoke-dir',('"'+$save+'"'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+(Join-Path $save 'SecondPlayer.log')+'"')) -WorkingDirectory $installed -WindowStyle Hidden -PassThru;$null=$main.Handle;$owned.Add($main)
 $restarted=WaitFor {$v=ReadJson $statePath;if($v -and $v.controlsBound -and $v.phase -eq 'Ready'){return $v}} 'second actual Unity startup ignores invalid old JSON' 90
 Check ((Wallet $restarted) -eq $settledWallet) 'Actual process restart reads committed SQLite balances without rollback or duplicate rewards'
 $null=Command @{action='reduce-motion';enabled=(-not $restarted.reduceMotion)}
 StopMain
 Check (([IO.File]::ReadAllText((Join-Path $save 'test-save.json'))) -like 'Deliberately invalid*') 'Post-restart command continues using SQLite without JSON dual writes'
 Json (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=$true;checks=$checks;initialWallet=$initialWallet;settledWallet=$settledWallet;limitations='Old parent is a disposable process fixture, not an old Unity build. Actual updater and actual new Unity migration, settlement and restart were exercised. No hardware power-loss claim.'}
 Write-Output ('PASS '+$folder)
} catch {
 $failure=$_
 Json (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=$false;checks=$checks;failure=$_.ToString();limitations='Old parent is a disposable process fixture, not an old Unity build.'}
 throw
} finally {
 RememberOwnChildren
 foreach($process in $owned){try{if(!$process.HasExited){$process.Kill();$null=$process.WaitForExit(5000)}}catch{Write-Warning ('Owned process cleanup failed: '+$process.Id+' '+$_)}finally{$process.Dispose()}}
}
