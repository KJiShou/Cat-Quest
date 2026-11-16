param([Parameter(Mandatory=$true)][string]$Executable,[switch]$PauseForWindowReview)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe=[IO.Path]::GetFullPath($Executable)
$identity=[IO.File]::ReadAllText((Join-Path ([IO.Path]::GetDirectoryName($exe)) 'catquest-build.json')) | ConvertFrom-Json
if(!(Test-Path -LiteralPath $exe) -or !$identity.development -or $identity.productName -notmatch '^CatQuest SQLite Verification( |$)'){throw 'An isolated SQLite Verification Development build is required.'}
foreach($existing in @(Get-CimInstance Win32_Process -Filter "Name='CatQuest.exe'")){
 if($existing.ExecutablePath -and [string]::Equals([IO.Path]::GetFullPath($existing.ExecutablePath),$exe,[StringComparison]::OrdinalIgnoreCase)){throw 'This exact verification executable is already running.'}
}
$folder=Join-Path $workspace ('Temp/SQLiteMigrationVerification/native-flows-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
if(Test-Path -LiteralPath $folder){throw 'Verification output must be new.'}
[IO.Directory]::CreateDirectory($folder)|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$checks=[Collections.Generic.List[string]]::new()
$ownedChildren=[Collections.Generic.List[Diagnostics.Process]]::new()
$main=$null;$child=$null;$failure=$null;$run=0
$watch=[Diagnostics.Stopwatch]::StartNew()
$resultPath=Join-Path $folder 'smoke-result.json'
$viewPath=$null
function WriteJson($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 20),$utf8)}
function ReadJson($path){
 try{
  $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
  $reader=[IO.StreamReader]::new($stream)
  try{return $reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
 }catch{return $null}
}
function Check($condition,$message){if(!$condition){throw $message};$checks.Add($message);Write-Host "PASS: $message"}
function WaitFor($file,[scriptblock]$predicate,$name,[int]$seconds=20){
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 do{
  if($watch.Elapsed.TotalSeconds -gt 600){throw 'Native flow deadline exceeded.'}
  if($main -and $main.HasExited){throw "Own main player exited unexpectedly: $($main.ExitCode)"}
  $errorPath=Join-Path $folder 'smoke-error.txt'
  if(Test-Path -LiteralPath $errorPath){throw [IO.File]::ReadAllText($errorPath)}
  $value=ReadJson $file
  if($value -and (& $predicate $value)){return $value}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 throw "Timed out: $name"
}
function QueueCommand($value){
 $path=Join-Path $folder 'smoke-command.json'
 if(Test-Path -LiteralPath $path){throw 'Previous command still pending.'}
 $value.requestId=[Guid]::NewGuid().ToString('N')
 WriteJson ($path+'.tmp') $value;[IO.File]::Move($path+'.tmp',$path)
 return $value.requestId
}
function Command($value){
 $request=QueueCommand $value
 return WaitFor $resultPath {param($v)$v.requestId -eq $request} ('main acknowledgement '+$value.action)
}
function Phase($expected){return WaitFor $resultPath {param($v)$v.phase -eq $expected} ('phase '+$expected)}
function Wallet($state){return "$($state.exp)/$($state.coins)/$($state.tickets)"}
function CaptureMain($name){
 $state=Command @{action='screenshot';screenshot=($name+'.png')}
 $path=Join-Path $folder ($name+'.png');$until=[DateTime]::UtcNow.AddSeconds(10)
 while((!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 100}
 if(!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0){throw "Screenshot command did not complete: $name"}
 return $state
}
function HostCommand($state,$action,$owner=$state.ownerId,$session=$state.sessionId,$request=[Guid]::NewGuid().ToString('N')){
 $path=Join-Path $state.companionDirectory 'pipe-command.request'
 if(Test-Path -LiteralPath $path){throw 'Previous Companion command pending.'}
 $before=(ReadJson $viewPath).ackCount
 WriteJson ($path+'.tmp') @{ownerId=$owner;sessionId=$session;requestId=$request;action=$action};[IO.File]::Move($path+'.tmp',$path)
 return WaitFor $viewPath {param($v)$v.lastAck -eq $request -and $v.ackCount -gt $before} ('real pipe acknowledgement '+$action)
}
function ConnectCompanion{
 $state=Command @{action='widget'}
 if([string]::IsNullOrWhiteSpace($state.companionDirectory) -or [IO.Path]::GetFileName($state.companionDirectory) -ne $state.ownerId){throw 'Invalid own Companion identity.'}
 $script:viewPath=Join-Path $state.companionDirectory 'companion-window.json'
 $view=WaitFor $viewPath {param($v)$v.connected -and $v.noPlayerStore} 'actual Companion connected without player store' 45
 $children=@(Get-CimInstance Win32_Process -Filter "Name='CatQuest.exe'" | Where-Object {$_.ParentProcessId -eq $main.Id -and $_.ExecutablePath -eq $exe})
 Check ($children.Count -eq 1) 'Exactly one own Companion process'
 $script:child=Get-Process -Id $children[0].ProcessId
 Check ($child.StartTime.ToUniversalTime() -ge $main.StartTime.ToUniversalTime()) 'Companion belongs to this test launch'
 $ownedChildren.Add($child)
 return $state
}
function StartOwnPlayer{
 $script:run++
 foreach($file in @('smoke-result.json','smoke-error.txt','smoke-command.json')){
  $path=Join-Path $folder $file;if(Test-Path -LiteralPath $path){Move-Item -LiteralPath $path -Destination ($path+'.run'+($run-1))}
 }
 $arguments=@('--focus-smoke','--smoke-dir',('"'+$folder+'"'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+(Join-Path $folder ("player-$run.log"))+'"'))
 # Visible interactive Player under test; no foreground, input or native window automation.
 $script:main=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Normal -PassThru
 return WaitFor $resultPath {param($v)$v.controlsBound -and $v.phase -eq 'Ready'} 'isolated SQLite startup' 60
}
function StopOwnPlayer{
 $null=QueueCommand @{action='quit'}
 if(!$main.WaitForExit(15000)){throw 'Own main player did not drain storage and close.'}
 Check ($main.ExitCode -eq 0) 'Own main player closed cleanly'
 foreach($process in $ownedChildren){if(!$process.HasExited -and !$process.WaitForExit(10000)){throw 'Own Companion did not close with main process.'}}
 $main.Dispose();$script:main=$null
}
try{
 WriteJson (Join-Path $folder 'context.json') @{utc=[DateTime]::UtcNow.ToString('O');executable=$exe;sha256=(Get-FileHash -LiteralPath $exe).Hash;productName=$identity.productName;isolated=$true}
 WriteJson (Join-Path $folder 'test-save.json') @{version=5;username='SQLite Flow Test';experience=100;tutorial=@{version=1;dismissed=$true;taskStep=4};settings=@{notificationsEnabled=$false;soundVolume=0;musicVolume=0}}
 $legacyHash=(Get-FileHash -LiteralPath (Join-Path $folder 'test-save.json')).Hash
 $state=StartOwnPlayer
 Check (Test-Path -LiteralPath (Join-Path $folder 'test-save.sqlite3')) 'Legacy fixture migrated to SQLite'
 $state=ConnectCompanion
 if($PauseForWindowReview){
  WriteJson (Join-Path $folder 'window-review.json') @{mainPid=$main.Id;companionPid=$child.Id;companionDirectory=$state.companionDirectory;executable=$exe;utc=[DateTime]::UtcNow.ToString('O')}
  Write-Output ('Window review ready: '+$folder)
  $reviewDeadline=[DateTime]::UtcNow.AddMinutes(5)
  $resume=Join-Path $folder 'window-review-complete.request'
  while(!(Test-Path -LiteralPath $resume)){
   if($main.HasExited -or $child.HasExited){throw 'Owned process exited during window review.'}
   if([DateTime]::UtcNow -gt $reviewDeadline){throw 'Window review exceeded five minutes.'}
   Start-Sleep -Milliseconds 250
  }
  $state=Command @{action='widget'}
 }
 foreach($mode in @(0,1,2)){
  $state=Command @{action='configure';mode=$mode;focusMinutes=25;restMinutes=1}
  $state=Command @{action='start'};$state=Phase 'Focusing'
  $state=Command @{action='advance';seconds=300}
  $before=Wallet $state
  $ack=HostCommand $state 'cancel'
  Check $ack.lastAccepted "Mode $mode actual pipe Cancel accepted"
  $state=Phase 'Interrupted';Check ((Wallet $state) -eq $before) "Mode $mode Cancel grants no rewards"
  $state=Command @{action='start'};$state=Phase 'Focusing'
  $state=Command @{action='advance';seconds=300};$priorExp=$state.exp
  $state=Command @{action='end'}
  Check ($state.cancelPrompt -and $state.phase -eq 'Focusing') "Mode $mode End Early awaits confirmation"
  $state=Command @{action='keep-focus'};Check (!$state.cancelPrompt -and $state.phase -eq 'Focusing') "Mode $mode Keep Focus preserves session"
  $state=Command @{action='end'};$state=Command @{action='confirm-cancel'}
  $expected=if($mode -eq 0){'Resting'}else{'Finished'}
  $state=Phase $expected;Check ($state.exp -gt $priorExp) "Mode $mode End Early commits earned rewards before $expected"
  $state=CaptureMain ("mode-$mode-ended")
  if($mode -eq 0){$state=Command @{action='rest'};$state=Phase 'Finished'}
 }
 foreach($mode in @(0,2)){
  $state=Command @{action='configure';mode=$mode;focusMinutes=1;restMinutes=1}
  $state=Command @{action='start'};$state=Phase 'Focusing';$state=Command @{action='advance';seconds=60}
  $expected=if($mode -eq 0){'Resting'}else{'Finished'};$state=Phase $expected
  Check ($state.phase -eq $expected) "Mode $mode natural completion settles"
  if($mode -eq 0){$before=Wallet $state;$state=Command @{action='advance';seconds=60};$state=Phase 'Finished';Check ((Wallet $state) -eq $before) 'Natural Rest completion grants no rewards'}
 }
 $state=Command @{action='configure';mode=1};$state=Command @{action='start'};$state=Phase 'Focusing'
 $state=Command @{action='advance';seconds=60};$before=Wallet $state
 $ack=HostCommand $state 'end' 'wrong-owner'
 Check (!$ack.lastAccepted) 'Actual Companion pipe rejects wrong owner'
 $ack=HostCommand $state 'end' $state.ownerId 'stale-session'
 Check (!$ack.lastAccepted) 'Actual Companion pipe rejects stale session'
 $state=CaptureMain 'identity-rejections';Check ($state.phase -eq 'Focusing' -and (Wallet $state) -eq $before) 'Rejected identities leave active session and wallet unchanged'
 $request=[Guid]::NewGuid().ToString('N');$session=$state.sessionId
 $ack=HostCommand $state 'end' $state.ownerId $session $request;Check $ack.lastAccepted 'Valid real pipe End Early accepted'
 $state=Phase 'Finished';$state=CaptureMain 'pipe-end';$before=Wallet $state
 $ack=HostCommand $state 'end' $state.ownerId $session $request;Check $ack.lastAccepted 'Exact duplicate acknowledges committed result'
 $ack=HostCommand $state 'cancel' $state.ownerId $session $request;Check (!$ack.lastAccepted) 'Changed duplicate payload rejected'
 $state=CaptureMain 'duplicate-result';Check ((Wallet $state) -eq $before) 'Duplicate commands leave wallet unchanged'
 $captureTime=[DateTime]::UtcNow;[IO.File]::WriteAllText((Join-Path $state.companionDirectory 'capture.request'),'capture',$utf8)
 $capturePath=Join-Path $state.companionDirectory 'companion-capture.png';$captureEnd=[DateTime]::UtcNow.AddSeconds(10)
 while((!(Test-Path -LiteralPath $capturePath) -or (Get-Item -LiteralPath $capturePath).LastWriteTimeUtc -lt $captureTime) -and [DateTime]::UtcNow -lt $captureEnd){Start-Sleep -Milliseconds 100}
 Check ((Test-Path -LiteralPath $capturePath) -and (Get-Item -LiteralPath $capturePath).LastWriteTimeUtc -ge $captureTime) 'Companion screenshot command completed'
 Copy-Item -LiteralPath $capturePath -Destination (Join-Path $folder 'companion.png')
 $oldChild=$child.Id;$state=Command @{action='shutdown-widget'}
 Check ($child.WaitForExit(10000)) 'Actual Companion clean shutdown'
 $state=Command @{action='resume-widget'};$state=ConnectCompanion
 Check ($child.Id -ne $oldChild) 'Actual Companion restarts and reconnects'
 $before=Wallet $state;$owner=$state.ownerId
 WriteJson (Join-Path $folder 'before-restart.json') $state
 StopOwnPlayer
 $state=StartOwnPlayer
 Check ((Wallet $state) -eq $before -and $state.ownerId -ne $owner) 'Real main-process restart restores SQLite rewards and creates a new owner'
 Check ((Get-FileHash -LiteralPath (Join-Path $folder 'test-save.json')).Hash -eq $legacyHash) 'Migrated legacy JSON remains unchanged'
 $state=ConnectCompanion;$state=CaptureMain 'after-restart'
 WriteJson (Join-Path $folder 'after-restart.json') $state
 StopOwnPlayer
}catch{$failure=$_.ToString();Write-Warning $failure}
finally{
 if($main){if(!$main.HasExited){$main.Kill();$null=$main.WaitForExit(5000)};$main.Dispose()}
 foreach($process in $ownedChildren){try{if(!$process.HasExited){$process.Kill();$null=$process.WaitForExit(5000)}}finally{$process.Dispose()}}
 WriteJson (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=($null -eq $failure);checks=$checks;failure=$failure;livePlayerPreferencesIsolatedByProductName=$true;notVerified=@('Visual correctness and motion require screenshot review and observed interaction.','Native minimize/resize/topmost and hardware power failure are outside this semantic flow.');evidence=$folder}
 Write-Output "Evidence: $folder"
}
if($failure){exit 1}
