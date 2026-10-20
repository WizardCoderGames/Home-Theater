@echo off
rem Builds the app, then the installer that carries it:  dist\EricsHomeTheater-Setup.exe
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
call "%~dp0build.cmd"
if errorlevel 1 exit /b 1
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /out:dist\EricsHomeTheater-Setup.exe /win32icon:src\Theater.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /resource:Theater.exe,payload.exe /resource:update_repo.txt,update_repo.txt installer\Setup.cs src\Updater.cs
if errorlevel 1 exit /b 1
echo Built dist\EricsHomeTheater-Setup.exe
