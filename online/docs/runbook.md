# Пошаговый запуск сайта онлайн-доски

Живёт на отдельном домене, отдельным сервисом, с отдельной базой и своим
магазином Робокассы. От десктопной доски не зависит ничем и запускается
когда угодно.

Обозначения: `[ПК]` — команда на вашем компьютере, `[СЕРВЕР]` — на сервере
по ssh.

---

## Развёртывание

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
