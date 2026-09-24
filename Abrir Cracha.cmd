@echo off
chcp 65001 >nul
title Cracha

rem Se o Cracha ja estiver rodando, so abre o navegador.
powershell -NoProfile -Command "try { Invoke-WebRequest http://localhost:5210/api/sistema -UseBasicParsing -TimeoutSec 2 | Out-Null; exit 0 } catch { exit 1 }"
if %errorlevel%==0 (
  start "" http://localhost:5210
  exit /b
)

rem Com o Azurite instalado (npm install -g azurite), o historico vai para uma Azure Table local,
rem como no Azure; o banco usa um arquivo proprio para os dois ficarem sempre em sincronia.
set "EXTRA="
where azurite-table >nul 2>nul
if %errorlevel%==0 (
  powershell -NoProfile -Command "if (-not (Test-NetConnection 127.0.0.1 -Port 10002 -InformationLevel Quiet -WarningAction SilentlyContinue)) { exit 1 }" >nul 2>nul
  if errorlevel 1 (
    if not exist "%USERPROFILE%\.azurite" mkdir "%USERPROFILE%\.azurite"
    start "Azurite" /min cmd /c azurite-table --location "%USERPROFILE%\.azurite" --silent --skipApiVersionCheck
    timeout /t 3 /nobreak >nul
  )
  set "EXTRA=--Historico:Provedor=AzureTable --Banco:ConnectionString=DataSource=cracha-azure.db"
)

cd /d "%~dp0src\Cracha.Api"
echo.
echo   Cracha - iniciando o servidor...
echo   O navegador abre sozinho quando estiver pronto.
echo   Para encerrar, feche esta janela.
echo.
dotnet run --no-launch-profile -- --urls http://localhost:5210 --AbrirNavegador=true %EXTRA%
if errorlevel 1 pause