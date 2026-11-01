ALTER TABLE order_packages ADD COLUMN ship_reminded_at timestamptz;
ALTER TABLE order_packages ADD COLUMN cancel_reason text;
ALTER TABLE order_packages ADD COLUMN refunded_at timestamptz;
ALTER TABLE order_packages ADD COLUMN refund_reference text;

CREATE INDEX order_packages_preparing_idx ON order_packages (order_id) WHERE status = 'preparing';
