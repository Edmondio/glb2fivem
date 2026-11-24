@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title glb2fivem - création du .exe
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1

echo ============================================================
echo   glb2fivem - compilation des .exe depuis les sources
echo ============================================================
echo.

rem ---- 1. SDK .NET 8 (ou plus récent) ----
where dotnet >nul 2>&1 || goto :nodotnet
dotnet --list-sdks 2>nul | findstr /r "^8\. ^9\. ^1[0-9]\." >nul || goto :nodotnet

rem ---- 2. Sources des dépendances (première fois) ----
if not exist "third_party\CodeWalker\CodeWalker.Core\CodeWalker.Core.csproj" goto :deps
if not exist "third_party\meshoptimizer\src\meshoptimizer.h" goto :deps
if not exist "third_party\miniPaint\dist\bundle.js" goto :deps
goto :publish

:deps
where git >nul 2>&1 || goto :nogit
echo [1/3] Récupération des sources des dépendances...
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\setup-deps.ps1"
if errorlevel 1 goto :fail

:publish
rem Options communes :
rem  - win-x64 autonome en un seul fichier : rien à installer sur le PC qui lance l'exe
rem  - --source nuget.org : uniquement pour télécharger le runtime .NET Windows (une fois, puis en cache)
set PUB=-c Release -r win-x64 -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:DebugType=none -o dist --source https://api.nuget.org/v3/index.json -nologo -v q -clp:ErrorsOnly

echo [2/3] Interface : dist\glb2fivem-gui.exe ...
echo       (la première fois, le runtime .NET Windows est téléchargé : il faut internet)
dotnet publish "src\Glb2FiveM.Gui\Glb2FiveM.Gui.csproj" %PUB%
if errorlevel 1 goto :fail

echo [3/3] Ligne de commande : dist\glb2fivem.exe ...
dotnet publish "src\Glb2FiveM.Cli\Glb2FiveM.Cli.csproj" %PUB%
if errorlevel 1 goto :fail

del /q "dist\*.pdb" 2>nul

echo.
echo ============================================================
echo   Terminé !
echo     dist\glb2fivem-gui.exe   l'interface (double-clic)
echo     dist\glb2fivem.exe       la ligne de commande (glb2fivem --help)
echo   Ces deux fichiers sont autonomes : tu peux les copier où tu veux.
echo ============================================================
echo.
start "" explorer "dist"
pause
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
echo [!] Git est nécessaire pour récupérer les sources des dépendances la première fois.
echo     Installe-le avec :  winget install Git.Git
echo     puis relance ce fichier.
echo.
pause
exit /b 1

:fail
echo.
echo [!] Échec de la compilation. Lis le message ci-dessus.
echo     - "NU1301" ou "Unable to load the service index" : pas d'accès à internet / nuget.org.
echo     - "being used by another process" : ferme glb2fivem-gui.exe s'il est lancé depuis dist\.
echo.
pause
exit /b 1
