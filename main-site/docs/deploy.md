# Развёртывание сервиса тарифов основного сайта

Маленький сервис на .NET 8, который ведёт подписки на тарифы ИИ-функций
school-pi.online (репозиторий `my_portfolio_project`, Flask). Своих
пользователей не заводит — только id с основного сайта, только оплата.

## Почему отдельный сервис, а не код на самом сайте

У основного сайта уже был принцип: пароли Робокассы там не хранятся
(`my_app/routes/payment.py` в `my_portfolio_project` — покупка ключей
десктопной доски делегирована серверу ключей ровно по этой причине).
Этот сервис — второе применение того же принципа, теперь для тарифов
основного сайта, по образцу `online/server` (биллинг онлайн-доски).

## Что нужно на сервере

- .NET 8 ASP.NET Core Runtime (сборка идёт на GitHub, здесь только запуск)
- PostgreSQL — база `school_pi_tariffs`, отдельная от баз доски и сервера
  ключей
- nginx с прокси на порт сервиса

## Раскладка

```
/var/www/schoolpitariffs/api/   <- архив tariffs-server-latest
```

## Сборка и доставка

Собирается на GitHub Actions (`.github/workflows/tariffs-server.yml`) при
пуше в `main-site/server/**`. На сервере — просто скачать готовое:

```bash
curl -sL -o ts.tar.gz https://github.com/pootymoty/SchoolPiBoard/releases/download/tariffs-server-latest/tariffs-server.tar.gz
sudo mkdir -p /var/www/schoolpitariffs/api
sudo tar -xzf ts.tar.gz -C /var/www/schoolpitariffs/api
```

## systemd: `/etc/systemd/system/schoolpitariffs.service`

```ini
[Unit]
Description=SchoolPi tariffs API
After=network.target postgresql.service

[Service]
WorkingDirectory=/var/www/schoolpitariffs/api
ExecStart=/usr/bin/dotnet /var/www/schoolpitariffs/api/SchoolPi.Tariffs.dll
Restart=always
RestartSec=5
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
# Порт внутренний — наружу сервис не торчит, только через nginx (или
# вообще только по localhost, раз наружу его дёргает лишь Робокасса).
Environment=ASPNETCORE_URLS=http://127.0.0.1:5090
Environment=ConnectionStrings__Postgres=Host=localhost;Database=school_pi_tariffs;Username=schoolpi_tariffs;Password=СЕКРЕТ
Environment=TARIFFS_API_KEY=СЕКРЕТ
Environment=Payments__MerchantLogin=ЛОГИН_МАГАЗИНА
Environment=ROBOKASSA_PASSWORD1=СЕКРЕТ
Environment=ROBOKASSA_PASSWORD2=СЕКРЕТ
```

`TARIFFS_API_KEY` — общий секрет с основным сайтом. Сгенерировать одной
командой (`openssl rand -hex 32`) и вписать сюда же и в `.env` основного
сайта как `TARIFFS_API_KEY` — обе стороны должны знать один и тот же
ключ. Если реквизиты Робокассы для этого магазина отличаются от тех, что
уже настроены на доске или сервере ключей — используйте свои.

Секреты в unit-файле видны любому, кто может его прочитать. Если это
неприемлемо — вынести их в `/etc/schoolpitariffs.env` с правами `600` и
подключить через `EnvironmentFile=`, как рекомендовано и для
`online/server`.

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now schoolpitariffs
sudo journalctl -u schoolpitariffs -f
```

Схема базы применяется при старте сервиса сама — отдельной команды нет.

## nginx

Сервис не отдаёт ничего браузеру напрямую, кроме одного адреса —
`/robokassa/result`, куда стучится сама Робокасса. Проще всего проксировать
его под путь на основном домене, например `/tariffs-api/`:

```nginx
location /tariffs-api/ {
    proxy_pass http://127.0.0.1:5090/;
    proxy_set_header Host              $host;
    proxy_set_header X-Real-IP         $remote_addr;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

Тогда ResultURL в настройках магазина Робокассы (в личном кабинете
Робокассы, не в коде) — `https://school-pi.online/tariffs-api/robokassa/result`.

## Робокасса: что настроить в личном кабинете

1. Технические настройки → Result URL:
   `https://school-pi.online/tariffs-api/robokassa/result`, метод POST.
2. Success URL и Fail URL — на сам сайт, не на этот сервис: он не
   показывает страниц человеку, только считает деньги.
   `https://school-pi.online/billing/success` и `/billing/fail`
   (маршруты основного сайта — их подтверждать подписью не нужно,
   реальное включение подписки идёт по Result URL, а эти страницы —
   только «спасибо, проверяем»).
3. Чек (54-ФЗ): если продавец — самозанятый, `Payments:TaxSystem=npd`,
   `Payments:Tax=none` уже так настроены по умолчанию; включается сам
   чек флагом `Payments:SendReceipt=true`, когда подключена онлайн-касса
   в личном кабинете Робокассы (или через её партнёра-ОФД).

## Порядок первого запуска

1. Создать базу и пользователя PostgreSQL.
2. Сгенерировать `TARIFFS_API_KEY`, вписать в unit-файл сервиса и в
   `.env` основного сайта.
3. Настроить nginx-проксирование и Result/Success/Fail URL в кабинете
   Робокассы (см. выше).
4. Запустить сервис, проверить
   `curl http://127.0.0.1:5090/health` и
   `curl https://school-pi.online/tariffs-api/plans`.
5. Провести тестовую оплату с `Payments__IsTest=true`, свериться, что
   `/robokassa/result` действительно приходит и подписка активируется —
   потом выключить тестовый режим.
