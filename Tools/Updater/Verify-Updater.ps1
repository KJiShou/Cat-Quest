$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$folder = Join-Path $repo ('Library/UpdaterVerification/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($folder) | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /main:Verification "/out:$folder/Verify.exe" /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll (Join-Path $repo 'Assets/Scripts/Updates/UpdateManifest.cs') (Join-Path $PSScriptRoot 'Program.cs') (Join-Path $PSScriptRoot 'Verification.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& "$folder/Verify.exe" "$folder/cases"
if ($LASTEXITCODE -ne 0) { throw "Updater checks failed: $folder" }
