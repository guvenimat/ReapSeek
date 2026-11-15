CREATE TABLE sellers.outbox (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_type text NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    trace_parent text,
    published_at timestamptz
);

CREATE INDEX outbox_unpublished_idx ON sellers.outbox (id) WHERE published_at IS NULL;
CREATE INDEX outbox_published_idx ON sellers.outbox (published_at) WHERE published_at IS NOT NULL;

CREATE TABLE sellers.inbox (
    id bigint GENERATED ALWAYS AS IDENTITY (START WITH 3000000000000001 MINVALUE 3000000000000001 MAXVALUE 3999999999999999) PRIMARY KEY,
    source_module text NOT NULL,
    source_message_id bigint NOT NULL,
    event_type text NOT NULL,
    handler_name text NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    available_at timestamptz NOT NULL DEFAULT now(),
    attempts int NOT NULL DEFAULT 0,
    last_error text,
    processed_at timestamptz,
    failed_at timestamptz,
    discarded_at timestamptz,
    trace_parent text,
    CONSTRAINT inbox_message_handler_key UNIQUE (source_module, source_message_id, handler_name)
);

CREATE INDEX inbox_pending_idx ON sellers.inbox (available_at, id) WHERE processed_at IS NULL AND failed_at IS NULL;
CREATE INDEX inbox_failed_idx ON sellers.inbox (id) WHERE failed_at IS NOT NULL AND discarded_at IS NULL;
CREATE INDEX inbox_processed_idx ON sellers.inbox (processed_at) WHERE processed_at IS NOT NULL;
