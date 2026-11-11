#define DataFolderName "TEHYS"
#define ExistingCleanTask
#define CustomDataPreparation
#ifndef AppVersion
  #define AppVersion "0.1.8"
#endif
#ifndef PackageDir
  #define PackageDir "..\dist\TEHYS-" + AppVersion + "-win-x64"
#endif
[Setup]
AppId={{65E1A409-6B5E-4316-9608-82FA2CC08E15}
AppName=TEHYS
AppVersion={#AppVersion}
AppVerName=TEHYS {#AppVersion}
AppPublisher=Aşkın KESKİNBALTA & Murat ÇALIMLIER
AppPublisherURL=https://github.com/m-calimlier/TEHYS
DefaultDirName={localappdata}\Programs\TEHYS
DefaultGroupName=TEHYS
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=TEHYS-{#AppVersion}-Setup-win-x64
SetupIconFile=..\src\OzelEgitimHakedis.Wpf\Assets\tehys.ico
UninstallDisplayIcon={app}\TEHYS.exe
#ifdef FastBuild
Compression=zip/1
SolidCompression=no
#else
Compression=lzma2
SolidCompression=yes
#endif
WizardStyle=modern dark polar includetitlebar hidebevels
WizardBackColor=#10263d
WizardSizePercent=115,115
WizardImageFile=Assets\setup-sidebar.png
WizardSmallImageFile=Assets\setup-mark.png
WizardImageBackColor=#10263d
WizardSmallImageBackColor=#10263d
WizardImageStretch=yes
ShowLanguageDialog=no
DisableWelcomePage=no
AppCopyright=Created by Aşkın KESKİNBALTA & Murat ÇALIMLIER
CloseApplications=yes
RestartApplications=no
UsePreviousTasks=no
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=TEHYS kullanıcı kurulumu
[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
[Messages]
SetupWindowTitle=TEHYS · Kurulum
WelcomeLabel1=TEHYS'ye hoş geldiniz
WelcomeLabel2=Taşımalı Eğitim Hakediş Yönetim Sistemi%n%nTüm kayıtlar, belgeler, arşivler ve yedekler masaüstündeki TEHYS klasöründe tutulur.%n%nNormal kurulum eski verilerinizi doğrulayarak bu klasöre taşır. Temiz kurulum seçilirse bu Windows hesabının bilinen TEHYS/OEHS veri klasörleri kalıcı silinir.%n%nSürüm {#AppVersion} · Windows x64
FinishedHeadingLabel=TEHYS kullanıma hazır
FinishedLabel=Kurulum tamamlandı.%n%nProgramı Başlat menüsünden veya oluşturduysanız masaüstü kısayolundan açabilirsiniz.%n%nCreated by Aşkın KESKİNBALTA & Murat ÇALIMLIER
[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"
Name: "cleandata"; Description: "Temiz kurulum — eski kayıtları, yedekleri ve çıktıları sil"; GroupDescription: "Veriler (yalnız bu Windows kullanıcısı):"; Flags: unchecked
[Files]
Source: "{#PackageDir}\TEHYS.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\KULLANIM.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\Lisanslar\*"; DestDir: "{app}\Lisanslar"; Flags: ignoreversion recursesubdirs
[Icons]
Name: "{group}\TEHYS"; Filename: "{app}\TEHYS.exe"
Name: "{group}\TEHYS Kaldır"; Filename: "{uninstallexe}"
Name: "{userdesktop}\TEHYS"; Filename: "{app}\TEHYS.exe"; Tasks: desktopicon
[InstallDelete]
Type: files; Name: "{app}\OEHS_DENEME_PAKETI.flag"
[Run]
Filename: "{app}\TEHYS.exe"; Description: "TEHYS programını aç"; Flags: nowait postinstall skipifsilent
; User records, archives and PDFs are deliberately absent from Files/UninstallDelete.

[Code]
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + MemoTasksInfo + NewLine + NewLine +
    'Veri klasörü:' + NewLine + Space + ExpandConstant('{userdesktop}\TEHYS') + NewLine +
    Space + 'Kayitlar / Belgeler / Yedekler / Calisma';
  if not WizardIsTaskSelected('cleandata') then
    Result := Result + NewLine + NewLine + 'Mevcut veriler doğrulanarak korunur ve masaüstüne taşınır.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Mode: String;
  ExitCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    ExtractTemporaryFile('TEHYS.exe');
    Mode := '--installer-prepare';
    if WizardIsTaskSelected('cleandata') then Mode := '--installer-clean';
    if not Exec(ExpandConstant('{tmp}\TEHYS.exe'), Mode + ' "' + ExpandConstant('{app}') + '"',
      ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Veri hazırlama aracı başlatılamadı. Kurulum durduruldu.');
    if ExitCode <> 0 then
      RaiseException('Veriler hazırlanamadı. Açık tüm TEHYS/OEHS pencerelerini kapatıp klasör izinlerini kontrol edin. Kurulum durduruldu. Kod: ' + IntToStr(ExitCode));
  end;
end;

#include "CommonWizard.iss"
