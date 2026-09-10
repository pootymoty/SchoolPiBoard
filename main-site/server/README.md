# SchoolPi.Tariffs

Сервис тарифов основного сайта (school-pi.online, репозиторий
`my_portfolio_project`, Flask) — подписки на ИИ-функции.

Паролей Робокассы **не держит вовсе**. Платёжное целиком живёт на
сервере ключей (`offline/server/SchoolPiBoard.LicenseServer` — том же,
что уже продаёт лицензии на офлайн-доску и подписки на онлайн-доску): у
тарифов там свой, третий магазин Робокассы
(`Robokassa:Tariffs:*` / `ROBOKASSA_TARIFFS_PASSWORD1/2`), с отдельно
подключаемыми рекуррентными платежами.

Держит:

- каталог тарифов (`Configuration/TariffOptions.cs`, `TariffPlans`);
- подписку пользователя основного сайта (по его же числовому id,
  собственных учётных записей здесь нет);
- очередь из не более чем одной отложенной покупки поверх действующей;
- автопродление как свойство аккаунта (переезжает на новую покупку,
  отключается кнопкой в личном кабинете);
- журнал согласий на автосписание (`Payment.ConsentText`).

Не держит: пароли Робокассы, почту (пока — см. TODO в
`Services/AutoRenewService.cs`), лимиты токенов, счётчик бесплатных
попыток — их считает сам основной сайт по своей таблице `AiUsage`.

Развёртывание — `../docs/deploy.md`. Устройство —
`Services/SubscriptionService.cs`, `Services/AutoRenewService.cs`,
`Services/LicenseServerClient.cs` — все три являются адаптированными
копиями `SubscriptionService.cs`/`AutoRenewService.cs`/`KeyServerClient.cs`
из `schoolpiboard_online`, под один календарный месяц вместо четырёх
сроков доски и без своей системы пользователей.

## API

Все пути, кроме `/health`, `/plans` и `/callback`, требуют заголовок
`X-Api-Key` с секретом, общим с основным сайтом (`TARIFFS_API_KEY`).

- `GET /health` — жив ли сервис.
- `GET /plans` — каталог тарифов (ключ, название, цена, срок в днях).
- `GET /status/{externalUserId}` — действующая подписка и отложенная
  покупка про запас (`upcoming`), если есть.
- `POST /checkout` `{externalUserId, email, plan, autoRenew, consent}` →
  `{paymentUrl}`. `consent` — текст согласия на автосписание, показанный
  человеку рядом с галочкой; сохраняется в `Payment.ConsentText`.
- `POST /auto-renew/cancel` `{externalUserId}` — отключает автопродление
  у текущей подписки. Включить обратно можно только новой покупкой.
- `POST|GET /callback` — сообщение сервера ключей об оплате. Защищено
  подписью с `TARIFFS_SHARED_SECRET`, а не ключом сайта: это разговор
  двух своих служб, а не сайта.
