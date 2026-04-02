@echo off
setlocal enabledelayedexpansion

:: Konserva Full Build - Self-contained (~60 MB)

echo ============================================
echo    Konserva Full Build (Self-contained)
echo    Single file (~60 MB, no dependencies)
echo ============================================
echo.

echo [1/3] Cleaning...
dotnet clean konserva-app/konserva-app.csproj -c Release -v q
if exist "publish\Full" rmdir /s /q "publish\Full"
if exist "konserva-app\bin" rmdir /s /q "konserva-app\bin"
if exist "konserva-app\obj" rmdir /s /q "konserva-app\obj"
echo.

echo [2/3] Building...
dotnet build konserva-app/konserva-app.csproj -c Release -v q
if errorlevel 1 goto :error
echo.

echo [3/3] Publishing...
dotnet publish konserva-app/konserva-app.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    -o "publish\Full"
if errorlevel 1 goto :error

echo ============================================
echo    Build Complete!
echo ============================================
echo.
for %%A in ("publish\Full\Konserva.exe") do echo Size: %%~zA bytes
echo.
goto :end

:error
echo Build Failed!
exit /b 1

:end
endlocal

pause
