@echo off
chcp 65001 >nul
rem Запуск на стенде: десктоп работает с той же БД на сервере, что и бронь с телефона.
rem Одно ssh-подключение: туннель к БД + пароль БД. Пароль от ssh-ключа спросит один раз в окне туннеля.
set "SRV=root@213.226.112.230"
set "PWFILE=%TEMP%\proday_db.txt"
if "%~1"=="tunnel" goto tunnel

del "%PWFILE%" 2>nul
start "NEON ARENA tunnel" "%~f0" tunnel
echo Введи пароль от ssh-ключа в окне "NEON ARENA tunnel" (если спросит)...
set /a N=0
:wait
timeout /t 1 >nul
set "SZ=0"
if exist "%PWFILE%" for %%F in ("%PWFILE%") do set "SZ=%%~zF"
if not "%SZ%"=="0" goto connected
set /a N+=1
if %N% lss 180 goto wait
echo Не удалось подключиться к серверу. Проверь интернет и ключ: ssh %SRV%
pause
exit /b 1

:connected
set /p PW=<"%PWFILE%"
del "%PWFILE%"
set "PRODAY_DB=Host=127.0.0.1;Port=40002;Database=proday;Username=xaliks;Password=%PW%;SSL Mode=Disable"
dotnet run --project "%~dp0ProDay\ProDay.csproj" -c Release
taskkill /fi "WINDOWTITLE eq NEON ARENA tunnel*" /t /f >nul 2>&1
exit /b

:tunnel
echo Туннель к серверу NEON ARENA. Окно не закрывать, пока работает приложение.
ssh -o ConnectTimeout=10 -o ServerAliveInterval=30 -o ExitOnForwardFailure=yes -L 40002:127.0.0.1:40001 %SRV% "sed -n s/^DB_PASSWORD=//p /opt/proday/.env; exec sleep infinity" > "%PWFILE%"
echo Туннель закрыт.
pause
