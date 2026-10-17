ALTER TABLE events
    ADD COLUMN user_id uuid;

DO $$
DECLARE
    account_count integer;
BEGIN
    IF EXISTS (SELECT 1 FROM events WHERE user_id IS NULL) THEN
        SELECT count(*) INTO account_count FROM auth_users;

        IF account_count <> 1 THEN
            RAISE EXCEPTION
                'Cannot assign ownership to existing events automatically: expected exactly one account, found %',
                account_count;
        END IF;

        UPDATE events
        SET user_id = (SELECT id FROM auth_users)
        WHERE user_id IS NULL;
    END IF;
END
$$;

ALTER TABLE events
    ALTER COLUMN user_id SET NOT NULL,
    ADD CONSTRAINT events_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES auth_users(id);

DROP INDEX IF EXISTS events_source_id_hash_uidx;

CREATE UNIQUE INDEX events_user_source_hash_uidx
    ON events (user_id, source_id, event_unique_hash);

CREATE INDEX events_user_created_at_idx
    ON events (user_id, created_at DESC, "Id" DESC);
