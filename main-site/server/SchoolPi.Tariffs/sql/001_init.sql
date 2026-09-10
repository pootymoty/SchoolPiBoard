CREATE TABLE IF NOT EXISTS subscriptions (
    id uuid PRIMARY KEY,
    external_user_id integer NOT NULL,
    plan varchar(30) NOT NULL,
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL,
    auto_renew boolean NOT NULL DEFAULT false,
    invoice_id bigint NULL,
    renewal_notice_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_subscriptions_external_user_id
    ON subscriptions (external_user_id);

CREATE INDEX IF NOT EXISTS ix_subscriptions_due_for_renewal
    ON subscriptions (auto_renew, ends_at)
    WHERE auto_renew = true AND invoice_id IS NOT NULL;

CREATE SEQUENCE IF NOT EXISTS payments_invoice_id_seq;

CREATE TABLE IF NOT EXISTS payments (
    id uuid PRIMARY KEY,
    external_user_id integer NOT NULL,
    invoice_id bigint NOT NULL,
    plan varchar(30) NOT NULL,
    amount numeric(12, 2) NOT NULL,
    auto_renew boolean NOT NULL DEFAULT false,
    status varchar(20) NOT NULL DEFAULT 'pending',
    consent_text text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    paid_at timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_payments_invoice_id
    ON payments (invoice_id);
