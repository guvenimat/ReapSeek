ALTER TABLE orders ADD COLUMN discount_total numeric(12, 2) NOT NULL DEFAULT 0;
ALTER TABLE orders ADD COLUMN coupon_code text;

ALTER TABLE returns ADD COLUMN refund_amount numeric(12, 2);
UPDATE returns SET refund_amount = total;
ALTER TABLE returns ALTER COLUMN refund_amount SET NOT NULL;

CREATE TABLE campaigns (
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

CREATE TABLE coupons (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code text NOT NULL UNIQUE,
    campaign_id bigint REFERENCES campaigns (id),
    customer_id bigint REFERENCES users (id),
    kind text NOT NULL CHECK (kind IN ('percent', 'fixed')),
    value numeric(12, 2) NOT NULL,
    min_subtotal numeric(12, 2) NOT NULL DEFAULT 0,
    max_discount numeric(12, 2),
    expires_at timestamptz NOT NULL,
    used_order_id uuid REFERENCES orders (id),
    used_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (campaign_id, customer_id)
);

CREATE INDEX coupons_customer_idx ON coupons (customer_id, expires_at DESC);
CREATE INDEX coupons_order_idx ON coupons (used_order_id) WHERE used_order_id IS NOT NULL;
