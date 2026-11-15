CREATE SCHEMA IF NOT EXISTS marketing;

CREATE TABLE marketing.campaigns (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name text NOT NULL,
    cohort_id bigint NOT NULL,
    cohort_name text NOT NULL,
    kind text NOT NULL CHECK (kind IN ('percent', 'fixed')),
    value numeric(12, 2) NOT NULL,
    min_subtotal numeric(12, 2) NOT NULL DEFAULT 0,
    max_discount numeric(12, 2),
    valid_days int NOT NULL CHECK (valid_days BETWEEN 1 AND 90),
    subject text NOT NULL,
    message text NOT NULL,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'sending', 'sent', 'failed')),
    recipients int NOT NULL DEFAULT 0,
    error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    sent_at timestamptz
);

CREATE TABLE marketing.coupons (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code text NOT NULL UNIQUE,
    campaign_id bigint REFERENCES marketing.campaigns (id),
    customer_id bigint,
    kind text NOT NULL CHECK (kind IN ('percent', 'fixed')),
    value numeric(12, 2) NOT NULL,
    min_subtotal numeric(12, 2) NOT NULL DEFAULT 0,
    max_discount numeric(12, 2),
    expires_at timestamptz NOT NULL,
    used_order_id uuid,
    used_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (campaign_id, customer_id)
);

CREATE INDEX coupons_customer_idx ON marketing.coupons (customer_id, expires_at DESC);
CREATE INDEX coupons_order_idx ON marketing.coupons (used_order_id) WHERE used_order_id IS NOT NULL;

CREATE TABLE marketing.reapdeep_alert_subscriptions (
    alert_id bigint PRIMARY KEY,
    secret text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE marketing.analytics_alerts (
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

CREATE INDEX analytics_alerts_received_idx ON marketing.analytics_alerts (received_at DESC);
