#!/bin/bash
# Двойной щелчок — игра откроется в браузере. Окно Терминала держит сервер: закроешь его — сервер остановится.
cd "$(dirname "$0")"
if xcode-select -p >/dev/null 2>&1 || [ -x /opt/homebrew/bin/python3 ] || [ -x /usr/local/bin/python3 ]; then
  if python3 -c "import http.server" >/dev/null 2>&1; then
    exec python3 serve.py
  fi
fi
# Без python3: встроенный в macOS ruby (браузер сам распакует сборку чуть медленнее).
PORT=8800
echo "Игра: http://localhost:$PORT/"
echo "Чтобы остановить сервер, закрой это окно или нажми Ctrl+C."
( sleep 1; open "http://localhost:$PORT/" ) &
exec ruby -run -e httpd . -p $PORT -b 127.0.0.1
