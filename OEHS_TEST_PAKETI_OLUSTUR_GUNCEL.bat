@echo off
setlocal EnableExtensions
cd /d "%~dp0" || exit /b 1

set "ROOT=%CD%"
set "SOLUTION=%ROOT%\TEHYS.sln"
set "PROJECT=%ROOT%\src\OzelEgitimHakedis.Wpf\OzelEgitimHakedis.Wpf.csproj"
set "TEST_PROJECT=%ROOT%\tests\OzelEgitimHakedis.Tests\OzelEgitimHakedis.Tests.csproj"
set "DIST=%ROOT%\dist"
set "PUBLISH=%DIST%\TEHYS-Test-Paketi"
set "ZIP=%DIST%\TEHYS-Test-Paketi.zip"
set "ZIP_TMP=%DIST%\TEHYS-Test-Paketi.tmp.zip"

title TEHYS - Windows Test Paketi Olusturucu
echo.
echo ============================================================
echo   TEHYS - WINDOWS TEST PAKETI OLUSTUR
echo ============================================================
echo.

if not exist "%SOLUTION%" (
    echo [HATA] Cozum dosyasi bulunamadi:
    echo        %SOLUTION%
    echo        Bu BAT dosyasini depo kokunde calistirin.
    goto :fail
)
if not exist "%PROJECT%" (
    echo [HATA] WPF proje dosyasi bulunamadi: %PROJECT%
    goto :fail
)
if not exist "%TEST_PROJECT%" (
    echo [HATA] Test proje dosyasi bulunamadi: %TEST_PROJECT%
    goto :fail
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [HATA] .NET SDK bulunamadi. .NET 8 SDK kurulu olmali.
    goto :fail
)

echo [1/4] Kurulu .NET SDK:
dotnet --version
if errorlevel 1 goto :fail

echo.
echo [2/4] Cozum derleniyor ve testler calistiriliyor...
dotnet test "%SOLUTION%" --configuration Release
if errorlevel 1 (
    echo.
    echo [HATA] Derleme veya test basarisiz. Mevcut test ZIP dosyasi korunmustur.
    goto :fail
)

if not exist "%DIST%" mkdir "%DIST%"
if errorlevel 1 (
    echo [HATA] Cikti klasoru olusturulamadi.
    goto :fail
)

echo.
echo [3/4] Windows x64 self-contained yayin olusturuluyor...
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
if exist "%PUBLISH%" (
    echo [HATA] Onceki yayin klasoru temizlenemedi.
    echo Dosyalar bir program tarafindan kullaniliyor olabilir.
    goto :fail
)

dotnet publish "%PROJECT%" --configuration Release --runtime win-x64 --self-contained true --output "%PUBLISH%" /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugSymbols=false /p:DebugType=None
if errorlevel 1 (
    echo.
    echo [HATA] dotnet publish basarisiz oldu. Mevcut test ZIP dosyasi korunmustur.
    goto :fail
)

copy /Y "%ROOT%\OEHS_DENEME_PAKETI.flag" "%PUBLISH%\OEHS_DENEME_PAKETI.flag" >nul
if errorlevel 1 (
    echo [HATA] Deneme paketi izole-veri isareti kopyalanamadi.
    goto :fail
)
copy /Y "%ROOT%\OEHS_DENEME_KULLANIM.txt" "%PUBLISH%\KULLANIM.txt" >nul
if errorlevel 1 (
    echo [HATA] Kullanim yonergesi kopyalanamadi.
    goto :fail
)

if not exist "%PUBLISH%\TEHYS.exe" (
    echo [HATA] Beklenen TEHYS.exe bulunamadi.
    goto :fail
)

echo.
echo [4/4] Yayin dosyalari gecici ZIP'e ekleniyor...
if exist "%ZIP_TMP%" del /q "%ZIP_TMP%"
if errorlevel 1 (
    echo [HATA] Gecici ZIP temizlenemedi.
    goto :fail
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%PUBLISH%\*' -DestinationPath '%ZIP_TMP%' -Force"
if errorlevel 1 (
    echo [HATA] ZIP olusturulamadi. Mevcut test ZIP dosyasi korunmustur.
    goto :fail
)
if not exist "%ZIP_TMP%" (
    echo [HATA] Gecici ZIP dosyasi olusmadi.
    goto :fail
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; $z=[IO.Compression.ZipFile]::OpenRead('%ZIP_TMP%'); try { if (-not ($z.Entries | Where-Object { $_.FullName -eq 'TEHYS.exe' -or $_.FullName.EndsWith('/TEHYS.exe') })) { exit 2 } } finally { $z.Dispose() }"
if errorlevel 1 (
    echo [HATA] ZIP dogrulamasi basarisiz: TEHYS.exe bulunamadi.
    goto :fail
)

move /y "%ZIP_TMP%" "%ZIP%" >nul
if errorlevel 1 (
    echo [HATA] Yeni ZIP hedef konumuna tasinamadi.
    goto :fail
)

echo.
echo ============================================================
echo   BASARILI - OEHS TEST PAKETI HAZIR
echo ============================================================
echo   ZIP: %ZIP%
echo.
echo Paket Windows x64 icindir ve self-contained olarak yayinlanir.
echo Yayin klasorundeki gerekli dosyalar ZIP'e dahil edilir.
echo Kisisel verileri veya gercek kurum kayitlarini pakete eklemeyin.
echo.
pause
exit /b 0

:fail
echo.
echo Islem tamamlanamadi. Yukaridaki hata mesajini kontrol edin.
echo Mevcut test ZIP dosyasi basarisizlik halinde korunur.
echo.
pause
exit /b 1
