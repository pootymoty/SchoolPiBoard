CREATE TABLE IF NOT EXISTS subscriptions (
    id uuid PRIMARY KEY,
    external_user_id integer NOT NULL,
    plan varchar(30) NOT NULL,
    started_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_subscriptions_external_user_id
    ON subscriptions (external_user_id);

CREATE SEQUENCE IF NOT EXISTS payments_invoice_id_seq;

CREATE TABLE IF NOT EXISTS payments (
    id uuid PRIMARY KEY,
    external_user_id integer NOT NULL,
    invoice_id bigint NOT NULL,
    plan varchar(30) NOT NULL,
    amount numeric(12, 2) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'pending',
    created_at timestamptz NOT NULL DEFAULT now(),
    paid_at timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_payments_invoice_id
    ON payments (invoice_id);
