@echo off
chcp 65001 >nul
rem Запуск на стенде: десктоп работает с той же БД на сервере, что и бронь с телефона.
rem Нужны интернет и ssh-ключ для root@213.226.112.230. Свёрнутое окно туннеля не закрывать.
set "SRV=root@213.226.112.230"
set "PW="
rem пароль БД берём с сервера, в git его нет
for /f "usebackq delims=" %%p in (`ssh -o ConnectTimeout=10 %SRV% "sed -n s/^DB_PASSWORD=//p /opt/proday/.env"`) do set "PW=%%p"
if not defined PW (
  echo Не удалось зайти на сервер по ssh. Проверь интернет и ключ: ssh %SRV%
  pause
  exit /b 1
)
start "proday-tunnel" /min ssh -N -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 -L 40002:127.0.0.1:40001 %SRV%
timeout /t 3 >nul
set "PRODAY_DB=Host=127.0.0.1;Port=40002;Database=proday;Username=xaliks;Password=%PW%;SSL Mode=Disable"
dotnet run --project "%~dp0ProDay\ProDay.csproj" -c Release
