# Развёртывание сервиса тарифов основного сайта

Маленький сервис на .NET 8, который ведёт подписки на тарифы ИИ-функций
school-pi.online (репозиторий `my_portfolio_project`, Flask). Своих
пользователей не заводит — только id с основного сайта, только оплата.

## Архитектура: кто что хранит

Три службы, три роли:

1. **Основной сайт (Flask)** — считает бесплатные попытки и расход
   токенов по своей таблице `AiUsage`, ничего не знает про Робокассу.
   Обращается только к этому сервису (`TARIFFS_API_URL` + `TARIFFS_API_KEY`).
2. **Этот сервис (SchoolPi.Tariffs)** — знает про тарифы, подписки,
   очередь и автопродление. Паролей Робокассы не держит — обращается к
   серверу ключей подписанными запросами (`TARIFFS_SHARED_SECRET`).
3. **Сервер ключей** (`offline/server/SchoolPiBoard.LicenseServer`) —
   единственный, кто знает пароли Робокассы. У тарифов там свой, третий
   магазин — отдельный от магазина лицензий и магазина подписок доски,
   ровно по той же причине: другой товар, другая оферта, другой сайт, и
   рекуррентные платежи Робокасса включает магазину, а не продавцу.

```
Flask ──(X-Api-Key)──> SchoolPi.Tariffs ──(подпись, TARIFFS_SHARED_SECRET)──> сервер ключей ──> Робокасса
                                          <──/callback (та же подпись)────────┘
```

## Что нужно на сервере

- .NET 8 ASP.NET Core Runtime (сборка идёт на GitHub, здесь только запуск)
- PostgreSQL — база `school_pi_tariffs`, отдельная от баз доски, сервера
  ключей и основного сайта

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
# Порт внутренний — наружу сервис не торчит вовсе, ни к нему, ни от него
# браузер не обращается напрямую.
Environment=ASPNETCORE_URLS=http://127.0.0.1:5090
Environment=ConnectionStrings__Postgres=Host=localhost;Database=school_pi_tariffs;Username=schoolpi_tariffs;Password=СЕКРЕТ
# Секрет между Flask-сайтом и этим сервисом.
Environment=TARIFFS_API_KEY=СЕКРЕТ
# Адрес и секрет сервера ключей — тот же секрет должен быть прописан там
# в Tariffs:SharedSecret / TARIFFS_SHARED_SECRET.
Environment=LicenseServer__Url=https://keys.school-pi.online
Environment=TARIFFS_SHARED_SECRET=СЕКРЕТ_С_СЕРВЕРОМ_КЛЮЧЕЙ

[Install]
WantedBy=multi-user.target
```

`TARIFFS_API_KEY` и `TARIFFS_SHARED_SECRET` — это **два разных секрета**
для двух разных связей: первый — с Flask-сайтом, второй — с сервером
ключей. Не путать и не использовать один и тот же.

Секреты в unit-файле видны любому, кто может его прочитать. Если это
неприемлемо — вынести их в `/etc/schoolpitariffs.env` с правами `600` и
подключить через `EnvironmentFile=`.

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now schoolpitariffs
sudo journalctl -u schoolpitariffs -f
```

Схема базы применяется при старте сервиса сама — отдельной команды нет.

## Что настроить на сервере ключей

Сервер ключей (`offline/server/SchoolPiBoard.LicenseServer`) уже
развёрнут — трогать нужно только его настройки, не код (код туда уже
попал этим же изменением, пересобрать и перезапустить как обычно):

```ini
# Третий магазин Робокассы — заводится в её личном кабинете отдельно
# от магазина лицензий и магазина подписок доски.
Environment=Robokassa__Tariffs__MerchantLogin=ЛОГИН_МАГАЗИНА_ТАРИФОВ
Environment=ROBOKASSA_TARIFFS_PASSWORD1=СЕКРЕТ
Environment=ROBOKASSA_TARIFFS_PASSWORD2=СЕКРЕТ

# Связь с этим сервисом — тот же секрет, что выше в LicenseServer__Url/
# TARIFFS_SHARED_SECRET на сервисе тарифов.
Environment=Tariffs__SharedSecret=СЕКРЕТ_С_СЕРВЕРОМ_КЛЮЧЕЙ
Environment=Tariffs__CallbackUrl=http://127.0.0.1:5090/callback
```

`Tariffs__CallbackUrl` указывает прямо на внутренний порт сервиса тарифов
(`127.0.0.1:5090`), если оба сервиса — на одной машине. Если на разных —
адрес снаружи, через nginx или прямой доступ по сети между серверами.

## Робокасса: что настроить в личном кабинете (магазин тарифов)

1. Технические настройки → Result URL:
   `https://ВАШ-ДОМЕН-СЕРВЕРА-КЛЮЧЕЙ/payment/robokassa/result`, метод POST
   (тот же адрес, что уже используют лицензии и доска — сервер ключей сам
   разбирает, какому магазину какой платёж принадлежит, по номеру счёта).
2. Success URL и Fail URL — на основной сайт, не на сервер ключей и не
   на этот сервис: ни один из них не показывает страниц человеку.
   `https://school-pi.online/billing/success` и `/billing/fail`.
3. Чек (54-ФЗ): самозанятому — `Robokassa:Tariffs:TaxSystem` пустым
   (берётся из кабинета магазина), `Tax=none`; `SendReceipt=true` — по
   умолчанию уже так.
4. **Рекуррентные платежи** — отдельная заявка в поддержку Робокассы на
   подключение автосписаний для этого магазина. Требования и как их
   выполнить — раздел ниже.

## Рекуррентные платежи: что нужно для заявки в Робокассу

Робокасса требует перед подключением:

- Публичную оферту с разделом об автосписаниях: период, сумма, порядок
  и дата списания, как отменить, как вернуть деньги, как меняется цена.
- Чекбокс согласия на форме оплаты, **не отмеченный по умолчанию**,
  текст «Я согласен на автоматические списания согласно условиям
  оферты», кликабельная ссылка на оферту, явно указанная периодичность.
- Историю согласий — реализована: `Payment.ConsentText` в
  `SchoolPi.Tariffs` хранит именно тот текст, что стоял рядом с галочкой
  на момент оплаты, а не факт «галочка была».
- Скриншоты двух страниц при подаче заявки: страницы оформления подписки
  (с галочкой) и страницы отмены автопродления в личном кабинете —
  обе есть на основном сайте (`/billing`).

Текст оферты и сама заявка в поддержку Робокассы — не код, здесь
подготовить нечего технически; когда текст будет готов, дайте знать —
подключу проверку кликабельной ссылки и периодичности на странице
`/billing`, если её там ещё не хватает.

## Порядок первого запуска

1. Создать базу и пользователя PostgreSQL для `school_pi_tariffs`.
2. Сгенерировать `TARIFFS_API_KEY` (сервис ↔ сайт) и `TARIFFS_SHARED_SECRET`
   (сервис ↔ сервер ключей) — два разных секрета, `openssl rand -hex 32`.
3. Завести третий магазин в Робокассе, вписать его логин/пароли в
   настройки сервера ключей.
4. Настроить Result/Success/Fail URL в кабинете Робокассы (см. выше).
5. Запустить оба сервиса (сервер ключей — пересобрать и перезапустить,
   сервис тарифов — развернуть впервые), проверить
   `curl http://127.0.0.1:5090/health`.
6. Провести тестовую оплату с `Robokassa:Tariffs:IsTest=true` на сервере
   ключей, свериться, что `/callback` действительно приходит и подписка
   активируется — потом выключить тестовый режим.
7. Подать заявку на рекуррентные платежи (раздел выше), уже на боевом
   режиме — тестовые оплаты рекуррентные списания не подтверждают.
