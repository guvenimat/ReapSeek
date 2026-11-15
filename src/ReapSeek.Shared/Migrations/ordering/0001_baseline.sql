CREATE SCHEMA IF NOT EXISTS ordering;

CREATE TABLE ordering.carts (
    id uuid PRIMARY KEY,
    customer_id bigint UNIQUE,
    guest_token text UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    reminded_at timestamptz,
    CHECK ((customer_id IS NULL) <> (guest_token IS NULL))
);

CREATE INDEX carts_reminder_idx ON ordering.carts (updated_at) WHERE customer_id IS NOT NULL;

CREATE TABLE ordering.cart_lines (
    cart_id uuid NOT NULL REFERENCES ordering.carts (id) ON DELETE CASCADE,
    offer_id bigint NOT NULL,
    quantity int NOT NULL CHECK (quantity BETWEEN 1 AND 10),
    added_price numeric(12, 2) NOT NULL,
    added_at timestamptz NOT NULL,
    PRIMARY KEY (cart_id, offer_id)
);

CREATE TABLE ordering.addresses (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id bigint NOT NULL,
    full_name text NOT NULL,
    phone text NOT NULL,
    city text NOT NULL,
    district text NOT NULL,
    line text NOT NULL,
    postal_code text NOT NULL,
    last_used_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (customer_id, full_name, phone, city, district, line, postal_code)
);

CREATE SEQUENCE ordering.order_number_seq;

CREATE TABLE ordering.orders (
    id uuid PRIMARY KEY,
    number text NOT NULL UNIQUE,
    customer_id bigint NOT NULL,
    status text NOT NULL CHECK (status IN ('pending_payment', 'paid', 'payment_failed', 'cancelled')),
    currency text NOT NULL,
    subtotal numeric(12, 2) NOT NULL,
    shipping_total numeric(12, 2) NOT NULL,
    total numeric(12, 2) NOT NULL,
    shipping_address jsonb NOT NULL,
    placed_at timestamptz NOT NULL,
    paid_at timestamptz,
    payment_reference text,
    close_reason text,
    updated_at timestamptz NOT NULL DEFAULT now(),
    discount_total numeric(12, 2) NOT NULL DEFAULT 0,
    coupon_code text,
    refunded_at timestamptz,
    refund_reference text,
    refund_notified_at timestamptz
);

CREATE INDEX orders_customer_idx ON ordering.orders (customer_id, placed_at DESC);
CREATE INDEX orders_placed_idx ON ordering.orders (placed_at DESC);
CREATE INDEX orders_pending_idx ON ordering.orders (placed_at) WHERE status = 'pending_payment';
CREATE UNIQUE INDEX orders_payment_reference_idx ON ordering.orders (payment_reference) WHERE payment_reference IS NOT NULL;

CREATE TABLE ordering.order_packages (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES ordering.orders (id) ON DELETE CASCADE,
    seller_id bigint NOT NULL,
    seller_name text NOT NULL,
    status text NOT NULL CHECK (status IN ('awaiting_payment', 'preparing', 'shipped', 'delivered', 'cancelled')),
    shipping_fee numeric(12, 2) NOT NULL,
    tracking_number text,
    shipped_at timestamptz,
    delivered_at timestamptz,
    commission_rate numeric(5, 4) NOT NULL,
    shipment_status text,
    ship_reminded_at timestamptz,
    cancel_reason text,
    refunded_at timestamptz,
    refund_reference text,
    refund_notified_at timestamptz
);

CREATE INDEX order_packages_order_idx ON ordering.order_packages (order_id);
CREATE INDEX order_packages_seller_idx ON ordering.order_packages (seller_id, status);
CREATE UNIQUE INDEX order_packages_tracking_idx ON ordering.order_packages (tracking_number) WHERE tracking_number IS NOT NULL;
CREATE INDEX order_packages_preparing_idx ON ordering.order_packages (order_id) WHERE status = 'preparing';

CREATE TABLE ordering.order_lines (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES ordering.orders (id) ON DELETE CASCADE,
    package_id uuid NOT NULL REFERENCES ordering.order_packages (id) ON DELETE CASCADE,
    offer_id bigint NOT NULL,
    product_id bigint NOT NULL,
    variant_id bigint NOT NULL,
    product_title text NOT NULL,
    variant_label text NOT NULL,
    sku text NOT NULL,
    image_url text,
    unit_price numeric(12, 2) NOT NULL,
    quantity int NOT NULL CHECK (quantity BETWEEN 1 AND 10)
);

CREATE INDEX order_lines_order_idx ON ordering.order_lines (order_id);

CREATE TABLE ordering.payments (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES ordering.orders (id) ON DELETE CASCADE,
    provider text NOT NULL,
    provider_payment_id text UNIQUE,
    amount numeric(12, 2) NOT NULL,
    currency text NOT NULL,
    status text NOT NULL CHECK (status IN ('created', 'pending', 'succeeded', 'failed')),
    redirect_url text,
    failure_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX payments_order_idx ON ordering.payments (order_id);

CREATE TABLE ordering.payment_events (
    provider text NOT NULL,
    event_id text NOT NULL,
    provider_payment_id text NOT NULL,
    type text NOT NULL,
    payload jsonb NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (provider, event_id)
);

CREATE TABLE ordering.shipment_events (
    event_id text PRIMARY KEY,
    tracking_number text NOT NULL,
    kind text NOT NULL CHECK (kind IN ('outbound', 'return')),
    status text NOT NULL,
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX shipment_events_tracking_idx ON ordering.shipment_events (tracking_number, occurred_at);

CREATE SEQUENCE ordering.return_number_seq;

CREATE TABLE ordering.returns (
    id uuid PRIMARY KEY,
    number text NOT NULL UNIQUE,
    order_id uuid NOT NULL REFERENCES ordering.orders (id),
    order_number text NOT NULL,
    package_id uuid NOT NULL REFERENCES ordering.order_packages (id),
    seller_id bigint NOT NULL,
    customer_id bigint NOT NULL,
    status text NOT NULL CHECK (status IN ('requested', 'approved', 'rejected', 'received', 'refunded')),
    reason text NOT NULL,
    comment text,
    commission_rate numeric(5, 4) NOT NULL,
    total numeric(12, 2) NOT NULL,
    currency text NOT NULL,
    requested_at timestamptz NOT NULL,
    decided_at timestamptz,
    rejection_note text,
    return_tracking text UNIQUE,
    received_at timestamptz,
    refunded_at timestamptz,
    refund_reference text,
    refund_amount numeric(12, 2) NOT NULL
);

CREATE INDEX returns_order_idx ON ordering.returns (order_id);
CREATE INDEX returns_seller_idx ON ordering.returns (seller_id, status, requested_at DESC);

CREATE TABLE ordering.return_lines (
    return_id uuid NOT NULL REFERENCES ordering.returns (id) ON DELETE CASCADE,
    order_line_id uuid NOT NULL REFERENCES ordering.order_lines (id),
    product_id bigint NOT NULL,
    product_title text NOT NULL,
    variant_label text NOT NULL,
    sku text NOT NULL,
    unit_price numeric(12, 2) NOT NULL,
    quantity int NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (return_id, order_line_id)
);
