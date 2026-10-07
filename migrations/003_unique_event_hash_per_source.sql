DROP INDEX IF EXISTS events_deduplication_window_idx;

CREATE UNIQUE INDEX events_source_id_hash_uidx
    ON events (source_id, event_unique_hash);
