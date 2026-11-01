param(
    [string]$BuildDirectory = 'Builds/Windows',
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$NotesFile,
    [switch]$AllowDevelopmentBuild
)
$ErrorActionPreference = 'Stop'
$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.StartsWith($build.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $output -eq $build) { throw 'Output must be outside the build directory.' }
$identity = Get-Content -LiteralPath (Join-Path $build 'catquest-build.json') -Raw | ConvertFrom-Json
if ($identity.development -and !$AllowDevelopmentBuild) { throw 'Development builds cannot be published. Build a release player first.' }
$version = [string]$identity.version
if ($version -notmatch '^\d+\.\d+(\.\d+){0,2}$') { throw 'Use a numeric release version, for example 1.2.1.' }
$notes = Get-Content -LiteralPath $NotesFile -Raw
if ($notes.Length -gt 8000) { throw 'Release notes must be at most 8000 characters.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$zipName = "CatQuest-$version-win-x64.zip"
$zipPath = Join-Path $output $zipName
if (Test-Path -LiteralPath $zipPath) { throw 'This output package already exists. Use a fresh output directory.' }
$allowed = @('CatQuest.exe','CatQuestUpdater.exe','UnityPlayer.dll','UnityCrashHandler64.exe','CatQuest_Data','MonoBleedingEdge','D3D12','WinPixEventRuntime.dll','GameAssembly.dll','baselib.dll','catquest-build.json')
$files = @()
foreach ($name in $allowed) {
    $item = Join-Path $build $name
    if (!(Test-Path -LiteralPath $item)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $item -File -Recurse)) {
        if ($file.Extension -in @('.pdb','.mdb') -or $file.FullName -match 'DoNotShip') { continue }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Release files cannot be links.' }
        $relative = $file.FullName.Substring($build.TrimEnd('\').Length+1).Replace('\','/')
        $files += [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();size=$file.Length}
    }
}
foreach ($required in @('CatQuest.exe','CatQuestUpdater.exe','UnityPlayer.dll')) { if ($required -notin $files.path) { throw "Missing required release file: $required" } }
$inventory = [ordered]@{format=1;version=$version;files=@($files | Sort-Object path)}
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $build 'catquest-package.json'),($inventory | ConvertTo-Json -Depth 5),$utf8)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $inventory.files) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $build $file.path),$file.path,[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $build 'catquest-package.json'),'catquest-package.json') | Out-Null
} finally { $archive.Dispose() }
$manifest = [ordered]@{format=1;version=$version;notes=$notes;url="https://github.com/KJiShou/Cat-Quest/releases/download/v$version/$zipName";sha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant();size=(Get-Item -LiteralPath $zipPath).Length}
[IO.File]::WriteAllText((Join-Path $output 'catquest-update.json'),($manifest | ConvertTo-Json),$utf8)
Write-Output "Created $zipPath"
Write-Output "Upload this ZIP and catquest-update.json to the public GitHub release tagged v$version. This script does not publish anything."
