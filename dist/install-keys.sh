#!/usr/bin/env bash
set -euo pipefail
base=/var/www/schoolpiboardoff
svc=schoolpiboardoff
dll=SchoolPiBoard.LicenseServer.dll
test "$(id -u)" = 0 || { echo 'Запустите от root.'; exit 1; }
test "$(systemctl show "$svc" --value -p WorkingDirectory)" = "$base/api"
stage=$(mktemp -d "$base/.deploy-a82802c.XXXXXXXX")
mkdir "$stage/new"
curl -fsSL --retry 3 https://raw.githubusercontent.com/pootymoty/SchoolPiBoard/packages/license-server-a82802c/dist/license-server.tar.gz -o "$stage/package.tar.gz"
echo "b73b2055901827ed8368e039e1e1c1cb88d159e2a5012cb5ae3995dab129a364  $stage/package.tar.gz" | sha256sum -c -
tar -xzf "$stage/package.tar.gz" -C "$stage/new"
test -s "$stage/new/$dll"
test -s "$stage/new/sql/005_renewal_notices.sql"
if cmp -s "$stage/new/$dll" "$base/api/$dll"; then
    echo 'Эта сборка уже установлена.'
    exit 0
fi
echo "c8018ebafeef2e345ef05f735aece6bfff422fba11afd51fa67d5a8a0734e7c7  $base/api/$dll" | sha256sum -c -
chown -R www-data:www-data "$stage/new"
healthy() {
    for attempt in {1..30}; do
        if curl -fsS --max-time 2 http://127.0.0.1:5080/health 2>/dev/null |
            python3 -c 'import json,sys; sys.exit(json.load(sys.stdin).get("status") != "ok")' 2>/dev/null; then return 0; fi
        sleep 1
    done
    return 1
}
systemctl stop "$svc"
if ! mv "$base/api" "$stage/previous"; then systemctl start "$svc"; exit 1; fi
if mv "$stage/new" "$base/api" && systemctl start "$svc" && healthy; then
    echo "Установлена сборка a82802c. Проверка /health прошла. Предыдущая версия: $stage/previous"
else
    journalctl -u "$svc" -n 30 --no-pager
    systemctl stop "$svc"
    if test -d "$base/api"; then mv "$base/api" "$stage/failed"; fi
    mv "$stage/previous" "$base/api"
    systemctl start "$svc"
    healthy && echo 'Предыдущая версия восстановлена и отвечает.'
    exit 1
fi
