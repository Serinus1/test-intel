!include MUI2.nsh
!include FileFunc.nsh

Name "Test Intel Reporting Tool"
OutFile "test-intel.exe"
InstallDir "$LOCALAPPDATA\Test Intel Reporting Tool"
RequestExecutionLevel user

Var UninstallKey

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "German"
!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "Japanese"

Section "Install"
	StrCpy $UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\TestIntelReporter"

	SetOutPath $INSTDIR
	File "PleaseIgnore.IntelMap\bin\Release\PleaseIgnore.IntelMap.dll"
	File "PleaseIgnore.IntelMap\bin\Release\PleaseIgnore.IntelMap.xml"
	File "PleaseIgnore.IntelMap\bin\Release\CodeContracts\PleaseIgnore.IntelMap.Contracts.dll"
	File "TestIntelReporter\bin\Release\TestIntelReporter.exe"
	File "TestIntelReporter\bin\Release\TestIntelReporter.exe.config"
	CreateShortCut "$SMPROGRAMS\Test Intel Reporting Tool.lnk" \
		"$INSTDIR\TestIntelReporter.exe" "" "" "" SW_SHOWNORMAL
	CreateShortCut "$SMSTARTUP\Test Intel Reporting Tool.lnk" \
		"$INSTDIR\TestIntelReporter.exe" "" "" "" SW_SHOWMINIMIZED

	WriteUninstaller "$INSTDIR\Uninstaller.exe"
	WriteRegStr HKCU $UninstallKey "DisplayIcon" "$INSTDIR\TestIntelReporter.exe"
	WriteRegStr HKCU $UninstallKey "DisplayName" "Test Intel Reporting Tool"
	WriteRegStr HKCU $UninstallKey "InstallLocation" "$INSTDIR"
	WriteRegStr HKCU $UninstallKey "Publisher" "Test Alliance Please Ignore"
	WriteRegStr HKCU $UninstallKey "UninstallString" "$INSTDIR\Uninstaller.exe"
	WriteRegStr HKCU $UninstallKey "URLInfoAbout" "http://maps.pleaseignore.com/"
	WriteRegDWORD HKCU $UninstallKey "NoModify" 1
	WriteRegDWORD HKCU $UninstallKey "NoRepair" 1
	${GetFileVersion} "$INSTDIR\TestIntelReporter.exe" $0
	WriteRegStr HKCU $UninstallKey "DisplayVersion" $0
	${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
	WriteRegDWORD HKCU $UninstallKey "EstimatedSize" $0
SectionEnd

Section "Uninstall"
	StrCpy $UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\TestIntelReporter"

	Delete "$INSTDIR\Uninstaller.exe"
	Delete "$INSTDIR\PleaseIgnore.IntelMap.dll"
	Delete "$INSTDIR\TestIntelReporter.exe"
	Delete "$INSTDIR\TestIntelReporter.exe.config"
	RMDir "$INSTDIR"
	Delete "$SMPROGRAMS\Test Intel Reporting Tool.lnk"
	Delete "$SMSTARTUP\Test Intel Reporting Tool.lnk"
	DeleteRegKey /ifempty HKCU $UninstallKey
SectionEnd
