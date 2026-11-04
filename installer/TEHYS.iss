#ifndef AppVersion
  #define AppVersion "0.1.5"
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
AppPublisherURL=https://github.com/mcalimlier/OzelEgitimHakedisSistemi
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
Compression=lzma2
SolidCompression=yes
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
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=TEHYS kullanıcı kurulumu
[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
[Messages]
SetupWindowTitle=TEHYS · Kurulum
WelcomeLabel1=TEHYS'ye hoş geldiniz
WelcomeLabel2=Taşımalı Eğitim Hakediş Yönetim Sistemi%n%nÖzel Eğitim Taşıma ve Temel Eğitim/Ortaöğretim Taşıma hakedişlerinizi aynı çalışma ortamında yönetin.%n%nBu sihirbaz program dosyalarını ve seçtiğiniz kısayolları kurar. Mevcut kayıtlarınız, arşivleriniz ve PDF belgeleriniz korunur.%n%nSürüm {#AppVersion} · Windows x64
FinishedHeadingLabel=TEHYS kullanıma hazır
FinishedLabel=Kurulum tamamlandı.%n%nProgramı Başlat menüsünden veya oluşturduysanız masaüstü kısayolundan açabilirsiniz.%n%nCreated by Aşkın KESKİNBALTA & Murat ÇALIMLIER
[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"
[Files]
Source: "{#PackageDir}\TEHYS.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\KULLANIM.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\Lisanslar\*"; DestDir: "{app}\Lisanslar"; Flags: ignoreversion recursesubdirs
[Icons]
Name: "{group}\TEHYS"; Filename: "{app}\TEHYS.exe"
Name: "{group}\TEHYS Kaldır"; Filename: "{uninstallexe}"
Name: "{userdesktop}\TEHYS"; Filename: "{app}\TEHYS.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\TEHYS.exe"; Description: "TEHYS programını aç"; Flags: nowait postinstall skipifsilent
; User records, archives and PDFs are deliberately absent from Files/UninstallDelete.
