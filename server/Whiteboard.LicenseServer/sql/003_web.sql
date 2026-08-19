-- Веб-версия: пользователи, подписки, доски, участники и объекты досок.
-- Скрипт идемпотентный, как и предыдущие.

CREATE TABLE IF NOT EXISTS users (
    id            uuid        PRIMARY KEY,
    email         text        NOT NULL,
    password_hash text        NOT NULL,
    display_name  text        NOT NULL DEFAULT '',
    created_at    timestamptz NOT NULL
);

-- Почта хранится в нижнем регистре, поэтому обычного уникального индекса хватает.
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email
    ON users (email);

CREATE TABLE IF NOT EXISTS subscriptions (
    id                 uuid        PRIMARY KEY,
    user_id            uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    -- Платёжная система пока не выбрана окончательно (см. docs/payment-legal-notes.md),
    -- поэтому вместо stripe_subscription_id — пара «провайдер + его идентификатор».
    provider           text        NOT NULL,
    external_id        text        NULL,
    plan               text        NOT NULL,
    status             text        NOT NULL,
    trial_ends_at      timestamptz NULL,
    current_period_end timestamptz NULL,
    created_at         timestamptz NOT NULL,
    updated_at         timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_subscriptions_user
    ON subscriptions (user_id);

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

CREATE INDEX IF NOT EXISTS ix_boards_owner
    ON boards (owner_id);

CREATE TABLE IF NOT EXISTS board_members (
    id         uuid        PRIMARY KEY,
    board_id   uuid        NOT NULL REFERENCES boards (id) ON DELETE CASCADE,
    user_id    uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    role       text        NOT NULL,
    invited_at timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_board_members_board_user
    ON board_members (board_id, user_id);

CREATE INDEX IF NOT EXISTS ix_board_members_user
    ON board_members (user_id);

-- Каждый объект доски — отдельная строка. Именно это делает возможными
-- точечные правки и блокировку на уровне объекта: одним JSON-блоком
-- на всю доску двое одновременно работать не смогли бы.
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

CREATE INDEX IF NOT EXISTS ix_board_items_board
    ON board_items (board_id);
