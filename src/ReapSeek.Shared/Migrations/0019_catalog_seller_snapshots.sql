CREATE TABLE catalog.seller_snapshots (
    id bigint PRIMARY KEY,
    name text NOT NULL,
    slug text NOT NULL,
    city text NOT NULL DEFAULT '',
    status text NOT NULL,
    rating numeric(2,1) NOT NULL DEFAULT 0
);

INSERT INTO catalog.seller_snapshots (id, name, slug, city, status, rating)
SELECT id, name, slug, city, status, rating FROM sellers.sellers;

CREATE OR REPLACE VIEW catalog.buy_box AS
SELECT DISTINCT ON (o.variant_id) o.variant_id, o.id AS offer_id, o.seller_id, o.price, o.list_price, o.stock, o.handling_days
FROM catalog.offers o
JOIN catalog.seller_snapshots s ON s.id = o.seller_id
WHERE o.status = 'active' AND o.stock > 0 AND s.status = 'active'
ORDER BY o.variant_id, o.price, s.rating DESC, o.handling_days, o.id;
