; Dalviiio DDR Launcher installer (Inno Setup 6). Built by installer\build.ps1, which passes /DAppVersion=x.y.z.
; Per-user install, no administrator rights: %LocalAppData%\Programs\DalviiioDDRLauncher.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Dalviiio DDR Launcher"
#define AppExe "DalviiioDDRLauncher.exe"

[Setup]
AppId={{1AFD62C6-CC83-400C-A0E3-72638341FE02}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Dalviiio
DefaultDirName={autopf}\DalviiioDDRLauncher
DisableDirPage=auto
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\publish\installer
OutputBaseFilename=DalviiioDDRLauncher-Setup-{#AppVersion}
SetupIconFile=..\src\DdrLauncher.App\Assets\DalviiioDDRLauncher.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; The installer starts in English and lists its languages to pick another. The launcher itself follows Windows' language.
ShowLanguageDialog=yes
LanguageDetectionMethod=none
UsePreviousLanguage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.LauncherOpen=Dalviiio DDR Launcher is open. Close it, then click Retry.
english.CloseFirst=Close Dalviiio DDR Launcher, then uninstall it again.
english.CleanupFailed=The launcher could not take all its changes out of the game (see the message it showed). Uninstall anyway?
english.DeleteData=Also delete your profiles, liveries, mod library and settings (%LocalAppData%\DalviiioDDRLauncher)?%n%nChoose No to keep them for a later install.
french.LauncherOpen=Dalviiio DDR Launcher est ouvert. Fermez-le, puis cliquez sur Réessayer.
french.CloseFirst=Fermez Dalviiio DDR Launcher, puis relancez la désinstallation.
french.CleanupFailed=Le launcher n'a pas pu retirer toutes ses modifications du jeu (voir son message). Désinstaller quand même ?
french.DeleteData=Supprimer aussi vos profils, livrées, bibliothèque de mods et paramètres (%LocalAppData%\DalviiioDDRLauncher) ?%n%nChoisissez Non pour les garder pour une prochaine installation.
german.LauncherOpen=Dalviiio DDR Launcher ist geöffnet. Schließe ihn und klicke dann auf Wiederholen.
german.CloseFirst=Schließe Dalviiio DDR Launcher und starte die Deinstallation erneut.
german.CleanupFailed=Der Launcher konnte nicht alle seine Änderungen aus dem Spiel entfernen (siehe seine Meldung). Trotzdem deinstallieren?
german.DeleteData=Auch deine Profile, Lackierungen, Mod-Bibliothek und Einstellungen löschen (%LocalAppData%\DalviiioDDRLauncher)?%n%nWähle Nein, um sie für eine spätere Installation zu behalten.
spanish.LauncherOpen=Dalviiio DDR Launcher está abierto. Ciérralo y pulsa Reintentar.
spanish.CloseFirst=Cierra Dalviiio DDR Launcher y vuelve a desinstalarlo.
spanish.CleanupFailed=El launcher no pudo quitar todos sus cambios del juego (mira su mensaje). ¿Desinstalar igualmente?
spanish.DeleteData=¿Borrar también tus perfiles, decoraciones, biblioteca de mods y ajustes (%LocalAppData%\DalviiioDDRLauncher)?%n%nElige No para conservarlos para otra instalación.
italian.LauncherOpen=Dalviiio DDR Launcher è aperto. Chiudilo, poi fai clic su Riprova.
italian.CloseFirst=Chiudi Dalviiio DDR Launcher, poi disinstallalo di nuovo.
italian.CleanupFailed=Il launcher non è riuscito a togliere tutte le sue modifiche dal gioco (vedi il suo messaggio). Disinstallare comunque?
italian.DeleteData=Eliminare anche profili, livree, libreria di mod e impostazioni (%LocalAppData%\DalviiioDDRLauncher)?%n%nScegli No per tenerli per una prossima installazione.
portuguese.LauncherOpen=O Dalviiio DDR Launcher está aberto. Feche-o e clique em Repetir.
portuguese.CloseFirst=Feche o Dalviiio DDR Launcher e desinstale de novo.
portuguese.CleanupFailed=O launcher não conseguiu tirar todas as alterações dele do jogo (veja a mensagem). Desinstalar mesmo assim?
portuguese.DeleteData=Excluir também seus perfis, pinturas, biblioteca de mods e configurações (%LocalAppData%\DalviiioDDRLauncher)?%n%nEscolha Não para mantê-los para outra instalação.
russian.LauncherOpen=Dalviiio DDR Launcher открыт. Закройте его и нажмите «Повтор».
russian.CloseFirst=Закройте Dalviiio DDR Launcher и запустите удаление снова.
russian.CleanupFailed=Лаунчер не смог убрать из игры все свои изменения (см. его сообщение). Всё равно удалить?
russian.DeleteData=Удалить также профили, ливреи, библиотеку модов и настройки (%LocalAppData%\DalviiioDDRLauncher)?%n%nВыберите «Нет», чтобы сохранить их для следующей установки.
japanese.LauncherOpen=Dalviiio DDR Launcher が開いています。閉じてから「再試行」をクリックしてください。
japanese.CloseFirst=Dalviiio DDR Launcher を閉じてから、もう一度アンインストールしてください。
japanese.CleanupFailed=ランチャーはゲームから変更をすべて外せませんでした（表示されたメッセージを確認してください）。それでもアンインストールしますか？
japanese.DeleteData=プロファイル、カラーリング、MOD ライブラリ、設定（%LocalAppData%\DalviiioDDRLauncher）も削除しますか？%n%n次回のインストール用に残す場合は「いいえ」を選んでください。

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\DdrLauncher.App\Fonts\OFL.txt"; DestDir: "{app}\Licenses"; DestName: "NunitoSans-OFL.txt"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; An update started from the launcher runs silently: open the launcher again once the new version is in place.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[Code]
const
  LauncherLock = 'Local\DalviiioDDRLauncher';

function InitializeSetup(): Boolean;
var
  I: Integer;
begin
  { Never install while the launcher is open (same lock as its single instance). After an update started from the launcher,
    it closes by itself: wait a few seconds for it. A silent install then gives up instead of asking. }
  Result := True;
  for I := 1 to 20 do
  begin
    if not CheckForMutexes(LauncherLock) then
      Exit;
    Sleep(500);
  end;
  while CheckForMutexes(LauncherLock) do
    if SuppressibleMsgBox(CustomMessage('LauncherOpen'), mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  { First the launcher takes its pak and the mods it placed out of the game, so the game is left as it ships. }
  Result := True;
  if Exec(ExpandConstant('{app}\{#AppExe}'), '--uninstall-cleanup', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 2 then
    begin
      SuppressibleMsgBox(CustomMessage('CloseFirst'), mbInformation, MB_OK, IDOK);
      Result := False;
    end
    else if ResultCode <> 0 then
      Result := SuppressibleMsgBox(CustomMessage('CleanupFailed'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Profiles, liveries and the mod library are the user's: only removed when asked. }
  if CurUninstallStep = usPostUninstall then
    if SuppressibleMsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{localappdata}\DalviiioDDRLauncher'), True, True, True);
end;
