-- Пробный период и платежи Робокассы. Скрипт идемпотентный, как и первый.

CREATE TABLE IF NOT EXISTS trial_activations (
    id          uuid        PRIMARY KEY,
    -- Отпечаток компьютера: один компьютер — один пробный период за всё время.
    hardware_id text        NOT NULL,
    email       text        NOT NULL,
    started_at  timestamptz NOT NULL,
    expires_at  timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_trial_activations_hardware
    ON trial_activations (hardware_id);

-- Почта проверяется отдельно: она отсекает повтор на новом компьютере,
-- когда отпечаток уже другой (переустановка Windows, новый диск).
CREATE INDEX IF NOT EXISTS ix_trial_activations_email
    ON trial_activations (lower(email));

-- Номер счёта Робокассы: целое число, уникальное на магазин.
CREATE SEQUENCE IF NOT EXISTS payments_invoice_id_seq AS bigint START WITH 1000;

CREATE TABLE IF NOT EXISTS payments (
    id         uuid           PRIMARY KEY,
    invoice_id bigint         NOT NULL,
    email      text           NOT NULL,
    amount     numeric(12, 2) NOT NULL,
    provider   text           NOT NULL,
    -- pending — счёт выставлен, paid — оплата подтверждена по ResultURL.
    status     text           NOT NULL,
    created_at timestamptz    NOT NULL,
    paid_at    timestamptz    NULL,
    license_id uuid           NULL REFERENCES licenses (id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_invoice_id
    ON payments (invoice_id);

CREATE INDEX IF NOT EXISTS ix_payments_email
    ON payments (lower(email));
