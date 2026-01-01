@echo off
chcp 65001 > nul
title Wolf Bot Launcher
cls

echo ==================================================
echo       Wolf Live Telegram Bot Launcher
echo ==================================================
echo.

:: 1. Check for C# Project (Priority)
if exist "TelegramBotController\TelegramBotController.csproj" goto :FOUND_CSHARP
goto :CHECK_PYTHON

:FOUND_CSHARP
echo [INFO] Detected C# Bot Project.

:: Check for .NET SDK
where dotnet >nul 2>nul
if %errorlevel% neq 0 goto :NO_DOTNET

echo [INFO] Starting C# Bot (TelegramBotController)...
echo.
dotnet run --project "TelegramBotController\TelegramBotController.csproj"

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] The bot crashed or failed to start.
)
goto :END

:NO_DOTNET
echo [ERROR] .NET SDK is not installed or not in PATH.
echo Please install .NET 9.0 SDK from: https://dotnet.microsoft.com/download
echo.
echo Falling back to Python check...
goto :CHECK_PYTHON

:CHECK_PYTHON
:: 2. Check for Python/Node (Fallback)
if exist venv\Scripts\activate.bat (
    call venv\Scripts\activate.bat
)

if exist main.py (
    echo [INFO] Running main.py...
    python main.py
) else if exist bot.py (
    echo [INFO] Running bot.py...
    python bot.py
) else if exist index.js (
    echo [INFO] Running index.js...
    node index.js
) else (
    echo [ERROR] No runnable bot file found (main.py, bot.py, index.js, or C# project).
)

:END
echo.
echo Press any key to exit...
pause >nul
