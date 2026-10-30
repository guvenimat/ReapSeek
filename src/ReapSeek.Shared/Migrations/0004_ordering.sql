CREATE TABLE carts (
    id uuid PRIMARY KEY,
    customer_id bigint UNIQUE REFERENCES users (id) ON DELETE CASCADE,
    guest_token text UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CHECK ((customer_id IS NULL) <> (guest_token IS NULL))
);

CREATE TABLE cart_lines (
    cart_id uuid NOT NULL REFERENCES carts (id) ON DELETE CASCADE,
    offer_id bigint NOT NULL REFERENCES offers (id) ON DELETE CASCADE,
    quantity int NOT NULL CHECK (quantity BETWEEN 1 AND 10),
    added_price numeric(12, 2) NOT NULL,
    added_at timestamptz NOT NULL,
    PRIMARY KEY (cart_id, offer_id)
);

CREATE TABLE addresses (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id bigint NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    full_name text NOT NULL,
    phone text NOT NULL,
    city text NOT NULL,
    district text NOT NULL,
    line text NOT NULL,
    postal_code text NOT NULL,
    last_used_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (customer_id, full_name, phone, city, district, line, postal_code)
);

CREATE SEQUENCE order_number_seq;

CREATE TABLE orders (
    id uuid PRIMARY KEY,
    number text NOT NULL UNIQUE,
    customer_id bigint NOT NULL REFERENCES users (id),
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
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX orders_customer_idx ON orders (customer_id, placed_at DESC);
CREATE INDEX orders_placed_idx ON orders (placed_at DESC);
CREATE INDEX orders_pending_idx ON orders (placed_at) WHERE status = 'pending_payment';

CREATE TABLE order_packages (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    seller_id bigint NOT NULL REFERENCES sellers (id),
    seller_name text NOT NULL,
    status text NOT NULL CHECK (status IN ('awaiting_payment', 'preparing', 'shipped', 'delivered', 'cancelled')),
    shipping_fee numeric(12, 2) NOT NULL,
    tracking_number text,
    shipped_at timestamptz,
    delivered_at timestamptz
);

CREATE INDEX order_packages_order_idx ON order_packages (order_id);
CREATE INDEX order_packages_seller_idx ON order_packages (seller_id, status);

CREATE TABLE order_lines (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
    package_id uuid NOT NULL REFERENCES order_packages (id) ON DELETE CASCADE,
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

CREATE INDEX order_lines_order_idx ON order_lines (order_id);

CREATE TABLE payments (
    id uuid PRIMARY KEY,
    order_id uuid NOT NULL REFERENCES orders (id) ON DELETE CASCADE,
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

CREATE INDEX payments_order_idx ON payments (order_id);

CREATE TABLE payment_events (
    provider text NOT NULL,
    event_id text NOT NULL,
    provider_payment_id text NOT NULL,
    type text NOT NULL,
    payload jsonb NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (provider, event_id)
);
