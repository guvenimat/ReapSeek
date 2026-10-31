ALTER TABLE carts ADD COLUMN reminded_at timestamptz;
CREATE INDEX carts_reminder_idx ON carts (updated_at) WHERE customer_id IS NOT NULL;

CREATE TABLE reapdeep_alert_subscriptions (
    alert_id bigint PRIMARY KEY,
    secret text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE analytics_alerts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    alert_id bigint NOT NULL,
    alert_name text NOT NULL,
    state text NOT NULL,
    condition text NOT NULL,
    threshold double precision NOT NULL,
    time_window text NOT NULL,
    value double precision NOT NULL,
    previous_value double precision,
    message text NOT NULL,
    insight_name text NOT NULL,
    checked_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (alert_id, checked_at, state)
);

CREATE INDEX analytics_alerts_received_idx ON analytics_alerts (received_at DESC);
