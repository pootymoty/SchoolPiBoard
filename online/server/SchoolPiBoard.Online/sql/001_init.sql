-- Схема онлайн-доски school-pi-board.online.
-- Отдельная база от сервера лицензий десктопной версии: общих данных нет.
-- Скрипт идемпотентный, выполняется при каждом старте сервиса.

CREATE TABLE IF NOT EXISTS users (
    id            uuid        PRIMARY KEY,
    email         text        NOT NULL,
    password_hash text        NOT NULL,
    last_name     text        NOT NULL,
    first_name    text        NOT NULL,
    birth_date    date        NOT NULL,
    created_at    timestamptz NOT NULL,
    -- Пробный период даётся один раз; здесь отметка о том, что он уже был.
    trial_used_at timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON users (email);

-- Незавершённая регистрация. Учётная запись появляется только после
-- подтверждения почты; час спустя запись становится недействительной.
CREATE TABLE IF NOT EXISTS pending_registrations (
    id            uuid        PRIMARY KEY,
    email         text        NOT NULL,
    password_hash text        NOT NULL,
    last_name     text        NOT NULL,
    first_name    text        NOT NULL,
    birth_date    date        NOT NULL,
    -- В базе только хеш кода из письма: утечка таблицы не даёт подтвердить чужую почту.
    token_hash    text        NOT NULL,
    created_at    timestamptz NOT NULL,
    expires_at    timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_pending_registrations_email ON pending_registrations (email);
CREATE INDEX IF NOT EXISTS ix_pending_registrations_token ON pending_registrations (token_hash);

-- Подтверждения действий по почте: удаление аккаунта и всё, что появится позже.
CREATE TABLE IF NOT EXISTS email_actions (
    id         uuid        PRIMARY KEY,
    user_id    uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    kind       text        NOT NULL,
    token_hash text        NOT NULL,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    used_at    timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_email_actions_token ON email_actions (token_hash);
CREATE INDEX IF NOT EXISTS ix_email_actions_user ON email_actions (user_id);

CREATE TABLE IF NOT EXISTS subscriptions (
    id          uuid        PRIMARY KEY,
    user_id     uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    -- trial | paid
    kind        text        NOT NULL,
    plan_days   integer     NOT NULL,
    status      text        NOT NULL,
    started_at  timestamptz NOT NULL,
    expires_at  timestamptz NOT NULL,
    auto_renew  boolean     NOT NULL DEFAULT false,
    provider    text        NULL,
    external_id text        NULL,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL
);

-- Действующая подписка у пользователя одна: продление сдвигает дату окончания.
CREATE UNIQUE INDEX IF NOT EXISTS ux_subscriptions_user ON subscriptions (user_id);

CREATE SEQUENCE IF NOT EXISTS payments_invoice_id_seq AS bigint START WITH 1000;

CREATE TABLE IF NOT EXISTS payments (
    id         uuid           PRIMARY KEY,
    user_id    uuid           NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    invoice_id bigint         NOT NULL,
    plan_days  integer        NOT NULL,
    amount     numeric(12, 2) NOT NULL,
    provider   text           NOT NULL,
    status     text           NOT NULL,
    created_at timestamptz    NOT NULL,
    paid_at    timestamptz    NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_invoice_id ON payments (invoice_id);
CREATE INDEX IF NOT EXISTS ix_payments_user ON payments (user_id);

CREATE TABLE IF NOT EXISTS boards (
    id               uuid        PRIMARY KEY,
    owner_id         uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    name             text        NOT NULL,
    created_at       timestamptz NOT NULL,
    modified_at      timestamptz NOT NULL,
    archived         boolean     NOT NULL DEFAULT false,
    background_style text        NOT NULL DEFAULT 'plain',
    background_color text        NOT NULL DEFAULT '#FFFFFF'
);

CREATE INDEX IF NOT EXISTS ix_boards_owner ON boards (owner_id);

CREATE TABLE IF NOT EXISTS board_members (
    id         uuid        PRIMARY KEY,
    board_id   uuid        NOT NULL REFERENCES boards (id) ON DELETE CASCADE,
    user_id    uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    role       text        NOT NULL,
    invited_at timestamptz NOT NULL,
    -- Пришёл по ссылке, а не по личному приглашению.
    via_link   boolean     NOT NULL DEFAULT false,
    -- Когда заканчивается право менять доску. NULL — бессрочно.
    -- После этой даты участник остаётся в доске, но только смотрит;
    -- продлить срок может только владелец, назначив роль заново.
    edit_until timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_board_members_board_user ON board_members (board_id, user_id);
CREATE INDEX IF NOT EXISTS ix_board_members_user ON board_members (user_id);

-- Ссылка-приглашение. Хранится хеш, поэтому из базы ссылку не восстановить.
CREATE TABLE IF NOT EXISTS board_invites (
    id           uuid        PRIMARY KEY,
    board_id     uuid        NOT NULL REFERENCES boards (id) ON DELETE CASCADE,
    created_by   uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    token_hash   text        NOT NULL,
    role         text        NOT NULL,
    created_at   timestamptz NOT NULL,
    expires_at   timestamptz NOT NULL,
    revoked_at   timestamptz NULL,
    uses         integer     NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_board_invites_token ON board_invites (token_hash);
CREATE INDEX IF NOT EXISTS ix_board_invites_board ON board_invites (board_id);

-- Каждый объект доски — отдельная строка: только так возможны точечные
-- правки и блокировка на уровне объекта при совместной работе.
CREATE TABLE IF NOT EXISTS board_items (
    id           uuid             PRIMARY KEY,
    board_id     uuid             NOT NULL REFERENCES boards (id) ON DELETE CASCADE,
    kind         text             NOT NULL,
    x            double precision NOT NULL DEFAULT 0,
    y            double precision NOT NULL DEFAULT 0,
    w            double precision NOT NULL DEFAULT 0,
    h            double precision NOT NULL DEFAULT 0,
    rotation     double precision NOT NULL DEFAULT 0,
    z_index      integer          NOT NULL DEFAULT 0,
    stroke_color text             NULL,
    fill_color   text             NULL,
    thickness    double precision NULL,
    opacity      double precision NULL,
    points       jsonb            NULL,
    text         text             NULL,
    font_size    double precision NULL,
    image_ref    text             NULL,
    created_by   uuid             NULL REFERENCES users (id) ON DELETE SET NULL,
    created_at   timestamptz      NOT NULL,
    updated_at   timestamptz      NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_board_items_board ON board_items (board_id);
