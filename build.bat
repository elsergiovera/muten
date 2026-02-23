@echo off
setlocal

set "publishDir=src\muten.Tray\bin\Release\net8.0-windows\win-x64\publish"
set "zipPath=muten.zip"

echo Publishing muten.Tray...
dotnet publish src\muten.Tray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o "%publishDir%"
if %errorlevel% neq 0 (
    echo Publish failed
    exit /b 1
)

if exist "%zipPath%" del "%zipPath%"

echo Creating zip...
powershell -NoProfile -Command "Compress-Archive -Path '%publishDir%\*' -DestinationPath '%zipPath%'"

echo Done: %zipPath%
