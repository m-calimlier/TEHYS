@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PROJECT=.\src\OzelEgitimHakedis.Wpf\OzelEgitimHakedis.Wpf.csproj"
set "APP=.\src\OzelEgitimHakedis.Wpf\bin\Debug\net8.0-windows\TEHYS.exe"
set "LOG=%~dp0OEHS_CALISMA_LOG.txt"

echo.
echo ==========================================
echo   TEHYS - DERLE VE CALISTIR
echo ==========================================
echo.

echo [%date% %time%] OEHS baslatma kaydi > "%LOG%"

echo [1/2] Paketler kontrol ediliyor...
dotnet restore "%PROJECT%"
if errorlevel 1 (
    echo.
    echo [HATA] Restore basarisiz.
    echo Ayrinti: OEHS_CALISMA_LOG.txt
    echo [%date% %time%] RESTORE BASARISIZ >> "%LOG%"
    pause
    exit /b 1
)

echo.
echo [2/2] Program derleniyor...
dotnet build "%PROJECT%" --configuration Debug --no-restore
if errorlevel 1 (
    echo.
    echo [HATA] Derleme basarisiz.
    echo Ayrinti: OEHS_CALISMA_LOG.txt
    echo [%date% %time%] BUILD BASARISIZ >> "%LOG%"
    pause
    exit /b 1
)

echo.
echo [OK] Derleme tamamlandi.
echo [OK] OEHS baslatiliyor...
echo [%date% %time%] BUILD BASARILI >> "%LOG%"
echo [%date% %time%] PROGRAM BASLATILIYOR >> "%LOG%"

if not exist "%APP%" (
    echo [HATA] TEHYS.exe bulunamadi: %APP%
    echo [%date% %time%] EXE BULUNAMADI >> "%LOG%"
    pause
    exit /b 1
)

start "" "%APP%"

echo.
echo [OK] OEHS baslatildi.
echo.
exit /b 0
