ALTER TABLE outbox ADD COLUMN discarded_at timestamptz;

CREATE INDEX outbox_failed_idx ON outbox (id) WHERE failed_at IS NOT NULL AND discarded_at IS NULL;
