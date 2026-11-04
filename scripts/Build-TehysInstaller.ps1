param(
    [string]$Version = '0.1.5',
    [Parameter(Mandatory)][string]$CompilerPath
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Geçerli sürüm girin.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$package = Join-Path $repoRoot ('dist/TEHYS-' + $Version + '-win-x64')
$installer = Join-Path $repoRoot ('dist/TEHYS-' + $Version + '-Setup-win-x64.exe')
if (Test-Path -LiteralPath $installer) { throw 'Mevcut kurulum paketinin üzerine yazılmadı.' }
if (-not (Test-Path -LiteralPath (Join-Path $package 'TEHYS.exe'))) { throw 'Önce temiz taşınabilir paketi hazırlayın.' }
$allowed = @('TEHYS.exe', 'KULLANIM.txt', 'Lisanslar/DOTNET-LICENSE.txt',
    'Lisanslar/DOTNET-THIRD-PARTY-NOTICES.txt', 'Lisanslar/PDFsharp-LICENSE.txt', 'Lisanslar/WPF-LICENSE.txt')
$actual = @(Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
    [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/')
})
if ($actual.Count -ne $allowed.Count -or @($actual | Where-Object { $_ -notin $allowed }).Count -gt 0) {
    throw 'Dağıtım klasöründe beklenmeyen dosya var; kurulum paketi hazırlanmadı.'
}
$executableVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'TEHYS.exe')).FileVersion
if ($executableVersion -ne ($Version + '.0')) { throw 'Program ve kurulum sürümü eşleşmiyor.' }
& $CompilerPath ('/DAppVersion=' + $Version) ('/DPackageDir=' + $package) (Join-Path $repoRoot 'installer/TEHYS.iss')
if ($LASTEXITCODE -ne 0) { throw 'Kurulum paketi oluşturulamadı.' }
$artifacts = @((Join-Path $repoRoot ('dist/TEHYS-' + $Version + '-win-x64.zip')), $installer)
$lines = foreach ($artifact in $artifacts) {
    $hash = Get-FileHash -LiteralPath $artifact -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $artifact)
}
$lines | Set-Content -LiteralPath (Join-Path $repoRoot ('dist/SHA256SUMS-' + $Version + '.txt')) -Encoding ascii
Get-Item -LiteralPath $installer
