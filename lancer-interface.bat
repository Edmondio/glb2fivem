@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title glb2fivem
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1

rem ---- 1. SDK .NET 8 (ou plus récent) ----
where dotnet >nul 2>&1 || goto :nodotnet
dotnet --list-sdks 2>nul | findstr /r "^8\. ^9\. ^1[0-9]\." >nul || goto :nodotnet

rem ---- 2. Dépendances (uniquement la première fois) ----
if not exist "third_party\CodeWalker\CodeWalker.Core\CodeWalker.Core.csproj" goto :deps
if not exist "third_party\meshoptimizer\src\meshoptimizer.h" goto :deps
if not exist "third_party\miniPaint\dist\bundle.js" goto :deps
goto :build

:deps
where git >nul 2>&1 || goto :nogit
echo Récupération des dépendances, première fois uniquement...
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\setup-deps.ps1"
if errorlevel 1 goto :fail

rem ---- 3. Compilation (rapide si rien n'a changé) ----
:build
echo Compilation... la première fois peut prendre une minute.
dotnet build "src\Glb2FiveM.Gui\Glb2FiveM.Gui.csproj" -c Release -nologo -v q -clp:ErrorsOnly
if errorlevel 1 goto :fail

rem ---- 4. Lancement ----
cls
"src\Glb2FiveM.Gui\bin\Release\net8.0\glb2fivem-gui.exe"
exit /b 0

:nodotnet
echo.
echo [!] Le SDK .NET 8 est introuvable.
echo     Installe-le avec :  winget install Microsoft.DotNet.SDK.8
echo     puis relance ce fichier.
echo.
pause
exit /b 1

:nogit
echo.
echo [!] Git est nécessaire pour récupérer les dépendances la première fois.
echo     Installe-le avec :  winget install Git.Git
echo     puis relance ce fichier.
echo.
pause
exit /b 1

:fail
echo.
echo [!] Échec. Lis le message ci-dessus.
echo     Si une fenêtre glb2fivem est déjà ouverte, ferme-la : le programme ne peut pas être recompilé pendant qu'il tourne.
echo.
pause
exit /b 1
