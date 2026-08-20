-- Схема сервера лицензий. Скрипт идемпотентный: выполняется при каждом старте
-- сервиса и ничего не ломает, если таблицы уже есть.

CREATE TABLE IF NOT EXISTS licenses (
    id                  uuid        PRIMARY KEY,
    key                 text        NOT NULL,
    email               text        NOT NULL,
    created_at          timestamptz NOT NULL,
    revoked             boolean     NOT NULL DEFAULT false,
    -- SHA-256 от идентификатора платежа. Сам идентификатор и тем более
    -- данные карты здесь не хранятся: хеша достаточно, чтобы распознать
    -- повторное уведомление об одной и той же оплате.
    payment_hash        text        NULL,
    -- Когда письмо с ключом реально ушло. NULL = ещё не отправляли,
    -- и повторное уведомление об оплате попробует снова.
    email_sent_at       timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_licenses_key
    ON licenses (key);

CREATE UNIQUE INDEX IF NOT EXISTS ux_licenses_payment_hash
    ON licenses (payment_hash)
    WHERE payment_hash IS NOT NULL;

CREATE TABLE IF NOT EXISTS license_activations (
    id                uuid        PRIMARY KEY,
    license_id        uuid        NOT NULL REFERENCES licenses (id) ON DELETE CASCADE,
    hardware_id       text        NOT NULL,
    activated_at      timestamptz NOT NULL,
    last_validated_at timestamptz NOT NULL
);

-- Одно устройство занимает ровно один слот, повторная активация обновляет запись.
CREATE UNIQUE INDEX IF NOT EXISTS ux_license_activations_license_hardware
    ON license_activations (license_id, hardware_id);

CREATE INDEX IF NOT EXISTS ix_license_activations_license
    ON license_activations (license_id);
