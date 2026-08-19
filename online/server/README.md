# SchoolPiBoard — сервер онлайн-доски

ASP.NET Core 8 (minimal API + SignalR), PostgreSQL, Redis.
Бэкенд сайта school-pi-board.online.

**С сервером лицензий десктопной версии (`offline/server`) не связан ничем:**
своя база, свои учётные записи, свои настройки и свой домен. Общего кода нет
намеренно — это разные продукты с разной аудиторией.

## Запуск

```bash
cd online/server/SchoolPiBoard.Online

export ConnectionStrings__Postgres="Host=localhost;Database=schoolpiboard_online;Username=postgres;Password=postgres"
export AUTH_TOKEN_SECRET="$(openssl rand -hex 32)"
export REDIS_CONNECTION_STRING="localhost:6379"
export SMTP_PASSWORD="..."
export CAPTCHA_SECRET_KEY="..."
export ROBOKASSA_PASSWORD1="..."
export ROBOKASSA_PASSWORD2="..."

dotnet run
```

Схема применяется при старте (`sql/*.sql`, идемпотентные скрипты).

### Переменные окружения

| Переменная | Обязательна | Зачем |
|---|---|---|
| `ConnectionStrings__Postgres` | да | подключение к PostgreSQL |
| `AUTH_TOKEN_SECRET` | да | подпись токенов входа |
| `REDIS_CONNECTION_STRING` | в Production | backplane SignalR и присутствие участников |
| `Smtp__Host`, `Smtp__FromEmail`, `Smtp__User`, `SMTP_PASSWORD` | в Production | письма подтверждения |
| `Captcha__Provider=yandex`, `CAPTCHA_SECRET_KEY` | в Production | защита регистрации |
| `Payments__MerchantLogin`, `ROBOKASSA_PASSWORD1`, `ROBOKASSA_PASSWORD2` | для оплаты | подписка |
| `Site__BaseUrl` | да | из него собираются ссылки в письмах |
| `Site__AppOrigins__0` | да | домен веб-приложения (CORS + WebSocket) |
| `Invites__LinkLifetimeDays` | нет | срок жизни ссылки-приглашения, по умолчанию 7 |
| `Invites__EditDaysAfterJoin` | нет | сколько дней вошедший по ссылке может править, по умолчанию 14 |

В Development сервис поднимается без SMTP, капчи и Redis: письма пишутся
в лог, капча не проверяется, присутствие живёт в памяти процесса.
В Production отсутствие любого из этих значений — ошибка старта, чтобы
сервис не работал «наполовину».

## Регистрация и вход

```
POST /auth/register  { lastName, firstName, birthDate, email, password, passwordConfirm, captchaToken }
POST /auth/confirm   { token }
POST /auth/login     { email, password }
GET  /auth/me
```

Учётная запись появляется **только после подтверждения почты**. До этого
данные лежат в `pending_registrations` и через час перестают действовать —
регистрацию нужно проходить заново. Так и написано в ТЗ, и это же избавляет
от мусорных учётных записей.

В базе хранится только хеш кода из письма: утечка таблицы не даёт подтвердить
чужую почту. Пароли — PBKDF2-HMAC-SHA256, 210 000 итераций.

При входе хеш пароля проверяется даже для незарегистрированной почты — иначе
по времени ответа было бы видно, есть такой адрес или нет.

## Подписка

```
GET  /billing/plans
GET  /billing/status
POST /billing/trial
POST /billing/checkout    { planDays }
POST /billing/auto-renew  { enabled }
POST /billing/cancel
POST /billing/robokassa/result     (ResultURL платёжной системы)
```

Тарифы: 30 дней — 499 ₽, 90 — 1449 ₽, 180 — 2799 ₽, 365 — 5399 ₽.
Пробный период — 7 дней, один раз на учётную запись.

Продление добавляет дни к остатку, а не обнуляет его: человек платит за срок,
а не за дату. Отмена подписки прекращает продления, но доступ сохраняется
до конца оплаченного срока — деньги за него уже взяты.

Оплата идёт через Робокассу: она доступна самозанятому продавцу в России,
в отличие от Stripe. Об оплате сервис узнаёт от платёжной системы по
ResultURL, а не от браузера.

**Не сделано:** реальное автосписание. Флаг `auto_renew` хранится и
переключается, но чтобы деньги списывались сами, нужен рекуррентный
интерфейс платёжной системы — его подключение впереди.

## Доски, участники, приглашения

```
GET    /boards?page=1&pageSize=10
POST   /boards                                { name }
GET    /boards/{id}
DELETE /boards/{id}
GET    /boards/{id}/members
POST   /boards/{id}/members                   { email, role }
PATCH  /boards/{id}/members/{userId}          { role }
DELETE /boards/{id}/members/{userId}
GET    /boards/{id}/invites
POST   /boards/{id}/invites                   { role, lifetimeDays, editDays }
DELETE /boards/{id}/invites/{inviteId}
GET    /invites/{token}                       (что за доска — видно и без входа)
POST   /invites/{token}/join
```

Роли: `owner`, `editor`, `viewer`. Подписка нужна только владельцу и только
для создания досок; приглашённым — нет.

### Как ограничено расползание ссылки

Ссылка живёт ограниченное время (по умолчанию 7 дней) и может быть отозвана
владельцем в любой момент. Тот, кто вошёл по ссылке, может **менять** доску
тоже ограниченный срок (по умолчанию 14 дней): дальше доска остаётся у него
в списке, но только для просмотра.

Это ровно то поведение, которое просили: ссылка, ушедшая в общий чат,
не превращается в вечное право редактирования. Личное приглашение по почте
такого ограничения не имеет — владелец звал конкретного человека.

Хранится хеш ссылки, поэтому саму ссылку сервер показывает один раз,
при создании.

## Комната доски: `/hub/board` (SignalR)

```
Клиент -> сервер:   JoinBoard(boardId), LeaveBoard(boardId), CursorMove(boardId, x, y)
Сервер -> клиентам: UserJoined, UserLeft, CursorMoved
```

`JoinBoard` возвращает состояние комнаты целиком — тот же вызов используется
после переподключения, поэтому клиенту не нужно «догонять» пропущенные события.

Роль проверяется в хабе при каждом обращении, тем же методом, что и в REST.

## Профиль

```
PATCH /profile                { lastName, firstName }
POST  /profile/password       { currentPassword, newPassword, confirmPassword }
POST  /profile/delete-request
POST  /profile/delete-confirm { token }
```

Удаление — в два шага, через ссылку из письма. Вместе с учётной записью
удаляются её доски и участие в чужих, подписка помечается отменённой,
автопродление снимается.
