param([string]$Version = '0.1.8')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'GeÃ§erli sÃ¼rÃ¼m girin.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $repoRoot ('dist/TEHYS-' + $Version + '-win-x64')
if (Test-Path -LiteralPath $packageRoot) { throw 'YayÄ±n klasÃ¶rÃ¼ zaten var; mevcut paketin Ã¼zerine yazÄ±lmadÄ±.' }
Push-Location $repoRoot
try {
    dotnet test TEHYS.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Testler baÅŸarÄ±sÄ±z; paket hazÄ±rlanmadÄ±.' }
    dotnet publish src/OzelEgitimHakedis.Wpf -c Release -r win-x64 --self-contained true -o $packageRoot /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=None /p:DebugSymbols=false /p:Version=$Version /p:AssemblyVersion="$Version.0" /p:FileVersion="$Version.0"
    if ($LASTEXITCODE -ne 0) { throw 'YayÄ±n derlemesi baÅŸarÄ±sÄ±z.' }

    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/TEHYS_PAYLASIM_KULLANIM.txt') -Destination (Join-Path $packageRoot 'KULLANIM.txt')
    & (Join-Path $PSScriptRoot 'Add-TehysNotices.ps1') -PackageDirectory $packageRoot
    $files = @(Get-ChildItem -LiteralPath $packageRoot -Force)
    if ($files.Count -ne 3 -or @($files | Where-Object { $_.Name -notin @('TEHYS.exe','KULLANIM.txt','Lisanslar') -or ($_.PSIsContainer -and $_.Name -ne 'Lisanslar') }).Count -gt 0) {
        throw 'DaÄŸÄ±tÄ±m klasÃ¶rÃ¼nde beklenmeyen dosya var; paylaÅŸÄ±m durduruldu.'
    }
    Get-Item -LiteralPath $packageRoot
} finally { Pop-Location }
