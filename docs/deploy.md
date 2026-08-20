# Развёртывание на сервере

Два независимых проекта, две папки, два процесса, две базы.

| Проект | Папка на сервере | Домен |
|---|---|---|
| Десктопная доска: сервер лицензий | `/var/www/schoolpiboardoff` | обслуживает установленные приложения |
| Онлайн-доска: сайт и API | `/var/www/schoolpiboardon` | school-pi-board.online |

## Что нужно на сервере

- .NET 8 ASP.NET Core Runtime (для сборки — SDK, но публиковать можно и локально)
- PostgreSQL — **две отдельные базы**, `schoolpiboard_licenses` и `schoolpiboard_online`
- Redis — для онлайн-доски (SignalR и список присутствующих)
- nginx с сертификатами Let's Encrypt

## Онлайн-доска

### Раскладка

```
/var/www/schoolpiboardon/
  api/   <- dotnet publish -c Release из online/server/SchoolPiBoard.Online
  web/   <- содержимое online/webapp/dist
```

### Сборка

```bash
# на машине разработчика
cd online/server/SchoolPiBoard.Online
dotnet publish -c Release -o ./publish

cd ../../webapp
npm ci
VITE_API_URL=/api npm run build

# на сервер
rsync -a online/server/SchoolPiBoard.Online/publish/ user@server:/var/www/schoolpiboardon/api/
rsync -a online/webapp/dist/            user@server:/var/www/schoolpiboardon/web/
```

`VITE_API_URL=/api` — ключевой момент. API и сайт живут на одном домене,
nginx проксирует путь `/api` в приложение. Тогда браузер считает запросы
своими, и CORS не нужен вовсе — меньше настроек и нечему ломаться.

### systemd: `/etc/systemd/system/schoolpiboardon.service`

```ini
[Unit]
Description=SchoolPiBoard online API
After=network.target postgresql.service redis-server.service

[Service]
WorkingDirectory=/var/www/schoolpiboardon/api
ExecStart=/usr/bin/dotnet /var/www/schoolpiboardon/api/SchoolPiBoard.Online.dll
Restart=always
RestartSec=5
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5081
Environment=ConnectionStrings__Postgres=Host=localhost;Database=schoolpiboard_online;Username=schoolpiboard;Password=СЕКРЕТ
Environment=AUTH_TOKEN_SECRET=СЕКРЕТ
Environment=REDIS_CONNECTION_STRING=localhost:6379
Environment=Site__BaseUrl=https://school-pi-board.online
Environment=Site__AppOrigins__0=https://school-pi-board.online
Environment=Smtp__Host=smtp.yandex.ru
Environment=Smtp__Port=465
Environment=Smtp__User=info@school-pi.online
Environment=Smtp__FromEmail=info@school-pi.online
Environment=SMTP_PASSWORD=ПАРОЛЬ_ПРИЛОЖЕНИЯ
Environment=Payments__MerchantLogin=ЛОГИН_МАГАЗИНА
Environment=ROBOKASSA_PASSWORD1=СЕКРЕТ
Environment=ROBOKASSA_PASSWORD2=СЕКРЕТ

[Install]
WantedBy=multi-user.target
```

Секреты в unit-файле видны любому, кто может его прочитать. Если это
неприемлемо — вынести их в `/etc/schoolpiboardon.env` с правами `600`
и подключить через `EnvironmentFile=`.

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now schoolpiboardon
sudo journalctl -u schoolpiboardon -f
```

Схема базы применяется при старте сервиса сама — отдельной команды нет.

### nginx

```nginx
server {
    listen 443 ssl http2;
    server_name school-pi-board.online;

    ssl_certificate     /etc/letsencrypt/live/school-pi-board.online/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/school-pi-board.online/privkey.pem;

    root /var/www/schoolpiboardon/web;
    index index.html;

    # Приложение одностраничное: любой адрес отдаём index.html,
    # маршрутизацией занимается сам сайт.
    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://127.0.0.1:5081/;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Доска работает по WebSocket — без этих двух заголовков соединение
    # не поднимется, и SignalR молча свалится на длинный опрос.
    location /api/hub/ {
        proxy_pass http://127.0.0.1:5081/hub/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade    $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host       $host;
        proxy_set_header X-Real-IP  $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 1h;
    }
}

server {
    listen 80;
    server_name school-pi-board.online;
    return 301 https://$host$request_uri;
}
```

Приложение стоит за nginx, поэтому в нём включён `UseForwardedHeaders`.
Чтобы ограничитель частоты видел настоящие адреса клиентов, а не адрес
прокси, укажите доверенные адреса в `ForwardedHeadersOptions.KnownProxies`
(по умолчанию доверяются только петлевые адреса — при `proxy_pass` на
127.0.0.1 этого достаточно).

## Сервер лицензий десктопной версии

Раскладка проще: только API.

```
/var/www/schoolpiboardoff/
  api/   <- dotnet publish -c Release из offline/server/Whiteboard.LicenseServer
```

systemd — такой же unit с другим портом (например, 5080), своей базой
`schoolpiboard_licenses` и своими переменными (`LICENSE_TOKEN_SECRET`,
`Smtp__*` с `SMTP_PASSWORD`, `ROBOKASSA_PASSWORD1/2`). Redis ему не нужен.
Почта у обоих сервисов одна и та же — Яндекс 360, ящик `info@school-pi.online`.

nginx проксирует его на том домене, который прописан в десктопном
приложении (`LicenseOptions.DefaultServerUrl`) — этот адрес должен
совпадать с реальным, иначе установленные копии не достучатся.

## Порядок первого запуска

1. Создать базы и пользователей PostgreSQL.
2. Поднять Redis.
3. Выпустить сертификаты, настроить nginx.
4. Запустить сервисы, проверить `curl https://school-pi-board.online/api/health`.
5. Зарегистрироваться на сайте и убедиться, что письмо дошло.
6. Провести тестовую оплату с `Payments__IsTest=true`.
