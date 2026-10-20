@echo off
rem Builds Theater.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:Theater.exe /win32icon:src\Theater.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Management.dll ^
  /resource:src\remote.html,remote.html /resource:src\genres.txt,genres.txt /resource:update_repo.txt,update_repo.txt ^
  src\*.cs
