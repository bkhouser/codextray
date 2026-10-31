@echo off
setlocal

set "ROOT=%~dp0"
set "APP_DIR=%ROOT%src\CodexTray\bin\Release\net10.0-windows"
set "APP=%APP_DIR%\CodexTray.exe"
set "DOTNET=dotnet"
if not exist "%ROOT%artifacts\toolchain\dotnet-10.0.403\dotnet.exe" goto system_dotnet
set "DOTNET_ROOT=%ROOT%artifacts\toolchain\dotnet-10.0.403"
set "DOTNET_ROOT_X64=%DOTNET_ROOT%"
set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
goto sdk_ready

:system_dotnet
where dotnet >nul 2>nul
if errorlevel 1 (
    echo CodexTray needs the .NET 10 SDK, but dotnet was not found.
    echo Install .NET 10, then run this file again.
    pause
    exit /b 1
)

:sdk_ready
echo Building CodexTray...
pushd "%ROOT%"
"%DOTNET%" build "src\CodexTray\CodexTray.csproj" --configuration Release -p:RestoreLockedMode=true
if errorlevel 1 (
    echo.
    echo The CodexTray build failed. Review the messages above.
    echo If the executable is locked, exit CodexTray from its tray menu and retry.
    popd
    pause
    exit /b 1
)
popd

:launch
start "" /D "%APP_DIR%" "%APP%"
exit /b 0
