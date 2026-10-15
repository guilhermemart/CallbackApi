CREATE TABLE IF NOT EXISTS auth_users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email text NOT NULL,
    password_hash text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT auth_users_email_unique UNIQUE (email)
);

CREATE TABLE IF NOT EXISTS auth_sessions (
    token_hash bytea PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES auth_users(id) ON DELETE CASCADE,
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS auth_sessions_user_id_idx ON auth_sessions (user_id);
CREATE INDEX IF NOT EXISTS auth_sessions_expires_at_idx ON auth_sessions (expires_at);
