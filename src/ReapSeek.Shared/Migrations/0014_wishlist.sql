CREATE TABLE wishlist_items (
    customer_id bigint NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    product_id bigint NOT NULL REFERENCES products (id) ON DELETE CASCADE,
    added_price numeric(12, 2),
    alert_price numeric(12, 2),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, product_id)
);

CREATE INDEX wishlist_items_product_idx ON wishlist_items (product_id);
