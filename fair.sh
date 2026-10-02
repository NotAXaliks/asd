#!/bin/sh
# Запуск на стенде (macOS) — то же, что fair.cmd: десктоп работает с БД на сервере, где крутится бронь с телефона.
# Нужны интернет и ssh-ключ для root@213.226.112.230.
set -e
cd "$(dirname "$0")"
SRV=root@213.226.112.230
TUNNEL=40002:127.0.0.1:40001

# Одно ssh-подключение: туннель к БД + пароль БД (в git его нет). Пароль от ssh-ключа спросит один раз.
pkill -f "$TUNNEL" || true
PWFILE=$(mktemp)
ssh -f -o ConnectTimeout=10 -o ServerAliveInterval=30 -o ExitOnForwardFailure=yes -L $TUNNEL $SRV \
  "sed -n s/^DB_PASSWORD=//p /opt/proday/.env; exec sleep infinity" > "$PWFILE"
trap 'pkill -f "$TUNNEL"; rm -f "$PWFILE"' EXIT
for _ in $(seq 50); do [ -s "$PWFILE" ] && break; sleep 0.2; done
PW=$(cat "$PWFILE"); rm -f "$PWFILE"
[ -n "$PW" ] || { echo "Не удалось подключиться к серверу. Проверь интернет и ключ: ssh $SRV"; exit 1; }

export PRODAY_DB="Host=127.0.0.1;Port=40002;Database=proday;Username=xaliks;Password=$PW;SSL Mode=Disable"
DOTNET=dotnet; [ -x ~/.dotnet/dotnet ] && DOTNET=~/.dotnet/dotnet
$DOTNET run --project ProDay/ProDay.csproj -c Release
