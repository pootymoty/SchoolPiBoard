-- Подписка на тарифы ИИ-функций основного сайта (school-pi.online) — тот
-- же приём, что и у подписки онлайн-доски в 003: те же счета, третье
-- значение kind, свой магазин Робокассы (Robokassa:Tariffs:*).
--
-- Отдельного столбца для board_user_id не хватает: это id пользователя
-- основного сайта, из другой базы данных, и путать их с id доски нельзя,
-- даже случайно.

ALTER TABLE payments
    ADD COLUMN IF NOT EXISTS tariff_user_id bigint NULL;

CREATE INDEX IF NOT EXISTS ix_payments_pending_notify_tariff
    ON payments (kind, status, notified_at)
    WHERE kind = 'tariff' AND status = 'paid' AND notified_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_payments_tariff_user
    ON payments (tariff_user_id)
    WHERE tariff_user_id IS NOT NULL;
