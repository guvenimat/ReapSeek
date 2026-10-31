CREATE TABLE reviews (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id bigint NOT NULL REFERENCES products (id),
    customer_id bigint NOT NULL REFERENCES users (id),
    rating int NOT NULL CHECK (rating BETWEEN 1 AND 5),
    title text NOT NULL,
    body text NOT NULL,
    variant_label text,
    status text NOT NULL DEFAULT 'visible' CHECK (status IN ('visible', 'hidden')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (product_id, customer_id)
);

CREATE INDEX reviews_product_idx ON reviews (product_id, status, created_at DESC);
CREATE INDEX reviews_status_idx ON reviews (status, created_at DESC);

CREATE TABLE questions (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id bigint NOT NULL REFERENCES products (id),
    customer_id bigint NOT NULL REFERENCES users (id),
    body text NOT NULL,
    status text NOT NULL DEFAULT 'visible' CHECK (status IN ('visible', 'hidden')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX questions_product_idx ON questions (product_id, status, created_at DESC);

CREATE TABLE answers (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    question_id bigint NOT NULL REFERENCES questions (id) ON DELETE CASCADE,
    seller_id bigint NOT NULL REFERENCES sellers (id),
    body text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (question_id, seller_id)
);
