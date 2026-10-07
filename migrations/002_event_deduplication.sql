ALTER TABLE events
    ADD COLUMN source_id text,
    ADD COLUMN event_unique_hash text;

UPDATE events
SET source_id = payload ->> 'source_id'
WHERE source_id IS NULL;

CREATE INDEX events_deduplication_window_idx
    ON events (source_id, created_at DESC, "Id" DESC);
