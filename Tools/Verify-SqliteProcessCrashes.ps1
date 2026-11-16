param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe=(Resolve-Path -LiteralPath $Executable).Path
$identity=[IO.File]::ReadAllText((Join-Path ([IO.Path]::GetDirectoryName($exe)) 'catquest-build.json'))|ConvertFrom-Json
if(!$identity.development -or $identity.productName -notmatch '^CatQuest SQLite Verification( |$)'){throw 'Requires the isolated SQLite Verification Development player.'}
$folder=Join-Path $workspace ('Temp/SQLiteMigrationVerification/process-crashes-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
if(Test-Path -LiteralPath $folder){throw 'Crash verification requires a new output directory.'}
[IO.Directory]::CreateDirectory($folder)|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$owned=[Collections.Generic.List[object]]::new()
$checks=[Collections.Generic.List[string]]::new()
$watch=[Diagnostics.Stopwatch]::StartNew()
$failure=$null
function Json($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 20),$utf8)}
function ReadJson($path){try{return [IO.File]::ReadAllText($path)|ConvertFrom-Json}catch{return $null}}
function Check($value,$message){if(!$value){throw $message};$checks.Add($message);[IO.File]::AppendAllText((Join-Path $folder 'checks.txt'),$message+"`n",$utf8);Write-Host ('PASS: '+$message)}
function Bound {if($watch.Elapsed.TotalMinutes -gt 12){throw 'Process crash verification exceeded12 minutes.'}}
function Launch($directory,$stage,$runId,$mode){
 $arguments=@('--focus-smoke','--sqlite-crash','--smoke-dir',('"'+$directory+'"'),'--crash-stage',$stage,'--crash-run-id',$runId,'-logFile',('"'+(Join-Path $directory ($mode+'.log'))+'"'))
 if($mode -eq 'verify-retry'){$arguments+='--crash-verify'}
 if($mode -eq 'final-read'){$arguments+='--crash-final-read'}
 $process=Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
 # Retain the owned process handle and creation time, never select by app name.
 $null=$process.Handle
 $record=[pscustomobject]@{Process=$process;StartTime=$process.StartTime;Id=$process.Id}
 $owned.Add($record)
 return $record
}
function StopOwned($record){
 $process=$record.Process
 if($process.HasExited){return}
 $current=Get-Process -Id $record.Id -ErrorAction Stop
 if($current.StartTime -ne $record.StartTime){throw 'Owned PID identity changed; refusing termination.'}
 $process.Kill()
 if(!$process.WaitForExit(10000)){throw 'Owned test process failed to terminate.'}
}
function WaitPaused($record,$directory,$stage,$runId){
 $until=[DateTime]::UtcNow.AddSeconds(90)
 do {
  Bound
  if($record.Process.HasExited){throw "Arm process exited before pause: $($record.Process.ExitCode)"}
  $marker=ReadJson (Join-Path $directory 'worker-paused.json')
  if($marker){
   Check ($marker.pid -eq $record.Id -and $marker.runId -eq $runId -and $marker.stage -eq $stage) ('Worker pause belongs to the owned process: '+$stage)
   Check ($marker.publishedNotifications -eq 0) ('Commit result has not been published to presentation: '+$stage)
   return
  }
  $errorResult=ReadJson (Join-Path $directory 'arm-result.json')
  if($errorResult){throw ('Arming failed: '+$errorResult.error)}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 throw ('Worker pause timed out: '+$stage)
}
function WaitResult($record,$directory,$phase,$stage,$runId){
 $until=[DateTime]::UtcNow.AddSeconds(90)
 do {
  Bound
  $result=ReadJson (Join-Path $directory ($phase+'-result.json'))
  if($result){
   Check ($result.pid -eq $record.Id -and $result.runId -eq $runId -and $result.stage -eq $stage -and $result.phase -eq $phase) ('Restart evidence matches owned process: '+$phase+' '+$stage)
   Check ($result.passed -eq $true -and $result.receipts -eq 1) ('Exact committed fields/wallet and one receipt: '+$phase+' '+$stage+' '+$result.error)
   Check ($record.Process.WaitForExit(15000) -and $record.Process.ExitCode -eq 0) ('Restart drained and exited successfully: '+$phase+' '+$stage)
   return $result
  }
  if($record.Process.HasExited){throw "Restart exited without evidence: $($record.Process.ExitCode)"}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $until)
 throw ('Restart verification timed out: '+$phase+' '+$stage)
}
try {
 foreach($stage in @('before-transaction','before-commit','after-commit')){
  Bound
  $directory=Join-Path $folder $stage
  [IO.Directory]::CreateDirectory($directory)|Out-Null
  $runId=[Guid]::NewGuid().ToString('N')
  [IO.File]::WriteAllText((Join-Path $directory 'sqlite-crash-authorized.txt'),$runId,$utf8)
  $armed=Launch $directory $stage $runId 'arm'
  WaitPaused $armed $directory $stage $runId
  StopOwned $armed
  Check ($armed.Process.HasExited) ('Forced termination at explicit worker boundary: '+$stage)
  $retry=Launch $directory $stage $runId 'verify-retry'
  $retryResult=WaitResult $retry $directory 'verify-retry' $stage $runId
  $reload=Launch $directory $stage $runId 'final-read'
  $reloadResult=WaitResult $reload $directory 'final-read' $stage $runId
  Check ($retryResult.receiptId -eq $reloadResult.receiptId -and $retryResult.fishCoins -eq $reloadResult.fishCoins -and $retryResult.experience -eq $reloadResult.experience -and $retryResult.tickets -eq $reloadResult.tickets) ('Retry receipt identity and exact wallet survive another real process restart: '+$stage)
 }
}catch{$failure=$_.Exception.Message;Write-Warning $failure}
finally {
 foreach($record in $owned){if(!$record.Process.HasExited){try{StopOwned $record}catch{if(!$failure){$failure=$_.Exception.Message}}}}
 Json (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=(!$failure);failure=$failure;checks=$checks;elapsedSeconds=$watch.Elapsed.TotalSeconds;executable=$exe;limits='Actual process termination at before-BEGIN, after domain writes/before-COMMIT, and after-COMMIT/before-result. Not an exact native COMMIT midpoint, physical disk fault, or hardware power-loss proof.'}
 Write-Host ('Evidence: '+$folder)
}
if($failure){throw $failure}
