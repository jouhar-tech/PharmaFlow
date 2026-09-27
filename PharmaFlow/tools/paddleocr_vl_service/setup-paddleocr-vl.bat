@echo off
setlocal
cd /d %~dp0

where python >nul 2>&1
if errorlevel 1 (
    echo Python 3.9-3.13 is required.
    echo Install Python from python.org and try again.
    exit /b 1
)

if not exist ".venv\Scripts\python.exe" (
    echo Creating PaddleOCR virtual environment...
    python -m venv .venv
    if errorlevel 1 exit /b 1
)

call ".venv\Scripts\activate.bat"
python -m pip install --upgrade pip
python -m pip install -r requirements.txt

echo.
echo PaddleOCR-VL environment is ready.
echo Start the service with start-paddleocr-vl.bat
