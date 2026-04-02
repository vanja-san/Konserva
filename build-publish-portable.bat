@echo off
setlocal enabledelayedexpansion

:: Konserva Portable Build - Framework-dependent Single File (~3 MB)

echo ============================================
echo    Konserva Portable Build (Requires .NET)
echo    Single file (~3 MB, requires .NET 10)
echo ============================================
echo.

echo [1/3] Cleaning...
dotnet clean konserva-app/konserva-app.csproj -c Release -v q
if exist "publish\Portable" rmdir /s /q "publish\Portable"
if exist "konserva-app\bin" rmdir /s /q "konserva-app\bin"
if exist "konserva-app\obj" rmdir /s /q "konserva-app\obj"
echo.

echo [2/3] Building...
dotnet build konserva-app/konserva-app.csproj -c Release -v q
if errorlevel 1 goto :error
echo.

echo [3/3] Publishing Portable (single file)...
dotnet publish konserva-app/konserva-app.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=false ^
    -p:IncludeNativeLibrariesForSelfExtract=false ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    -o "konserva-app\publish\Portable"
if errorlevel 1 goto :error

echo.
if not exist "publish\Portable" mkdir "publish\Portable"
xcopy /Y "konserva-app\publish\Portable\*" "publish\Portable\" >nul

echo ============================================
echo    Build Complete!
echo ============================================
echo.
for %%A in ("publish\Portable\Konserva.exe") do echo Size: %%~zA bytes
echo.
echo Requires .NET 10 Runtime!
echo.
goto :end

:error
echo Build Failed!
exit /b 1

:end
endlocal

pause
