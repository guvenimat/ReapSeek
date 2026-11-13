CREATE UNIQUE INDEX orders_payment_reference_idx ON orders (payment_reference) WHERE payment_reference IS NOT NULL;

ALTER TABLE orders ADD COLUMN refund_notified_at timestamptz;
ALTER TABLE order_packages ADD COLUMN refund_notified_at timestamptz;

UPDATE orders SET refund_notified_at = refunded_at WHERE refunded_at IS NOT NULL;
UPDATE order_packages SET refund_notified_at = refunded_at WHERE refunded_at IS NOT NULL;
