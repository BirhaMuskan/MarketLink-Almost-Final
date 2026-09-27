@echo off
cd /d %~dp0

echo ==========================================
echo MarketLink Free Local AI Service
echo ==========================================

if not exist .venv (
    echo First-time setup: creating Python virtual environment...
    py -m venv .venv

    if errorlevel 1 (
        echo.
        echo ERROR: Python could not create the virtual environment.
        echo Make sure Python is installed and "py" works in Command Prompt.
        pause
        exit /b 1
    )

    call .venv\Scripts\activate.bat

    echo Installing free AI packages...
    python -m pip install --upgrade pip
    pip install -r requirements.txt

    if errorlevel 1 (
        echo.
        echo ERROR: Python packages could not be installed.
        pause
        exit /b 1
    )
) else (
    call .venv\Scripts\activate.bat
)

echo.
echo Starting MarketLink AI on http://127.0.0.1:8001
echo Press CTRL+C to stop it when running manually.
echo.

python -m uvicorn app:app --host 127.0.0.1 --port 8001
