CREATE TABLE users (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email text NOT NULL UNIQUE,
    name text NOT NULL,
    password_hash text NOT NULL,
    role text NOT NULL DEFAULT 'customer' CHECK (role IN ('customer', 'admin')),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE sessions (
    token_hash text PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    audience text NOT NULL CHECK (audience IN ('storefront', 'backoffice')),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL
);

CREATE INDEX sessions_user_idx ON sessions (user_id);

CREATE TABLE outbox (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    type text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    available_at timestamptz NOT NULL DEFAULT now(),
    attempts int NOT NULL DEFAULT 0,
    last_error text,
    processed_at timestamptz,
    failed_at timestamptz
);

CREATE INDEX outbox_pending_idx ON outbox (available_at, id) WHERE processed_at IS NULL AND failed_at IS NULL;
