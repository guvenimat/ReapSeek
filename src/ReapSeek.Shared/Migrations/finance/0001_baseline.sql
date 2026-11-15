CREATE SCHEMA IF NOT EXISTS finance;

CREATE TABLE finance.payouts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    seller_id bigint NOT NULL,
    period_end timestamptz NOT NULL,
    amount numeric(12, 2) NOT NULL,
    entry_count int NOT NULL,
    transfer_reference text,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (seller_id, period_end)
);

CREATE TABLE finance.seller_ledger (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    seller_id bigint NOT NULL,
    package_id uuid NOT NULL,
    order_number text NOT NULL,
    kind text NOT NULL,
    amount numeric(12, 2) NOT NULL,
    available_at timestamptz NOT NULL,
    payout_id bigint REFERENCES finance.payouts (id),
    created_at timestamptz NOT NULL DEFAULT now(),
    return_id uuid,
    CONSTRAINT seller_ledger_kind_check CHECK (kind IN ('sale', 'shipping', 'commission', 'refund', 'commission_refund'))
);

CREATE INDEX seller_ledger_open_idx ON finance.seller_ledger (seller_id, available_at) WHERE payout_id IS NULL;
CREATE INDEX seller_ledger_payout_idx ON finance.seller_ledger (payout_id) WHERE payout_id IS NOT NULL;
CREATE UNIQUE INDEX seller_ledger_package_kind_idx ON finance.seller_ledger (package_id, kind) WHERE return_id IS NULL;
CREATE UNIQUE INDEX seller_ledger_return_kind_idx ON finance.seller_ledger (return_id, kind) WHERE return_id IS NOT NULL;
