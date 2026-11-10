@echo off
rem Double-click: the game opens in the browser. Close this window to stop the server.
cd /d "%~dp0"
where python >nul 2>nul && (python serve.py & goto :eof)
where py >nul 2>nul && (py -3 serve.py & goto :eof)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0serve.ps1"
