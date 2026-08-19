# Whiteboard License Server

Минимальный сервис лицензий для десктопной версии Whiteboard:
выпускает ключ после оплаты, привязывает его максимум к двум устройствам
и отвечает клиенту на периодическую проверку.

Стек: ASP.NET Core 8 (minimal API) + PostgreSQL (EF Core / Npgsql).

## Модель лицензии

- Разовая оплата → один перманентный ключ формата `XXXX-XXXX-XXXX-XXXX`, без срока действия.
- Ключ живёт максимум на **2 устройствах** одновременно (значение настраивается: `License:DeviceLimit`).
- Клиент проверяется онлайн при активации и раз в сутки в фоне; без интернета
  приложение работает ещё 14 дней (грейс-период считает клиент).

## Запуск

```bash
cd server/Whiteboard.LicenseServer

export ConnectionStrings__Postgres="Host=localhost;Database=whiteboard_licenses;Username=postgres;Password=postgres"
export LICENSE_TOKEN_SECRET="$(openssl rand -hex 32)"
export STRIPE_WEBHOOK_SECRET="whsec_..."
export SENDGRID_API_KEY="SG...."
export SendGrid__FromEmail="noreply@ваш-домен"
export License__DownloadUrl="https://ваш-домен/download/Whiteboard.exe"

dotnet run
```

Схема базы применяется при старте (`sql/001_init.sql`, идемпотентный скрипт),
отдельная команда миграции не нужна.

### Переменные окружения

| Переменная | Обязательна | Зачем |
|---|---|---|
| `ConnectionStrings__Postgres` | да | подключение к PostgreSQL |
| `LICENSE_TOKEN_SECRET` | да | секрет подписи токенов активации (HMAC-SHA256) |
| `STRIPE_WEBHOOK_SECRET` | в Production | проверка подписи вебхука Stripe |
| `SENDGRID_API_KEY` | в Production | отправка письма с ключом |
| `SendGrid__FromEmail` | в Production | адрес отправителя, подтверждённый в SendGrid |
| `License__DownloadUrl` | нет | ссылка на EXE в письме |
| `License__SupportEmail` | нет | адрес поддержки в письме |
| `License__DeviceLimit` | нет | сколько устройств на ключ (по умолчанию 2) |

В режиме Development сервис поднимается и без Stripe/SendGrid: подпись вебхука
не проверяется, а письмо пишется в лог. В Production отсутствие любого из
секретов — ошибка старта, чтобы сервис не работал «наполовину».

## Эндпоинты

### `POST /license/activate`

```json
{ "key": "ABCD-EFGH-JKMN-PQRS", "hardwareId": "8F2C…" }
```

| Ответ | Когда |
|---|---|
| `200 { token, key, email, activatedAt, devicesUsed, deviceLimit }` | слот занят этим устройством (в том числе повторно) |
| `400 { error: "bad_request" }` | ключ не той длины/формата |
| `403 { error: "invalid_key" }` | ключа нет или он отозван |
| `409 { error: "device_limit", devicesUsed, deviceLimit }` | все слоты заняты другими устройствами |

`token` — JWT (HS256) без даты истечения, с полем `issuedAt`. Клиент хранит его
как есть и не разбирает; подпись проверяет сервер.

Проверка лимита выполняется в транзакции с `SELECT … FOR UPDATE` по строке
лицензии — два одновременных запроса не смогут занять третий слот.

### `POST /license/validate`

Тело то же. Всегда `200`:

```json
{ "valid": true, "reason": "ok", "devicesUsed": 1, "deviceLimit": 2 }
```

Отдельный код ошибки здесь был бы вреден: клиенту важно отличать «сервер сказал
нет» (блокировать приложение) от «до сервера не достучались» (продолжать работу).

### `POST /license/deactivate`

Освобождает слот устройства — нужно при переезде на новый компьютер.
`200 { ok: true }`, если слот освобождён или его уже не было.

### `POST /webhook/stripe`

Принимает `checkout.session.completed` и `payment_intent.succeeded`, выпускает
ключ и синхронно отправляет письмо через SendGrid.

- Подпись проверяется по схеме Stripe (`t=…,v1=…`, HMAC-SHA256, окно 5 минут).
- В базе хранится **только SHA-256 от payment intent id** — ни идентификатора
  платежа, ни тем более карточных данных.
- Повторная доставка того же события не создаёт вторую лицензию (уникальный
  индекс по хешу платежа), а если письмо в прошлый раз не ушло — сервис вернёт
  `500`, и Stripe повторит попытку.

### `GET /health`

`200 { "status": "ok" }`.

## Ограничение частоты

Fixed window по IP-адресу: `/license/activate` и `/license/deactivate` —
10 запросов в минуту, `/license/validate` — 60. Превышение → `429`.

За обратным прокси включите `UseForwardedHeaders` с указанием доверенных
адресов (`KnownProxies`/`KnownNetworks`), иначе все клиенты будут выглядеть
как один IP прокси и делить общий лимит.

## Эксплуатация

Отозвать ключ (клиент заблокируется при следующей фоновой проверке):

```sql
UPDATE licenses SET revoked = true WHERE key = 'ABCD-EFGH-JKMN-PQRS';
```

Освободить слот руками:

```sql
DELETE FROM license_activations
 WHERE license_id = (SELECT id FROM licenses WHERE key = 'ABCD-EFGH-JKMN-PQRS');
```

Выпустить тестовый ключ без Stripe (PostgreSQL 13+):

```sql
INSERT INTO licenses (id, key, email, created_at, revoked)
VALUES (gen_random_uuid(), 'ABCD-EFGH-JKMN-PQRS', 'test@example.com', now(), false);
```

В ключе используются только символы `ABCDEFGHJKMNPQRSTUVWXYZ23456789` —
без `I`, `L`, `O`, `0` и `1`, чтобы ключ можно было продиктовать по телефону.

## Проверка связки с клиентом

Адрес сервера в десктопном приложении берётся из переменной окружения
`WHITEBOARD_LICENSE_URL` (перекрывает всё), иначе из `LicenseServerUrl`
в `%APPDATA%\WhiteboardApp\settings.json`.

```powershell
$env:WHITEBOARD_LICENSE_URL = "http://localhost:5000"
.\Whiteboard.exe
```

## Чего здесь намеренно нет

- Личного кабинета и админки: отозвать ключ и освободить слот проще запросом в базу.
- Очереди писем: одно письмо на оплату, синхронной отправки с повтором от Stripe достаточно.
- Защиты от подбора ключей с распределённого ботнета: ограничитель работает по IP.
  Перебор 16 символов из алфавита в 31 знак (~79 бит) и так не имеет смысла.
