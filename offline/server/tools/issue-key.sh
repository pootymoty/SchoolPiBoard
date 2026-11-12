#!/usr/bin/env bash
# Выдача ключа «Доска Пи» вручную, без оплаты: подарок, замена, проверка.
#
# Запуск на сервере ключей (под root):
#   bash issue-key.sh
# Спросит почту, создаст ключ в базе сервера ключей и покажет его.
# Письмо НЕ отправляется — ключ вы передаёте человеку сами.
#
# Работает напрямую с базой schoolpiboard_licenses, сервис не трогает и не
# перезапускает: ключ сразу активируется в программе, как купленный.
# Магазины онлайн-доски и тарифов, основной сайт — не затрагиваются.
set -euo pipefail

DB="schoolpiboard_licenses"
ALPHABET="ABCDEFGHJKMNPQRSTUVWXYZ23456789"   # тот же, что в LicenseKey.cs

# sudo -u postgres из /root ругается на права текущей папки.
cd /tmp

read -rp "Почта покупателя: " EMAIL
EMAIL="$(printf '%s' "$EMAIL" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
if [[ ! "$EMAIL" =~ ^[^@,\;]+@[^@,\;]+\.[^@,\;]+$ ]]; then
    echo "Это не похоже на адрес почты: '$EMAIL'" >&2
    exit 1
fi

new_key() {
    local key="" i n
    for i in $(seq 1 16); do
        n=$(od -An -N4 -tu4 /dev/urandom | tr -d ' ')
        key+="${ALPHABET:$(( n % ${#ALPHABET} )):1}"
        if (( i % 4 == 0 && i < 16 )); then key+="-"; fi
    done
    printf '%s' "$key"
}

psql_db() {
    sudo -u postgres psql -d "$DB" -v ON_ERROR_STOP=1 -qAt "$@"
}

# Ключ должен быть уникальным — на случай невероятного совпадения пробуем ещё.
for attempt in 1 2 3 4 5; do
    KEY="$(new_key)"
    EXISTS="$(psql_db -v key="$KEY" <<'SQL'
SELECT count(*) FROM licenses WHERE key = :'key';
SQL
)"
    [[ "$EXISTS" == "0" ]] && break
    KEY=""
done
if [[ -z "$KEY" ]]; then
    echo "Не удалось подобрать свободный ключ — попробуйте ещё раз." >&2
    exit 1
fi

read -rp "Создать ключ $KEY для $EMAIL? [y/N] " ANSWER
if [[ ! "$ANSWER" =~ ^[yYдД]$ ]]; then
    echo "Отменено, ничего не создано."
    exit 0
fi

psql_db -v key="$KEY" -v email="$EMAIL" <<'SQL'
INSERT INTO licenses (id, key, email, created_at, revoked)
VALUES (gen_random_uuid(), :'key', :'email', now(), false);
SQL

echo
echo "Готово. Ключ: $KEY"
echo "Почта:       $EMAIL"
echo
echo "Все ключи этой почты:"
psql_db -v email="$EMAIL" <<'SQL'
SELECT key || '   создан ' || to_char(created_at AT TIME ZONE 'Europe/Moscow', 'DD.MM.YYYY HH24:MI')
       || CASE WHEN revoked THEN '   ОТОЗВАН' ELSE '' END
FROM licenses WHERE email = :'email' ORDER BY created_at;
SQL
