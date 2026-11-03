param([string]$Version = '0.1.4')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Geçerli sürüm girin.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $repoRoot ('dist/TEHYS-' + $Version + '-win-x64')
if (Test-Path -LiteralPath $packageRoot) { throw 'Yayın klasörü zaten var; mevcut paketin üzerine yazılmadı.' }
Push-Location $repoRoot
try {
    dotnet test OzelEgitimHakedisSistemi.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Testler başarısız; paket hazırlanmadı.' }
    dotnet publish src/OzelEgitimHakedis.Wpf -c Release -r win-x64 --self-contained true -o $packageRoot /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=None /p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Yayın derlemesi başarısız.' }
    Rename-Item -LiteralPath (Join-Path $packageRoot 'OEHS.exe') -NewName 'TEHYS.exe'
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/TEHYS_PAYLASIM_KULLANIM.txt') -Destination (Join-Path $packageRoot 'KULLANIM.txt')
    & (Join-Path $PSScriptRoot 'Add-TehysNotices.ps1') -PackageDirectory $packageRoot
    $files = @(Get-ChildItem -LiteralPath $packageRoot -Force)
    if ($files.Count -ne 3 -or @($files | Where-Object { $_.Name -notin @('TEHYS.exe','KULLANIM.txt','Lisanslar') -or ($_.PSIsContainer -and $_.Name -ne 'Lisanslar') }).Count -gt 0) {
        throw 'Dağıtım klasöründe beklenmeyen dosya var; paylaşım durduruldu.'
    }
    $zipPath = $packageRoot + '.zip'
    Compress-Archive -LiteralPath @((Join-Path $packageRoot 'TEHYS.exe'),(Join-Path $packageRoot 'KULLANIM.txt'),(Join-Path $packageRoot 'Lisanslar')) -DestinationPath $zipPath
    Get-Item -LiteralPath $zipPath
    Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
} finally { Pop-Location }
