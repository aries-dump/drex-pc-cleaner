@echo off
setlocal
cd /d "%~dp0src"
where dotnet >nul 2>&1 || (echo .NET 10 SDK not found. Install from https://dotnet.microsoft.com/download & exit /b 1)
if exist "..\dist" rmdir /s /q "..\dist"

echo === Framework-dependent single-file (small; needs .NET 10 Desktop Runtime) ===
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "..\dist\framework-dependent" || exit /b 1

echo === Self-contained single-file (no runtime needed; larger) ===
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "..\dist\self-contained" || exit /b 1

echo.
echo Output:
dir /b "..\dist\framework-dependent\DREX_PC_CLEANER.exe" "..\dist\self-contained\DREX_PC_CLEANER.exe"
endlocal
