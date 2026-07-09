@echo off
echo Building BricsAI Solution...
dotnet build -c Debug
if %errorlevel% neq 0 (
    echo Build failed!
    pause
    exit /b %errorlevel%
)
echo Build succeeded!
echo.
echo Interface (Overlay): BricsAI.Overlay\bin\Debug\net8.0-windows\BricsAI.Overlay.exe
echo Plugin (Executor): BricsAI.Plugin\bin\Debug\net461\BricsAI.Plugin.dll
echo.
pause
