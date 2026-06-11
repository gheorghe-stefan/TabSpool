@echo off
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo Error: .NET SDK is not installed or not in your PATH.
    echo Please install .NET SDK 10.0 or later and try again.
    pause
    exit /b 1
)

echo Compiling TabSpool.exe in C# (.NET 10)...
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false

if %errorlevel% equ 0 (
    copy /y bin\Release\net10.0-windows\win-x64\publish\TabSpool.exe .\TabSpool.exe >nul
    echo Compile Successful! TabSpool.exe created in root directory.
    echo.
    echo Double-click TabSpool.exe to run it. It will run in your system tray with a custom icon.
) else (
    echo Compile FAILED. Please check compiler errors above.
)
pause
