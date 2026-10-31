ALTER TABLE users ADD COLUMN email_verified_at timestamptz;

UPDATE users SET email_verified_at = created_at;

CREATE TABLE account_tokens (
    token_hash text PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    purpose text NOT NULL CHECK (purpose IN ('password_reset', 'email_verification')),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    used_at timestamptz
);

CREATE INDEX account_tokens_user_idx ON account_tokens (user_id, purpose, created_at DESC);
