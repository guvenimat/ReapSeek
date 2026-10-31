ALTER TABLE order_packages ADD COLUMN shipment_status text;
CREATE UNIQUE INDEX order_packages_tracking_idx ON order_packages (tracking_number) WHERE tracking_number IS NOT NULL;

CREATE TABLE shipment_events (
    event_id text PRIMARY KEY,
    tracking_number text NOT NULL,
    kind text NOT NULL CHECK (kind IN ('outbound', 'return')),
    status text NOT NULL,
    occurred_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX shipment_events_tracking_idx ON shipment_events (tracking_number, occurred_at);

CREATE SEQUENCE return_number_seq;

CREATE TABLE returns (
    id uuid PRIMARY KEY,
    number text NOT NULL UNIQUE,
    order_id uuid NOT NULL REFERENCES orders (id),
    order_number text NOT NULL,
    package_id uuid NOT NULL REFERENCES order_packages (id),
    seller_id bigint NOT NULL REFERENCES sellers (id),
    customer_id bigint NOT NULL REFERENCES users (id),
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
    refund_reference text
);

CREATE INDEX returns_order_idx ON returns (order_id);
CREATE INDEX returns_seller_idx ON returns (seller_id, status, requested_at DESC);

CREATE TABLE return_lines (
    return_id uuid NOT NULL REFERENCES returns (id) ON DELETE CASCADE,
    order_line_id uuid NOT NULL REFERENCES order_lines (id),
    product_id bigint NOT NULL,
    product_title text NOT NULL,
    variant_label text NOT NULL,
    sku text NOT NULL,
    unit_price numeric(12, 2) NOT NULL,
    quantity int NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (return_id, order_line_id)
);

ALTER TABLE seller_ledger DROP CONSTRAINT seller_ledger_kind_check;
ALTER TABLE seller_ledger ADD CONSTRAINT seller_ledger_kind_check CHECK (kind IN ('sale', 'shipping', 'commission', 'refund', 'commission_refund'));
ALTER TABLE seller_ledger ADD COLUMN return_id uuid REFERENCES returns (id);
ALTER TABLE seller_ledger DROP CONSTRAINT seller_ledger_package_id_kind_key;
CREATE UNIQUE INDEX seller_ledger_package_kind_idx ON seller_ledger (package_id, kind) WHERE return_id IS NULL;
CREATE UNIQUE INDEX seller_ledger_return_kind_idx ON seller_ledger (return_id, kind) WHERE return_id IS NOT NULL;
