# Карта репозитория и развёртывания

Этот файл — для любого, кто открывает репозиторий впервые: для вас самого
через полгода, для другого человека, для ИИ-ассистента в отдельном чате
(Claude, ChatGPT, Cursor и т.п.), который ничего не помнит из прошлых
разговоров. Задача файла — дать полную картину без необходимости
перечитывать весь код: что здесь лежит, как это связано, куда и как это
доставляется, какие команды для этого нужны.

Более подробные документы по каждой части перечислены в конце. Этот файл —
не замена им, а вход в них: короткая, но полная навигация.

**Если вы ИИ-ассистент и читаете это впервые** — раздел
[«Памятка для ИИ-ассистента»](#памятка-для-ии-ассистента) в конце написан
прямо для вас, начните с него после общей картины.

---

## 1. Что здесь: три независимых продукта

Общего кода между ними нет **намеренно**: разные домены, разные базы
данных, разные учётные записи покупателей, разные магазины Робокассы.
Соединяет их только тема (образовательная доска), автор и то, что все три
живут в одном git-репозитории ради удобства.

| Папка | Продукт | Домен | Модель продажи |
|---|---|---|---|
| `offline/` | Десктопное приложение «Доска Пи» под Windows | страница покупки на `school-pi.online` | разовый платёж, бессрочный ключ |
| `online/` | Онлайн-доска для совместной работы в браузере | `school-pi-board.online` | подписка |
| `main-site/` | Сервис тарифов ИИ-функций основного сайта | нет своего — внутренний, за Flask-сайтом | подписка |

Четвёртый участник, которого **в этом репозитории нет**: сам основной
сайт `school-pi.online` — это Flask-приложение в отдельном репозитории
(`my_portfolio_project`). `main-site/server/` — только его платёжный
компаньон, сам сайт сюда не входит.

### `offline/` — десктопная доска «Доска Пи»

| Подпапка | Что там |
|---|---|
| `offline/` (корень) | само WPF-приложение (`SchoolPiBoard.csproj`, exe называется `DoskaPi.exe`) |
| `offline/installer/` | установщик Inno Setup (`DoskaPiSetup.exe`) |
| `offline/server/SchoolPiBoard.LicenseServer/` | сервер лицензий: ключи, устройства, пробный период, приём оплаты за десктоп |
| `offline/web/` | страница покупки и оферта (готовые HTML, собирать не нужно) |
| `offline/docs/` | развёртывание сервера ключей, платежи, интеграция с сайтом |

Подробности продукта, история версий — `offline/README.md`.
Пошаговый запуск с нуля — `offline/docs/runbook.md`.

### `online/` — онлайн-доска по подписке

| Подпапка | Что там |
|---|---|
| `online/server/SchoolPiBoard.Online/` | ASP.NET Core + PostgreSQL + Redis: учётные записи, подписки, доски, приглашения, SignalR |
| `online/webapp/` | React + TypeScript + Vite: весь сайт |
| `online/docs/` | развёртывание, автопродление подписки |

Подробности — `online/README.md`, развёртывание — `online/docs/deploy.md`.

### `main-site/` — тарифы ИИ-функций основного сайта

| Подпапка | Что там |
|---|---|
| `main-site/server/SchoolPi.Tariffs/` | ASP.NET Core + PostgreSQL: подписки на тарифы, очередь автопродления |
| `main-site/docs/deploy.md` | развёртывание, что настроить на сервере ключей, заявка на рекуррентные платежи |

Сам основной сайт (Flask, `my_portfolio_project`) сюда не входит — этот
сервис только считает подписки и обращается к серверу ключей за оплатой.

---

## 2. Git: ветки и как пуллить

**Важная особенность именно этого репозитория**: основная (default)
ветка на GitHub называется не `main`, а
**`claude/insert-archive-files-f0rzcg`**. Это не опечатка и не временное
состояние — так сложилось исторически, и релизы (см. раздел 3) публикуются
именно с неё. Перед тем как пушить, проверьте, что вы действительно
работаете в ней:

```bash
git clone https://github.com/pootymoty/SchoolPiBoard.git
cd SchoolPiBoard
git branch -a               # покажет текущую и все ветки
git status                  # проверить, что вы не в отдельной feature-ветке случайно
```

Обычный цикл работы:

```bash
git pull origin claude/insert-archive-files-f0rzcg
# ...правите код...
git add <файлы>
git commit -m "Понятное сообщение: что и почему"
git push origin claude/insert-archive-files-f0rzcg
```

Если вы (или ИИ-ассистент) заводите отдельную ветку для проверки перед
слиянием — CI на ней тоже отработает (сборка проверится), но релизы
(раздел 3) с неё **не публикуются**: `if: github.ref_name ==
github.event.repository.default_branch` в workflow-файлах. Значит, чтобы
изменения в `offline/server/` или `main-site/server/` реально попали на
сервер, ветку с ними нужно довести до `claude/insert-archive-files-f0rzcg`.

---

## 3. CI/CD: что собирается само, что вручную

Три workflow-файла в `.github/workflows/`, каждый реагирует на пуш в свою
папку:

| Workflow | Реагирует на пуш в | Что делает | Публикует релиз? |
|---|---|---|---|
| `desktop-build.yml` | `offline/**.cs`, `offline/**.xaml`, `offline/SchoolPiBoard.csproj` | **Только проверяет**, что приложение компилируется (Windows-раннер) | Нет — это просто проверка |
| `license-server.yml` | `offline/server/**` | Собирает сервер лицензий, упаковывает | Да — `license-server-latest` (только с default-ветки) |
| `tariffs-server.yml` | `main-site/server/**` | Собирает сервис тарифов, упаковывает | Да — `tariffs-server-latest` (только с default-ветки) |

**У `online/` своего workflow нет вовсе.** Сервер и сайт онлайн-доски
собираются и доставляются вручную (`rsync`, см. раздел 5.3) — CI под них
не заведён.

**Десктопное приложение (`DoskaPi.exe`) никогда не собирается на GitHub** —
CI его только проверяет на компилируемость. Реальный `.exe` и установщик
собираются исключительно на Windows-машине разработчика (WPF и Inno
Setup — не кросс-платформенные инструменты). См. раздел 5.1.

Проверить последний собранный релиз:

```bash
# license-server-latest
curl -sL https://api.github.com/repos/pootymoty/SchoolPiBoard/releases/tags/license-server-latest

# tariffs-server-latest
curl -sL https://api.github.com/repos/pootymoty/SchoolPiBoard/releases/tags/tariffs-server-latest
```
В ответе `body` показывает, из какого коммита собрано — сверяйте с
`git log -1`, прежде чем разворачивать на сервере.

---

## 4. Топология сервера — где что живёт

Все три бэкенд-сервиса (лицензии, онлайн-доска, тарифы) предполагаются на
одной машине (Ubuntu/Debian, systemd, nginx), но не обязаны — каждый
самодостаточен и общается с другими только по сети, если вообще общается.

| | Сервер лицензий (offline) | Онлайн-доска (online) | Тарифы (main-site) |
|---|---|---|---|
| **Домен** | `keys.school-pi.online` | `school-pi-board.online` | нет — только `127.0.0.1:5090`, наружу не торчит |
| **Путь на диске** | `/var/www/schoolpiboardoff/api/` | `/var/www/schoolpiboardon/{api,web}/` | `/var/www/schoolpitariffs/api/` |
| **systemd-юнит** | `schoolpiboardoff.service` | `schoolpiboardon.service` | `schoolpitariffs.service` |
| **Файл секретов** | `/etc/schoolpiboardoff.env` | секреты в самом unit-файле (см. `online/docs/deploy.md`, можно вынести в `.env`) | секреты в unit-файле, можно вынести в `/etc/schoolpitariffs.env` |
| **База PostgreSQL** | `schoolpiboard_licenses` | `schoolpiboard_online` | `school_pi_tariffs` |
| **Redis** | не нужен | нужен (SignalR, присутствие) | не нужен |
| **Откуда берётся код** | готовый архив с GitHub Release | `rsync` с машины разработчика | готовый архив с GitHub Release |
| **Порт (за nginx)** | — (сам слушает домен) | `127.0.0.1:5081` | `127.0.0.1:5090` |

Связи между сервисами (кто кого дёргает):

```
Покупатель офлайн-ключа ──> сервер лицензий ──> Робокасса (магазин №1 «лицензии»)
Подписчик онлайн-доски  ──> сервер онлайн-доски (свой Robokassa-контур, отдельно от сервера лицензий)
Flask-сайт school-pi.online ──> сервис тарифов ──(подписанный запрос)──> сервер лицензий ──> Робокасса (магазин №3 «тарифы»)
```

**Важно:** сервер лицензий обслуживает **три разных магазина Робокассы**
одновременно — их конфигурация в `offline/server/.../ServerOptions.cs`
называется `Robokassa` (офлайн-лицензии), `RobokassaBoard` (подписка
онлайн-доски — да, платёж за неё тоже идёт **через сервер лицензий**, а не
через сервер онлайн-доски) и `RobokassaTariffs` (тарифы сайта). Это три
независимых блока настроек, каждый со своими логином/паролями в Робокассе,
каждый со своим текстом чека. Меняя один — никогда не трогайте два других
не глядя.

---

## 5. Как внести изменение и доставить его на сервер

### 5.1. Десктопное приложение и установщик (`offline/`, корень + `installer/`)

Собирается **только на Windows**. GitHub только проверяет компилируемость,
готового файла не выдаёт.

```bat
:: на Windows-машине с .NET 8 SDK
cd offline
build.bat
:: результат: offline\publish\DoskaPi.exe

:: с установленным Inno Setup 6
installer\build-installer.bat
:: результат: offline\dist\DoskaPiSetup.exe — этот файл выдаётся покупателям
```

Дальше `DoskaPiSetup.exe` нужно вручную положить туда, куда ведёт ссылка
на странице покупки (`/download/DoskaPiSetup.exe` в
`offline/web/index.html`) — обычно на тот же сервер, где крутится сайт
`school-pi.online`, или в его статику. Это отдельный шаг, CI его не делает.

### 5.2. Сервер лицензий (`offline/server/`)

```bash
# 1. Изменить код, закоммитить и запушить в default-ветку
git add offline/server/...
git commit -m "..."
git push origin claude/insert-archive-files-f0rzcg

# 2. Дождаться зелёного workflow license-server.yml (2-3 минуты)
#    Проверить: curl -sL https://api.github.com/repos/pootymoty/SchoolPiBoard/releases/tags/license-server-latest

# 3. На сервере по SSH — обновить и перезапустить
systemctl stop schoolpiboardoff
cd /tmp && rm -f ls.tar.gz
curl -sL -o ls.tar.gz https://github.com/pootymoty/SchoolPiBoard/releases/download/license-server-latest/license-server.tar.gz
rm -rf /var/www/schoolpiboardoff/api/*
tar -xzf ls.tar.gz -C /var/www/schoolpiboardoff/api
chown -R www-data:www-data /var/www/schoolpiboardoff
systemctl start schoolpiboardoff

# 4. Проверка
curl https://keys.school-pi.online/health   # {"status":"ok"}
journalctl -u schoolpiboardoff -f            # если что-то не так
```

База при обновлении не трогается — схема применяется сама при старте,
если появились новые SQL-миграции.

### 5.3. Онлайн-доска (`online/server/`, `online/webapp/`)

CI нет — сборка и доставка полностью вручную:

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

# на сервере
ssh user@server
sudo systemctl restart schoolpiboardon
sudo journalctl -u schoolpiboardon -f
curl https://school-pi-board.online/api/health
```

### 5.4. Сервис тарифов (`main-site/server/`)

Тот же приём, что и у сервера лицензий:

```bash
git push origin claude/insert-archive-files-f0rzcg
# дождаться workflow tariffs-server.yml

# на сервере
curl -sL -o ts.tar.gz https://github.com/pootymoty/SchoolPiBoard/releases/download/tariffs-server-latest/tariffs-server.tar.gz
sudo systemctl stop schoolpitariffs
sudo rm -rf /var/www/schoolpitariffs/api/*
sudo tar -xzf ts.tar.gz -C /var/www/schoolpitariffs/api
sudo systemctl start schoolpitariffs
curl http://127.0.0.1:5090/health
```

---

## 6. О чём стоит подумать дважды перед изменением

- **Сервер лицензий обслуживает три магазина Робокассы** (раздел 4).
  Меняя цену/описание/название для одного — не задевайте два других не
  глядя.
- **«Соль» в `offline/HardwareId.cs` и `offline/LocalCrypto.cs`**
  (строки вида `"SchoolPiBoard.HardwareId.v1|..."`) — **намеренно** не
  переименована вместе с продуктом. Смена этих строк меняет отпечаток
  устройства у всех уже активированных копий приложения и аннулирует их
  привязку на сервере. Трогать только осознанно, зная, что делаете с уже
  выданными лицензиями.
- **`AppId` в `offline/installer/SchoolPiBoard.iss`** — тот же GUID с
  момента создания установщика. Меняя его, Windows перестанет считать
  новую версию обновлением старой (будет ставить рядом, а не поверх).
- **Три базы данных не пересекаются намеренно**
  (`schoolpiboard_licenses`, `schoolpiboard_online`, `school_pi_tariffs`)
  — покупатель десктопа, подписчик онлайн-доски и подписчик тарифов не
  связаны друг с другом ни таблицей, ни учётной записью.
- **Домены и URL офлайн-оферты** (`school-pi.online/offer/...`,
  `keys.school-pi.online`) — не переименовывались при ребрендинге
  SchoolPiBoard → «Доска Пи»: смена живого домена — отдельное решение с
  инфраструктурными последствиями, не текстовая правка.

---

## 7. Секреты — где живут, как понять, что нужно завести

Ни один секрет не хранится в репозитории — только имена переменных
окружения и пустые заглушки в `appsettings.json`. Реальные значения — в
`/etc/*.env` или прямо в unit-файлах systemd на сервере (см. таблицу
раздела 4 и подробности в `docs/deploy.md`/`docs/runbook.md` каждого
продукта). Если поднимаете сервис впервые — там же расписано, какие
секреты сгенерировать (`openssl rand -hex 32`) и куда вписать.

---

## 8. Подробные документы — куда смотреть дальше

| Вопрос | Файл |
|---|---|
| Десктопное приложение: сборка, версии, все части | `offline/README.md` |
| Десктоп: запуск с нуля пошагово | `offline/docs/runbook.md` |
| Десктоп: справочник по редактору доски | `offline/docs/editor-reference.md` |
| Десктоп: платежи, чек, юридические заметки | `offline/docs/payment-legal-notes.md` |
| Десктоп: как сайт `school-pi.online` встраивает страницу покупки | `offline/docs/site-integration.md` |
| Сервер лицензий: протокол API | `offline/server/SchoolPiBoard.LicenseServer/BOARD-PROTOCOL.md` |
| Онлайн-доска: обзор, что работает / не работает | `online/README.md` |
| Онлайн-доска: развёртывание | `online/docs/deploy.md` |
| Онлайн-доска: автопродление подписки | `online/docs/robokassa-recurring.md` |
| Онлайн-доска: техническая спецификация | `online/docs/board-spec.md`, `online/docs/design-spec.md` |
| Тарифы сайта: развёртывание, рекуррентные платежи | `main-site/docs/deploy.md` |
| Тарифы сайта: обзор сервиса | `main-site/server/README.md` |

---

## Памятка для ИИ-ассистента

Если вы читаете этот файл в новой сессии без памяти о прошлых разговорах —
вот что стоит знать и сделать до того, как начать менять код.

**Прежде чем действовать:**
1. Уточните у пользователя, какой из трёх продуктов (offline / online /
   main-site) в работе — они не связаны кодом, и правка в одном не должна
   случайно задеть файлы другого.
2. Если задача касается `offline/server/` — спросите, есть ли уже
   активные платящие покупатели (скорее всего да). Это меняет, что можно
   трогать без разговора (см. раздел 6 — особенно про соль в
   `HardwareId`/`LocalCrypto` и про три магазина Робокассы).
3. Проверьте текущую ветку (`git branch`, `git status`) — default-ветка
   называется `claude/insert-archive-files-f0rzcg`, не `main`. От неё
   зависит, опубликуется ли релиз при пуше (раздел 2–3).
4. Правки в реальный `.exe`/установщик приложения вы собрать не можете —
   нет Windows и Inno Setup в типичной среде ИИ-агента. Собирайте и
   проверяйте компилируемость через `dotnet build`/CI, а реальный файл
   собирает и распространяет сам пользователь (раздел 5.1).
5. Изменение кода в git **не equals** изменению на живом сервере. Пуш —
   это либо (а) просто код в репозитории, либо (б) для `offline/server/`
   и `main-site/server/` — ещё и новый файл в GitHub Release, но **не
   автоматический деплой**: обновление сервиса на сервере — отдельный
   ручной шаг по SSH (разделы 5.2, 5.4), требующий доступа, которого у
   вас, скорее всего, нет. Не утверждайте, что «обновление уже
   применилось», пока пользователь явно не подтвердил, что прогнал
   команды на сервере.

**Стиль репозитория** (виден по существующим `.md`-файлам и коммитам):
докстринги и комментарии — на русском, объясняют «почему», а не «что»;
коммиты пишутся в духе «что изменилось и зачем», без лишней рекламы;
изменения затрагивают минимум нужного, без попутного рефакторинга и без
самовольных решений там, где есть реальная неоднозначность (спросить —
дешевле, чем аннулировать чужие активации или задеть чужой магазин
Робокассы).
