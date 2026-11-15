CREATE SCHEMA IF NOT EXISTS engagement;

CREATE TABLE engagement.reviews (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id bigint NOT NULL,
    customer_id bigint NOT NULL,
    rating int NOT NULL CHECK (rating BETWEEN 1 AND 5),
    title text NOT NULL,
    body text NOT NULL,
    variant_label text,
    status text NOT NULL DEFAULT 'visible' CHECK (status IN ('visible', 'hidden')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (product_id, customer_id)
);

CREATE INDEX reviews_product_idx ON engagement.reviews (product_id, status, created_at DESC);
CREATE INDEX reviews_status_idx ON engagement.reviews (status, created_at DESC);

CREATE TABLE engagement.questions (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id bigint NOT NULL,
    customer_id bigint NOT NULL,
    body text NOT NULL,
    status text NOT NULL DEFAULT 'visible' CHECK (status IN ('visible', 'hidden')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX questions_product_idx ON engagement.questions (product_id, status, created_at DESC);

CREATE TABLE engagement.answers (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    question_id bigint NOT NULL REFERENCES engagement.questions (id) ON DELETE CASCADE,
    seller_id bigint NOT NULL,
    body text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (question_id, seller_id)
);

CREATE TABLE engagement.wishlist_items (
    customer_id bigint NOT NULL,
    product_id bigint NOT NULL,
    added_price numeric(12, 2),
    alert_price numeric(12, 2),
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (customer_id, product_id)
);

CREATE INDEX wishlist_items_product_idx ON engagement.wishlist_items (product_id);
