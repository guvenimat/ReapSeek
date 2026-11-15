CREATE SCHEMA IF NOT EXISTS platform;

CREATE TABLE platform.outbox (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    type text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    available_at timestamptz NOT NULL DEFAULT now(),
    attempts int NOT NULL DEFAULT 0,
    last_error text,
    processed_at timestamptz,
    failed_at timestamptz,
    discarded_at timestamptz,
    trace_parent text
);

CREATE INDEX outbox_pending_idx ON platform.outbox (available_at, id) WHERE processed_at IS NULL AND failed_at IS NULL;
CREATE INDEX outbox_failed_idx ON platform.outbox (id) WHERE failed_at IS NOT NULL AND discarded_at IS NULL;

DO $$
BEGIN
    EXECUTE format('ALTER DATABASE %I SET search_path = identity, catalog, sellers, ordering, finance, engagement, marketing, platform, public', current_database());
END
$$;
