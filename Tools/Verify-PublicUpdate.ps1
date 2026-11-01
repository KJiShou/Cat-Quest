param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$ExpectedVersion='1.2.1',
    [ValidateSet('Startup','Upgrade','Current')][string]$Mode='Upgrade'
)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$folder=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $folder){throw 'Use a fresh isolated evidence directory.'}
if(Get-Process CatQuest -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe}){throw 'This verification installation is already running.'}
[IO.Directory]::CreateDirectory($folder)|Out-Null
$utf8=New-Object Text.UTF8Encoding($false)
$checks=[Collections.Generic.List[string]]::new()
$registryPath='Software\DefaultCompany\Cat Quest';$backup=@();$existed=$false;$main=$null;$work=$null;$failure=$null
$statePath=Join-Path $folder 'smoke-result.json'
function Json($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 30),$utf8)}
function Read($path){for($attempt=0;$attempt -lt 8;$attempt++){try{$value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json;if($null -ne $value){return $value}}catch{};Start-Sleep -Milliseconds 25};return $null}
function Check($value,$message){if(!$value){throw $message};$checks.Add($message);Write-Output "PASS: $message"}
function Wait([scriptblock]$condition,[string]$label,[int]$seconds=60){$until=[DateTime]::UtcNow.AddSeconds($seconds);do{if(&$condition){return};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until);throw "Timed out: $label"}
function Command($value){$path=Join-Path $folder 'smoke-command.json';Json $path $value;Wait {!(Test-Path -LiteralPath $path)} 'command accepted';$consumed=[DateTime]::UtcNow;Wait {(Get-Item -LiteralPath $statePath).LastWriteTimeUtc -gt $consumed} 'fresh state';return Read $statePath}
try {
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    if($key){$existed=$true;try{foreach($name in $key.GetValueNames()){$backup+=@{name=$name;kind=$key.GetValueKind($name);value=$key.GetValue($name)}}}finally{$key.Dispose()}}
    Json (Join-Path $folder 'test-save.json') @{version=5;username='Public Update Verification';experience=3000;tickets=3;fishCoins=47;settings=@{notificationsEnabled=$false;soundVolume=0;musicVolume=0}}
    $arguments='--focus-smoke --smoke-dir "'+$folder+'" -force-d3d11 -logFile "'+$folder+'/Player.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720'
    $main=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Hidden -PassThru
    Wait {(Read $statePath).controlsBound} 'release player startup'
    $r=Read $statePath;$initial=$r.appVersion
    Check (!$r.developmentBuild) 'Actual non-development Windows player'
    Check ($r.exp -eq 3000 -and $r.coins -eq 47) 'Isolated test save loaded'
    $r=Command @{action='widget'};$view=Join-Path $r.companionDirectory 'companion-window.json'
    Wait {(Read $view).connected} 'real Companion connection'
    Check ((Read $view).noPlayerStore) 'Companion connected without opening player store'
    if($Mode -eq 'Startup') { Check ($r.appVersion -eq $ExpectedVersion) 'Expected release version starts' }
    else {
        $r=Command @{action='updates-check'}
        Wait {(Read $statePath).updateState -in @('Available','Current','Error')} 'public release check' 90
        $r=Read $statePath
        if($Mode -eq 'Current') {
            Check ($r.appVersion -eq $ExpectedVersion -and $r.updateState -eq 'Current' -and $r.updateMessage -eq 'You have the latest version.') 'Published version reports latest through public GitHub feed'
        } else {
            Check ($r.updateState -eq 'Available') ('Public GitHub feed reports a newer version: '+$r.updateMessage)
            $before=Read (Join-Path $folder 'test-save.json')
            $r=Command @{action='updates-download'}
            $deadline=[DateTime]::UtcNow.AddMinutes(15);$lastProgress=-1
            do {
                $r=Read $statePath
                $progress=[int]($r.updateProgress*100)
                if($progress -ge $lastProgress+10){Write-Output "Download: $progress%";$lastProgress=$progress}
                if($r.updateState -in @('Ready','Error')){break}
                Start-Sleep -Milliseconds 300
            }while([DateTime]::UtcNow -lt $deadline)
            Check ($r.updateState -eq 'Ready') ('Real Unity HTTPS download and SHA-256 verification: '+$r.updateMessage)
            $work=$r.updateWorkDirectory
            $downloadHash=(Get-FileHash -LiteralPath (Join-Path $work 'package.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
            Json (Join-Path $folder 'download.json') @{sha256=$downloadHash;bytes=(Get-Item -LiteralPath (Join-Path $work 'package.zip')).Length;source='UnityWebRequest via public GitHub Releases';authentication='none'}
            $r=Command @{action='updates-install'}
            Wait {$main.HasExited} 'old process exit' 45
            Wait {(Test-Path -LiteralPath (Join-Path $work 'result.txt'))} 'installer result' 180
            $installResult=Get-Content -LiteralPath (Join-Path $work 'result.txt') -Raw
            [IO.File]::WriteAllText((Join-Path $folder 'installer-result.txt'),$installResult)
            Check ($installResult -match ('^Installed '+[Regex]::Escape($ExpectedVersion))) 'Downloaded public release installed successfully'
            Wait {(Read $statePath).appVersion -eq $ExpectedVersion} 'new release startup' 90
            $after=Read (Join-Path $folder 'test-save.json')
            Check ($before.username -eq $after.username -and $before.experience -eq $after.experience -and $before.fishCoins -eq $after.fishCoins -and $before.tickets -eq $after.tickets -and $before.tasks.Count -eq $after.tasks.Count) 'Tasks and progression preserved across online upgrade'
            Check (!(Read $statePath).developmentBuild) 'Installed public release is also non-development'
            $r=Command @{action='updates-check'};Wait {(Read $statePath).updateState -in @('Current','Error')} 'post-update version check' 90
            Check ((Read $statePath).updateMessage -eq 'You have the latest version.') 'Upgraded player now reports latest'
            $r=Command @{action='widget'};$view=Join-Path $r.companionDirectory 'companion-window.json';Wait {(Read $view).connected} 'updated Companion'
            Check ((Read $statePath).widgetOpen) 'Companion reopens after public update'
        }
    }
} catch {$failure=$_.ToString();Write-Output "FAIL: $failure"}
finally {
    if($work){foreach($helper in @(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq (Join-Path $work 'CatQuestUpdater.exe')})){try{Stop-Process -Id $helper.ProcessId -ErrorAction Stop}catch{}}}
    foreach($owned in @(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq $exe})){try{Stop-Process -Id $owned.ProcessId -ErrorAction Stop}catch{}}
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
    try{foreach($name in $key.GetValueNames()){if($name -notin $backup.name){$key.DeleteValue($name,$false)}};foreach($v in $backup){$key.SetValue($v.name,$v.value,$v.kind)}}finally{$key.Dispose()}
    if(!$existed){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
    Json (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=($null -eq $failure);failure=$failure;checks=$checks;initialVersion=$initial;targetVersion=$ExpectedVersion;mode=$Mode;executable=$exe;isolatedSave=$true;restoredPlayerPrefs=$true}
    Write-Output "Evidence: $folder"
}
if($failure){exit 1}
