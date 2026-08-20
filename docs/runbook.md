# Пошаговый запуск трёх продуктов

Порядок между продуктами есть: десктопное приложение знает адрес своего
сервера ключей и зашивает его в exe. Поэтому **сначала сервер ключей,
потом приложение**. Сайт онлайн-доски ни от чего не зависит — его можно
делать когда угодно.

Обозначения: `[ПК]` — команда на вашем компьютере, `[СЕРВЕР]` — на сервере
по ssh.

---

## Продукт 1. Сервер ключей офлайн-доски

Живёт в `/var/www/schoolpiboardoff`. Нужен PostgreSQL, Redis не нужен.

### Шаг 1.1. Выбрать домен `[решение]`

Сервер должен быть доступен из интернета — к нему обращаются установленные
приложения. Например `keys.school-pi.online`. Заведите A-запись на сервер.

Этот адрес понадобится в шаге 2.1 — приложение зашивает его внутрь себя.

### Шаг 1.2. Подготовить сервер `[СЕРВЕР]`

```bash
# runtime .NET (SDK не нужен)
sudo apt update
sudo apt install -y aspnetcore-runtime-8.0

# база
sudo -u postgres psql
CREATE USER schoolpi WITH PASSWORD 'ПРИДУМАЙТЕ_ПАРОЛЬ';
CREATE DATABASE schoolpiboard_licenses OWNER schoolpi;
\q

sudo mkdir -p /var/www/schoolpiboardoff/api
sudo chown -R www-data:www-data /var/www/schoolpiboardoff
```

### Шаг 1.3. Собрать `[ПК]`

```
cd SchoolPiBoard\offline\server\Whiteboard.LicenseServer
dotnet publish -c Release -o publish
```

### Шаг 1.4. Отправить на сервер `[ПК]`

```
scp -r publish\* user@server:/tmp/lic/
```

```bash
# [СЕРВЕР]
sudo cp -r /tmp/lic/* /var/www/schoolpiboardoff/api/
sudo chown -R www-data:www-data /var/www/schoolpiboardoff
```

### Шаг 1.5. Секреты `[СЕРВЕР]`

```bash
openssl rand -hex 32          # запишите — это LICENSE_TOKEN_SECRET

sudo nano /etc/schoolpiboardoff.env
```

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
ConnectionStrings__Postgres=Host=localhost;Database=schoolpiboard_licenses;Username=schoolpi;Password=ПАРОЛЬ_БАЗЫ
LICENSE_TOKEN_SECRET=ТО_ЧТО_СГЕНЕРИРОВАЛИ
License__DownloadUrl=https://school-pi.online/download/WhiteboardSetup.exe
License__SupportEmail=info@school-pi.online
Robokassa__MerchantLogin=ЛОГИН_МАГАЗИНА
ROBOKASSA_PASSWORD1=ПАРОЛЬ1
ROBOKASSA_PASSWORD2=ПАРОЛЬ2
Robokassa__IsTest=true
Web__SiteUrl=https://school-pi.online/whiteboard
```

```bash
sudo chmod 600 /etc/schoolpiboardoff.env
sudo chown root:root /etc/schoolpiboardoff.env
```

> **Письма с ключами пока не уйдут.** Этот сервис писался под SendGrid,
> а у вас Яндекс 360. Пока переменные SendGrid не заданы, ключ выпускается
> и пишется в лог, но покупателю не отправляется. Надо переключить сервис
> на тот же SMTP, что и у онлайн-доски — работы немного.

### Шаг 1.6. Служба `[СЕРВЕР]`

```bash
sudo nano /etc/systemd/system/schoolpiboardoff.service
```

```ini
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
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now schoolpiboardoff
sudo journalctl -u schoolpiboardoff -f      # тут видно, поднялся ли он
```

Схему базы сервис создаёт сам при первом запуске.

### Шаг 1.7. nginx и сертификат `[СЕРВЕР]`

```nginx
server {
    listen 443 ssl http2;
    server_name keys.school-pi.online;

    ssl_certificate     /etc/letsencrypt/live/keys.school-pi.online/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/keys.school-pi.online/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

```bash
sudo certbot --nginx -d keys.school-pi.online
sudo nginx -t && sudo systemctl reload nginx

curl https://keys.school-pi.online/health     # ждём {"status":"ok"}
```

### Шаг 1.8. ResultURL в кабинете Робокассы `[решение]`

`https://keys.school-pi.online/webhook/robokassa` — адрес, по которому
приходит уведомление об оплате десктопной лицензии.

### Шаг 1.9. Тестовый ключ `[СЕРВЕР]`

Чтобы проверить активацию, не проводя оплату:

```sql
sudo -u postgres psql schoolpiboard_licenses
INSERT INTO licenses (id, key, email, created_at, revoked)
VALUES (gen_random_uuid(), 'ABCD-EFGH-JKMN-PQRS', 'вы@почта', now(), false);
```

---

## Продукт 2. Офлайн-доска (exe на ПК)

### Шаг 2.1. Проверить адрес сервера `[ПК]`

`offline\LicenseState.cs` → `DefaultServerUrl` уже стоит
`https://keys.school-pi.online`. Если домен из шага 1.1 другой — поправьте
здесь: этот адрес зашивается в exe при сборке, и установленные копии будут
обращаться именно по нему.

### Шаг 2.2. Заменить оставшиеся заглушки `[ПК]`

Ищите слово `ЗАГЛУШКА`:

- `offline\installer\Whiteboard.iss` → `AppPublisher` (ФИО)
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

Результат: `offline\dist\WhiteboardSetup.exe`.

Если нужно просто проверить приложение без установщика — `build.bat`,
результат в `offline\publish\Whiteboard.exe`.

### Шаг 2.5. Установить и проверить `[ПК]`

Запустите установщик, пройдите мастер. На последней странице оставьте
галочку — откроется экран ввода ключа.

Что проверить:

1. Ключ из шага 1.9 принимается, приложение открывается.
2. Кнопка «Попробовать 3 дня» на чистой машине даёт пробный период.
3. Отключить интернет — приложение продолжает работать.
4. Настройки → «О программе и лицензии» показывают почту и «устройств 1 из 2».

### Шаг 2.6. Выложить установщик `[ПК → сервер]`

Положите `WhiteboardSetup.exe` туда, куда ведёт ссылка со страницы
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

1. **Письма с ключами десктопной версии** — переключить сервер лицензий
   с SendGrid на SMTP Яндекс 360 (шаг 1.5).
2. **Чеки** — уточнить в Робокассе схему для самозанятого
   (`docs/payment-legal-notes.md`).
3. **Правовые тексты** — заглушки в `offline\installer\LICENSE.txt`,
   `offline\web\index.html` и на страницах `/legal/*` онлайн-доски.
4. **Капча** — включится, когда появится доступ к Yandex SmartCaptcha.
5. **Первая сборка** — ни один из серверов ещё не компилировался: в этом
   окружении нет .NET SDK. Первый `dotnet publish` может выявить опечатки;
   пришлите вывод, поправлю.
