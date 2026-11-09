# Publishes the single-file app and wraps it in an Inno Setup installer.
#
#   powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
#
# Output: artifacts\installer\Steam-Games-Manager-Setup-<version>.exe

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Sgm.App\Sgm.App.csproj'
$publish = Join-Path $root 'publish\app'
$iss     = Join-Path $root 'installer\installer.iss'

if (Test-Path $publish) {
    try {
        Remove-Item $publish -Recurse -Force
    }
    catch {
        throw "Could not clear $publish. Close a running SteamGamesManager.exe and try again. $($_.Exception.Message)"
    }
}

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}

if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php.'
}

& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

Write-Output ''
Write-Output "Installer written to $(Join-Path $root 'artifacts\installer')"
