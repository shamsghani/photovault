@echo off
title PhotoVault Scanner Bridge
cd /d "%~dp0"

echo ===================================================
echo             PhotoVault Scanner Bridge
echo ===================================================
echo.

if exist "PhotoVaultScannerBridge.exe" (
    echo Starting PhotoVaultScannerBridge.exe ...
    PhotoVaultScannerBridge.exe
) else (
    echo Starting PowerShell bridge ...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scanner-bridge.ps1"
)

pause
