CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE identity.users (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email text NOT NULL UNIQUE,
    name text NOT NULL,
    password_hash text NOT NULL,
    role text NOT NULL DEFAULT 'customer',
    created_at timestamptz NOT NULL DEFAULT now(),
    seller_id bigint,
    suspended_at timestamptz,
    suspended_reason text,
    email_verified_at timestamptz,
    CONSTRAINT users_role_check CHECK (role IN ('customer', 'admin', 'seller')),
    CONSTRAINT users_seller_role_check CHECK ((role = 'seller') = (seller_id IS NOT NULL))
);

CREATE INDEX users_seller_idx ON identity.users (seller_id) WHERE seller_id IS NOT NULL;

CREATE TABLE identity.sessions (
    token_hash text PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES identity.users (id) ON DELETE CASCADE,
    audience text NOT NULL CHECK (audience IN ('storefront', 'backoffice')),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL
);

CREATE INDEX sessions_user_idx ON identity.sessions (user_id);

CREATE TABLE identity.account_tokens (
    token_hash text PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES identity.users (id) ON DELETE CASCADE,
    purpose text NOT NULL CHECK (purpose IN ('password_reset', 'email_verification')),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    used_at timestamptz
);

CREATE INDEX account_tokens_user_idx ON identity.account_tokens (user_id, purpose, created_at DESC);
