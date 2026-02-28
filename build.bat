@echo off
setlocal

set "publishDir=src\muten.Tray\bin\Release\net8.0-windows\win-x64\publish"
set "zipPath=muten.zip"

:: Get version from argument or latest git tag (strip leading 'v')
if not "%~1"=="" (
    set "version=%~1"
) else (
    for /f "tokens=*" %%t in ('git describe --tags --abbrev^=0 2^>nul') do set "version=%%t"
)
if defined version (
    set "version=%version:v=%"
) else (
    set "version=0.0.0"
)

echo Publishing muten v%version%...
dotnet publish src\muten.Tray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:Version=%version% -o "%publishDir%"
if %errorlevel% neq 0 (
    echo Publish failed
    exit /b 1
)

if exist "%zipPath%" del "%zipPath%"

echo Creating zip...
powershell -NoProfile -Command "Compress-Archive -Path '%publishDir%\*' -DestinationPath '%zipPath%'"

echo Done: %zipPath% (v%version%)
