ALTER TABLE users DROP CONSTRAINT users_role_check;
ALTER TABLE users ADD CONSTRAINT users_role_check CHECK (role IN ('customer', 'admin', 'seller'));
ALTER TABLE users ADD COLUMN seller_id bigint REFERENCES sellers (id);
ALTER TABLE users ADD CONSTRAINT users_seller_role_check CHECK ((role = 'seller') = (seller_id IS NOT NULL));
CREATE INDEX users_seller_idx ON users (seller_id) WHERE seller_id IS NOT NULL;

ALTER TABLE products DROP CONSTRAINT products_status_check;
ALTER TABLE products ADD CONSTRAINT products_status_check CHECK (status IN ('draft', 'pending_review', 'active', 'rejected', 'archived'));
ALTER TABLE products ADD COLUMN submitted_by_seller_id bigint REFERENCES sellers (id);
ALTER TABLE products ADD COLUMN review_note text;
CREATE INDEX products_submitted_by_idx ON products (submitted_by_seller_id) WHERE submitted_by_seller_id IS NOT NULL;

ALTER TABLE order_packages ADD COLUMN commission_rate numeric(5, 4);
UPDATE order_packages p SET commission_rate = s.commission_rate FROM sellers s WHERE s.id = p.seller_id;
ALTER TABLE order_packages ALTER COLUMN commission_rate SET NOT NULL;

CREATE TABLE payouts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    seller_id bigint NOT NULL REFERENCES sellers (id),
    period_end timestamptz NOT NULL,
    amount numeric(12, 2) NOT NULL,
    entry_count int NOT NULL,
    transfer_reference text,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (seller_id, period_end)
);

CREATE TABLE seller_ledger (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    seller_id bigint NOT NULL REFERENCES sellers (id),
    package_id uuid NOT NULL REFERENCES order_packages (id),
    order_number text NOT NULL,
    kind text NOT NULL CHECK (kind IN ('sale', 'shipping', 'commission')),
    amount numeric(12, 2) NOT NULL,
    available_at timestamptz NOT NULL,
    payout_id bigint REFERENCES payouts (id),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (package_id, kind)
);

CREATE INDEX seller_ledger_open_idx ON seller_ledger (seller_id, available_at) WHERE payout_id IS NULL;
CREATE INDEX seller_ledger_payout_idx ON seller_ledger (payout_id) WHERE payout_id IS NOT NULL;
