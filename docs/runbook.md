# Пошаговый запуск трёх продуктов

Порядок между продуктами есть: десктопное приложение знает адрес своего
сервера ключей и зашивает его в exe. Поэтому **сначала сервер ключей,
потом приложение**. Сайт онлайн-доски ни от чего не зависит — его можно
делать когда угодно.

Обозначения: `[ПК]` — команда на вашем компьютере, `[СЕРВЕР]` — на сервере
по ssh.

---

## Продукт 1. Сервер ключей офлайн-доски

**Пройдено на живом сервере** 20 августа 2026 года — ниже то, что реально
работает, а не предполагаемый порядок.

Живёт в `/var/www/schoolpiboardoff`. Нужен PostgreSQL, Redis не нужен.

### Откуда берётся собранный сервис

Ничего не собирается ни на сервере, ни на вашем ПК: при каждом изменении
в `offline/server/**` GitHub Actions публикует готовый архив
(`.github/workflows/license-server.yml`). Постоянная ссылка:

```
https://github.com/pootymoty/SchoolPiBoard/releases/download/license-server-latest/license-server.tar.gz
```

Серверу нужен только рантайм — ни SDK, ни исходников, ни Node.js.

### 1.1. Домен

Поддомен основного домена, отдельный покупать не нужно. Заведите A-запись
`keys` → IP сервера. Этот адрес зашит в приложении
(`offline/LicenseState.cs`, `DefaultServerUrl`), и они должны совпадать.

### 1.2. Пакеты и база

```bash
apt update
apt install -y aspnetcore-runtime-8.0 postgresql

cd /tmp
DBPASS=$(openssl rand -hex 16)
echo "$DBPASS" > /root/.spb-db-pass && chmod 600 /root/.spb-db-pass
sudo -u postgres psql -c "CREATE USER schoolpi WITH PASSWORD '$DBPASS';"
sudo -u postgres psql -c "CREATE DATABASE schoolpiboard_licenses OWNER schoolpi;"
```

Команды с `sudo -u postgres` выполняйте из `/tmp`: из `/root` этот
пользователь получит `Permission denied` и настоящий вывод потеряется
среди предупреждений.

### 1.3. Сервис

```bash
mkdir -p /var/www/schoolpiboardoff/api
cd /tmp
curl -sL -o ls.tar.gz https://github.com/pootymoty/SchoolPiBoard/releases/download/license-server-latest/license-server.tar.gz
tar -xzf ls.tar.gz -C /var/www/schoolpiboardoff/api
chown -R www-data:www-data /var/www/schoolpiboardoff
```

### 1.4. Настройки

Пароль приложения для почты берётся в Яндекс ID (Безопасность → Пароли
приложений → Почта). Пароль от самого ящика SMTP не примет.

```bash
SMTPPASS='пароль_приложения'

cat > /etc/schoolpiboardoff.env <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
ConnectionStrings__Postgres=Host=localhost;Database=schoolpiboard_licenses;Username=schoolpi;Password=$(cat /root/.spb-db-pass)
LICENSE_TOKEN_SECRET=$(openssl rand -hex 32)
Smtp__Host=smtp.yandex.ru
Smtp__Port=465
Smtp__User=info@school-pi.online
Smtp__FromEmail=info@school-pi.online
SMTP_PASSWORD=$SMTPPASS
License__DownloadUrl=https://school-pi.online/download/SchoolPiBoardSetup.exe
License__SupportEmail=info@school-pi.online
Web__SiteUrl=https://school-pi.online
EOF

chmod 600 /etc/schoolpiboardoff.env
chown root:root /etc/schoolpiboardoff.env
```

Настройки Робокассы добавляются сюда же — см. раздел 1.7. Без них сервис
работает, только `/purchase/start` отвечает 503.

### 1.5. Служба

```bash
cat > /etc/systemd/system/schoolpiboardoff.service <<'EOF'
[Unit]
Description=SchoolPiBoard license server
After=network.target postgresql.service

[Service]
WorkingDirectory=/var/www/schoolpiboardoff/api
ExecStart=/usr/bin/dotnet /var/www/schoolpiboardoff/api/Whiteboard.LicenseServer.dll
EnvironmentFile=/etc/schoolpiboardoff.env
Restart=always
RestartSec=5
User=www-data

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now schoolpiboardoff
systemctl is-active schoolpiboardoff
curl -s http://127.0.0.1:5080/health; echo
```

Схему базы сервис создаёт сам при первом запуске.

### 1.6. nginx и сертификат

```bash
cat > /etc/nginx/sites-available/keys.school-pi.online <<'EOF'
server {
    listen 80;
    server_name keys.school-pi.online;

    location / {
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
EOF

ln -s /etc/nginx/sites-available/keys.school-pi.online /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
certbot --nginx -d keys.school-pi.online --redirect -n
```

Проверка снаружи: `curl -s https://keys.school-pi.online/health`.

Если сам сервер отвечает `Could not resolve host`, а браузер телефона
страницу открывает — это кеш резолвера на сервере, на работу не влияет.
Проверить в обход:

```bash
curl -sS --resolve keys.school-pi.online:443:IP_СЕРВЕРА https://keys.school-pi.online/health
```

### 1.7. Робокасса

В кабинете магазина:

| Настройка | Значение |
|---|---|
| ResultURL | `https://keys.school-pi.online/payment/robokassa/result` |
| Метод ResultURL | POST |
| Алгоритм подписи | MD5 |
| SuccessURL | страница вашего сайта «спасибо, ключ отправлен» |
| FailURL | страница «оплата не прошла» |

Третий пароль (для JWT) относится к отдельному JWT-API Робокассы и этому
сервису не нужен — мы работаем по классическому протоколу с подписью MD5.
В `LICENSE_TOKEN_SECRET` его класть нельзя: это наш собственный секрет.

Если метод для SuccessURL и FailURL тоже POST, маршруты на сайте должны
принимать POST, иначе покупатель после оплаты увидит 405.

Затем на сервере:

```bash
cat >> /etc/schoolpiboardoff.env <<'EOF'
Robokassa__MerchantLogin=логин_магазина
ROBOKASSA_PASSWORD1=тестовый_пароль_1
ROBOKASSA_PASSWORD2=тестовый_пароль_2
Robokassa__IsTest=true
EOF

systemctl restart schoolpiboardoff
```

**Про тестовый режим.** При `IsTest=true` подпись считается тестовой парой
паролей, при `false` — боевой. Это разные пароли: переключая режим, меняйте
и их, иначе Робокасса ответит «неверная подпись».

### 1.8. Проверка

Тестовый ключ без оплаты:

```bash
cd /tmp
sudo -u postgres psql schoolpiboard_licenses -c "INSERT INTO licenses (id, key, email, created_at, revoked) VALUES (gen_random_uuid(), 'ABCD-EFGH-JKMN-PQRS', 'вы@почта', now(), false);"
```

Активация, лимит устройств и отказ третьему:

```bash
for d in TESTDEVICE0001 TESTDEVICE0002 TESTDEVICE0003; do
  printf "%s: " "$d"
  curl -sS -X POST https://keys.school-pi.online/license/activate \
    -H 'Content-Type: application/json' \
    -d "{\"key\":\"ABCD-EFGH-JKMN-PQRS\",\"hardwareId\":\"$d\"}" \
    | grep -o '"devicesUsed":[0-9]*\|"error":"[a-z_]*"' | tr '\n' ' '
  echo
done
```

Ожидается `1`, `2` и `device_limit`. После проверки освободите слоты, иначе
настоящее приложение упрётся в лимит:

```bash
sudo -u postgres psql schoolpiboard_licenses -c "DELETE FROM license_activations WHERE hardware_id LIKE 'TESTDEVICE%';"
```

### 1.9. Обновление сервиса

```bash
systemctl stop schoolpiboardoff
cd /tmp && rm -f ls.tar.gz
curl -sL -o ls.tar.gz https://github.com/pootymoty/SchoolPiBoard/releases/download/license-server-latest/license-server.tar.gz
rm -rf /var/www/schoolpiboardoff/api/*
tar -xzf ls.tar.gz -C /var/www/schoolpiboardoff/api
chown -R www-data:www-data /var/www/schoolpiboardoff
systemctl start schoolpiboardoff
```

База при этом не трогается, новые скрипты схемы применяются сами.
Журнал: `journalctl -u schoolpiboardoff -f`.

## Продукт 2. Офлайн-доска (exe на ПК)

### Шаг 2.1. Проверить адрес сервера `[ПК]`

`offline\LicenseState.cs` → `DefaultServerUrl` уже стоит
`https://keys.school-pi.online`. Если домен из шага 1.1 другой — поправьте
здесь: этот адрес зашивается в exe при сборке, и установленные копии будут
обращаться именно по нему.

### Шаг 2.2. Заменить оставшиеся заглушки `[ПК]`

Ищите слово `ЗАГЛУШКА`:

- `offline\installer\SchoolPiBoard.iss` → `AppPublisher` (ФИО)
- `offline\installer\LICENSE.txt` → реквизиты правообладателя
- `offline\web\index.html` → реквизиты в подвале (ФИО, ИНН) и текст
  раздела «Возврат»

Адрес сервера, ссылка на установщик и почта поддержки уже подставлены.

### Шаг 2.3. Поставить инструменты `[ПК]`

- .NET 8 SDK — https://dotnet.microsoft.com/download/dotnet/8.0
- Inno Setup 6 — https://jrsoftware.org/isdl.php

### Шаг 2.4. Собрать `[ПК]`

```
cd SchoolPiBoard\offline
installer\build-installer.bat
```

Результат: `offline\dist\SchoolPiBoardSetup.exe`.

Если нужно просто проверить приложение без установщика — `build.bat`,
результат в `offline\publish\SchoolPiBoard.exe`.

### Шаг 2.5. Установить и проверить `[ПК]`

Запустите установщик, пройдите мастер. На последней странице оставьте
галочку — откроется экран ввода ключа.

Что проверить:

1. Ключ из шага 1.9 принимается, приложение открывается.
2. Кнопка «Попробовать 3 дня» на чистой машине даёт пробный период.
3. Отключить интернет — приложение продолжает работать.
4. Настройки → «О программе и лицензии» показывают почту и «устройств 1 из 2».

### Шаг 2.6. Выложить установщик `[ПК → сервер]`

Положите `SchoolPiBoardSetup.exe` туда, куда ведёт ссылка со страницы
покупки (`License__DownloadUrl` из шага 1.5).

### Шаг 2.7. Страница покупки `[сайт]`

`offline\web\index.html` вставьте в свой сайт как обычную страницу.
Собирать её не надо.

---

## Продукт 3. Сайт онлайн-доски

Живёт в `/var/www/schoolpiboardon`. Нужны PostgreSQL **и** Redis.

### Шаг 3.1. DNS `[решение]`

A-запись `school-pi-board.online` → ваш сервер.

### Шаг 3.2. Подготовить сервер `[СЕРВЕР]`

```bash
sudo apt install -y redis-server
sudo systemctl enable --now redis-server

sudo -u postgres psql
CREATE DATABASE schoolpiboard_online OWNER schoolpi;
\q

sudo mkdir -p /var/www/schoolpiboardon/{api,web}
sudo chown -R www-data:www-data /var/www/schoolpiboardon
```

База отдельная от лицензионной — это разные продукты.

### Шаг 3.3. Пароль приложения Яндекс 360 `[решение]`

В Яндекс ID для `info@school-pi.online` создайте **пароль приложения**
для почты. Обычный пароль от ящика не подойдёт.

### Шаг 3.4. Собрать сервер `[ПК]`

```
cd SchoolPiBoard\online\server\SchoolPiBoard.Online
dotnet publish -c Release -o publish
```

### Шаг 3.5. Собрать сайт `[ПК]`

```
cd SchoolPiBoard\online\webapp
npm ci
set VITE_API_URL=/api
npm run build
```

В PowerShell вместо `set`: `$env:VITE_API_URL="/api"`.

**Не пропускайте `VITE_API_URL=/api`** — иначе сайт откроется, но не найдёт
сервер.

### Шаг 3.6. Отправить на сервер `[ПК]`

```
scp -r online\server\SchoolPiBoard.Online\publish\* user@server:/tmp/on-api/
scp -r online\webapp\dist\*                        user@server:/tmp/on-web/
```

```bash
# [СЕРВЕР]
sudo cp -r /tmp/on-api/* /var/www/schoolpiboardon/api/
sudo cp -r /tmp/on-web/* /var/www/schoolpiboardon/web/
sudo chown -R www-data:www-data /var/www/schoolpiboardon
```

### Шаг 3.7. Секреты `[СЕРВЕР]`

```bash
openssl rand -hex 32          # это AUTH_TOKEN_SECRET

sudo nano /etc/schoolpiboardon.env
```

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5081
ConnectionStrings__Postgres=Host=localhost;Database=schoolpiboard_online;Username=schoolpi;Password=ПАРОЛЬ_БАЗЫ
AUTH_TOKEN_SECRET=ТО_ЧТО_СГЕНЕРИРОВАЛИ
REDIS_CONNECTION_STRING=localhost:6379
Site__BaseUrl=https://school-pi-board.online
Site__AppOrigins__0=https://school-pi-board.online
Site__SupportEmail=info@school-pi.online
Smtp__Host=smtp.yandex.ru
Smtp__Port=465
Smtp__User=info@school-pi.online
Smtp__FromEmail=info@school-pi.online
SMTP_PASSWORD=ПАРОЛЬ_ПРИЛОЖЕНИЯ
Payments__MerchantLogin=ЛОГИН_МАГАЗИНА
ROBOKASSA_PASSWORD1=ПАРОЛЬ1
ROBOKASSA_PASSWORD2=ПАРОЛЬ2
Payments__IsTest=true
```

```bash
sudo chmod 600 /etc/schoolpiboardon.env
sudo chown root:root /etc/schoolpiboardon.env
```

### Шаг 3.8. Служба `[СЕРВЕР]`

```ini
# /etc/systemd/system/schoolpiboardon.service
[Unit]
Description=SchoolPiBoard online API
After=network.target postgresql.service redis-server.service

[Service]
WorkingDirectory=/var/www/schoolpiboardon/api
ExecStart=/usr/bin/dotnet /var/www/schoolpiboardon/api/SchoolPiBoard.Online.dll
EnvironmentFile=/etc/schoolpiboardon.env
Restart=always
RestartSec=5
User=www-data

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now schoolpiboardon
sudo journalctl -u schoolpiboardon -f
```

### Шаг 3.9. nginx `[СЕРВЕР]`

Полный конфиг — в `docs/deploy.md`. Главное, чего нельзя забыть:

- `try_files $uri $uri/ /index.html;` — иначе внутренние адреса сайта
  будут отдавать 404 при перезагрузке страницы;
- отдельный блок `/api/hub/` с заголовками `Upgrade` и `Connection` —
  без них живая доска не поднимет WebSocket.

```bash
sudo certbot --nginx -d school-pi-board.online
sudo nginx -t && sudo systemctl reload nginx

curl https://school-pi-board.online/api/health
```

### Шаг 3.10. ResultURL в кабинете Робокассы `[решение]`

`https://school-pi-board.online/api/billing/robokassa/result`

Это второй адрес — у десктопных ключей свой (шаг 1.8). Если магазин один,
уточните в поддержке Робокассы, как развести два продукта; при необходимости
проще завести второй магазин.

### Шаг 3.11. Проверить `[браузер]`

1. Открыть сайт — главная с кнопками «ВОЙТИ» и «ЗАРЕГИСТРИРОВАТЬСЯ».
2. Зарегистрироваться → письмо со ссылкой → подтвердить → войти.
3. Взять пробные 7 дней, создать доску.
4. Создать ссылку-приглашение, открыть её во втором браузере под другим
   пользователем — оба должны видеть друг друга в списке участников.
5. Тестовая оплата при `Payments__IsTest=true`, потом переключить на `false`.

---

## Что нужно решить до реальных продаж

1. **Чеки** — уточнить в Робокассе схему для самозанятого
   (`docs/payment-legal-notes.md`).
2. **Правовые тексты** — заглушки в `offline\installer\LICENSE.txt`,
   `offline\web\index.html` и на страницах `/legal/*` онлайн-доски.
3. **Капча** — включится, когда появится доступ к Yandex SmartCaptcha.
4. **Первая сборка** — ни один из серверов ещё не компилировался: в этом
   окружении нет .NET SDK. Первый `dotnet publish` может выявить опечатки;
   пришлите вывод, поправлю.
