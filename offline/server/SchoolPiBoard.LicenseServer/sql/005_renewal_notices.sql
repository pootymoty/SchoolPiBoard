ALTER TABLE payments ADD COLUMN IF NOT EXISTS renewal_end timestamptz;
ALTER TABLE payments ADD COLUMN IF NOT EXISTS next_status_check timestamptz;
ALTER TABLE payments ADD COLUMN IF NOT EXISTS decline_reason text;
ALTER TABLE payments ADD COLUMN IF NOT EXISTS retry_eligible boolean NOT NULL DEFAULT false;
ALTER TABLE payments ADD COLUMN IF NOT EXISTS failure_notice_end timestamptz;
ALTER TABLE payments ADD COLUMN IF NOT EXISTS advance_notice_end timestamptz;
CREATE INDEX IF NOT EXISTS ix_payments_status_check ON payments(next_status_check) WHERE next_status_check IS NOT NULL AND status = 'pending';
