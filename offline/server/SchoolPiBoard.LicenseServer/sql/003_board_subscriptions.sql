-- Подписка онлайн-доски: те же счета, что и у лицензий, но покупка другая.
--
-- Отдельной таблицы нет намеренно. Номер счёта у Робокассы один на магазин,
-- и вторая таблица со своей последовательностью рано или поздно выдала бы
-- номер, который уже занят: оплата пришла бы на счёт, которого сервис не
-- знает, а человек остался бы без того, за что заплатил.
--
-- Все колонки добавляются со значениями по умолчанию, поэтому уже
-- выставленные счета за офлайн-лицензию остаются ровно такими, как были:
-- kind = 'license' — это они.

ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS kind text NOT NULL DEFAULT 'license';

-- Кому продлевать подписку на доске. Учётной записи доски здесь нет и не
-- будет: сервис ключей знает только её номер.
ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS board_user_id bigint NULL;

ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS plan_code text NULL;

ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS period_days integer NULL;

-- Назначение платежа для этого счёта. У лицензии оно одно на всех и лежит
-- в настройках, а у подписки зависит от тарифа и срока — и в чеке должно
-- стоять именно оно.
ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS description text NULL;

-- Когда доска подтвердила, что узнала об оплате. Пусто — значит уведомление
-- ещё не дошло и его нужно повторить.
ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS notified_at timestamptz NULL;

-- Согласился ли человек на автопродление. Робокасса разрешает повторные
-- списания только по счёту, который был помечен таковым при первой оплате.
ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS auto_renew boolean NOT NULL DEFAULT false;

-- Счёт, по которому Робокасса списывает повторно.
ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS previous_invoice_id bigint NULL;

-- Оплаченные, но не доставленные доске — их разбирает повтор уведомлений.
CREATE INDEX IF NOT EXISTS ix_payments_pending_notify
    ON payments (kind, status, notified_at)
    WHERE kind = 'subscription' AND status = 'paid' AND notified_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_payments_board_user
    ON payments (board_user_id)
    WHERE board_user_id IS NOT NULL;
