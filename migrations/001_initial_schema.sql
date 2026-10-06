CREATE TABLE IF NOT EXISTS events (
    "Id" uuid PRIMARY KEY,
    event_type varchar(200) NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    deleted_at timestamptz NULL
);
