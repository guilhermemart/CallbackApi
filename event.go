package main

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"strings"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/redis/go-redis/v9"
)

const (
	eventStream      = "callback:events"
	opStream         = "callback:event-operations"
	indexKey         = "callback:events:index"
	groupName        = "postgres-snapshot"
	consumer         = "postgres-snapshot-worker"
	recentHashPrefix = "callback:event-hashes:"
)

type Event struct {
	ID              string          `json:"id"`
	EventType       string          `json:"eventType"`
	Payload         json.RawMessage `json:"payload"`
	SourceID        string          `json:"sourceId"`
	EventUniqueHash string          `json:"eventUniqueHash"`
	CreatedAt       time.Time       `json:"createdAt"`
	UpdatedAt       time.Time       `json:"updatedAt"`
	DeletedAt       *time.Time      `json:"deletedAt"`
}
type input struct {
	EventType       string          `json:"event_type"`
	EventUniqueHash string          `json:"event_unique_hash"`
	Payload         json.RawMessage `json:"payload"`
}
type EventStore struct {
	redis *redis.Client
	pg    *pgxpool.Pool
}

func (s *EventStore) save(w http.ResponseWriter, r *http.Request) {
	in, ok := decodeInput(w, r)
	if !ok {
		return
	}
	if strings.TrimSpace(in.EventUniqueHash) == "" {
		writeJSON(w, 400, map[string]any{"title": "One or more validation errors occurred.", "status": 400, "errors": map[string][]string{"EventUniqueHash": {"The event_unique_hash field is required."}}})
		return
	}
	now := time.Now().UTC()
	created, _ := payloadString(in.Payload, "created_at")
	t, err := time.Parse(time.RFC3339Nano, created)
	if err != nil {
		writeJSON(w, 400, map[string]any{"title": "One or more validation errors occurred.", "status": 400, "errors": map[string][]string{"Payload": {"created_at must be an ISO 8601 date and time."}}})
		return
	}
	sourceID, _ := payloadString(in.Payload, "source_id")
	e := Event{ID: newUUID(), EventType: in.EventType, Payload: in.Payload, SourceID: sourceID, EventUniqueHash: in.EventUniqueHash, CreatedAt: t.UTC(), UpdatedAt: now}
	recentDuplicate, err := s.hasRecentHash(r.Context(), e.SourceID, e.EventUniqueHash)
	if err != nil {
		log.Printf("recent hash lookup failed: %v", err)
		writeJSON(w, 503, map[string]string{"title": "Deduplication check is unavailable"})
		return
	}
	if recentDuplicate {
		writeJSON(w, 409, map[string]any{"title": "Duplicate event detected", "status": 409, "detail": "event_unique_hash appears in the ten most recent events for this source_id."})
		return
	}
	duplicate, err := s.reserveAndEnqueue(r.Context(), e)
	if err != nil {
		writeJSON(w, 500, map[string]string{"title": "An unexpected error occurred"})
		return
	}
	if duplicate {
		writeJSON(w, 409, map[string]any{"title": "Duplicate event detected", "status": 409, "detail": "event_unique_hash has already been used for this source_id."})
		return
	}
	if err := s.rememberRecentHash(r.Context(), e.SourceID, e.EventUniqueHash); err != nil {
		log.Printf("recent hash cache update failed for %s: %v", e.SourceID, err)
	}
	w.Header().Set("Location", "/v1/events/"+e.ID)
	writeJSON(w, 202, map[string]any{"id": e.ID, "event_type": e.EventType, "event_unique_hash": e.EventUniqueHash, "payload": json.RawMessage(in.Payload), "created_at": e.CreatedAt, "updated_at": e.UpdatedAt, "deleted_at": e.DeletedAt, "status": "queued"})
}

func decodeInput(w http.ResponseWriter, r *http.Request) (input, bool) {
	var in input
	d := json.NewDecoder(http.MaxBytesReader(w, r.Body, 2<<20))
	if err := d.Decode(&in); err != nil {
		writeJSON(w, 400, map[string]any{"title": "One or more validation errors occurred.", "status": 400})
		return in, false
	}
	errs := map[string][]string{}
	if strings.TrimSpace(in.EventType) == "" {
		errs["EventType"] = []string{"The EventType field is required."}
	}
	var p map[string]json.RawMessage
	if len(in.Payload) == 0 || json.Unmarshal(in.Payload, &p) != nil || p == nil {
		errs["Payload"] = []string{"payload must be a JSON object."}
	} else {
		var src []any
		if json.Unmarshal(p["source"], &src) != nil || len(src) == 0 {
			errs["Payload"] = append(errs["Payload"], "source must be a non-empty JSON array.")
		}
		var sid, cb string
		if json.Unmarshal(p["source_id"], &sid) != nil || strings.TrimSpace(sid) == "" {
			errs["Payload"] = append(errs["Payload"], "source_id must be a non-empty string.")
		}
		var data map[string]json.RawMessage
		if json.Unmarshal(p["data"], &data) != nil || data == nil {
			errs["Payload"] = append(errs["Payload"], "data must be a JSON object.")
		}
		if _, err := time.Parse(time.RFC3339Nano, rawString(p["created_at"])); err != nil {
			errs["Payload"] = append(errs["Payload"], "created_at must be an ISO 8601 date and time.")
		}
		if json.Unmarshal(p["created_by"], &cb) != nil || strings.TrimSpace(cb) == "" {
			errs["Payload"] = append(errs["Payload"], "created_by must be a non-empty string.")
		}
	}
	if len(errs) > 0 {
		writeJSON(w, 400, map[string]any{"title": "One or more validation errors occurred.", "status": 400, "errors": errs})
		return in, false
	}
	return in, true
}

func rawString(b json.RawMessage) string { var s string; _ = json.Unmarshal(b, &s); return s }

func payloadString(b json.RawMessage, k string) (string, bool) {
	var p map[string]json.RawMessage
	_ = json.Unmarshal(b, &p)
	var s string
	err := json.Unmarshal(p[k], &s)
	return s, err == nil
}

func (s *EventStore) enqueue(ctx context.Context, e Event) (string, error) {
	b, _ := json.Marshal(e)
	pipe := s.redis.TxPipeline()
	pipe.Del(ctx, "callback:event:deleted:"+e.ID)
	pipe.Set(ctx, "callback:event:"+e.ID, b, 0)
	pipe.ZAdd(ctx, indexKey, redis.Z{Score: float64(e.CreatedAt.UnixMilli()), Member: e.ID})
	streamEntry := pipe.XAdd(ctx, &redis.XAddArgs{Stream: eventStream, Values: map[string]any{"event": string(b)}})
	_, err := pipe.Exec(ctx)
	streamID, _ := streamEntry.Result()
	return streamID, err
}

func recentHashKey(sourceID string) string {
	return recentHashPrefix + fmt.Sprintf("%x", sha256.Sum256([]byte(sourceID)))
}

func (s *EventStore) hasRecentHash(ctx context.Context, sourceID, eventHash string) (bool, error) {
	hashes, err := s.redis.LRange(ctx, recentHashKey(sourceID), -10, -1).Result()
	if err != nil {
		return false, err
	}
	for _, hash := range hashes {
		if hash == eventHash {
			return true, nil
		}
	}
	return false, nil
}

func (s *EventStore) rememberRecentHash(ctx context.Context, sourceID, eventHash string) error {
	key := recentHashKey(sourceID)
	pipe := s.redis.TxPipeline()
	pipe.RPush(ctx, key, eventHash)
	pipe.LTrim(ctx, key, -10, -1)
	_, err := pipe.Exec(ctx)
	return err
}

func (s *EventStore) reserveAndEnqueue(ctx context.Context, e Event) (bool, error) {
	tx, err := s.pg.Begin(ctx)
	if err != nil {
		return false, err
	}
	defer func() { _ = tx.Rollback(ctx) }()
	tag, err := tx.Exec(ctx, `INSERT INTO events("Id",event_type,payload,source_id,event_unique_hash,created_at,updated_at,deleted_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT (source_id,event_unique_hash) DO NOTHING`, e.ID, e.EventType, e.Payload, e.SourceID, e.EventUniqueHash, e.CreatedAt, e.UpdatedAt, e.DeletedAt)
	if err != nil {
		return false, err
	}
	if tag.RowsAffected() == 0 {
		return true, nil
	}
	streamID, enqueueErr := s.enqueue(ctx, e)
	if enqueueErr != nil {
		s.removeQueuedState(ctx, e.ID, streamID)
		return false, enqueueErr
	}
	if err = tx.Commit(ctx); err != nil {
		s.removeQueuedState(ctx, e.ID, streamID)
		return false, err
	}
	return false, nil
}
func (s *EventStore) removeQueuedState(ctx context.Context, id, streamID string) {
	pipe := s.redis.TxPipeline()
	pipe.Del(ctx, "callback:event:"+id)
	pipe.ZRem(ctx, indexKey, id)
	if streamID != "" {
		pipe.XDel(ctx, eventStream, streamID)
	}
	if _, err := pipe.Exec(ctx); err != nil {
		log.Printf("queued event cleanup failed for %s: %v", id, err)
	}
}
func (s *EventStore) state(ctx context.Context, id string) (*Event, error) {
	b, err := s.redis.Get(ctx, "callback:event:"+id).Bytes()
	if err == redis.Nil {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	var e Event
	err = json.Unmarshal(b, &e)
	return &e, err
}
func (s *EventStore) saveState(ctx context.Context, e Event) error {
	b, _ := json.Marshal(e)
	p := s.redis.TxPipeline()
	p.Set(ctx, "callback:event:"+e.ID, b, 0)
	p.ZAdd(ctx, indexKey, redis.Z{Score: float64(e.CreatedAt.UnixMilli()), Member: e.ID})
	_, err := p.Exec(ctx)
	return err
}
func (s *EventStore) op(ctx context.Context, op string, e *Event, id string) error {
	v := map[string]any{"operation": op}
	if e != nil {
		b, _ := json.Marshal(e)
		v["event"] = string(b)
	} else {
		v["event_id"] = id
	}
	_, err := s.redis.XAdd(ctx, &redis.XAddArgs{Stream: opStream, Values: v}).Result()
	return err
}

func hasStreamMessages(streams []redis.XStream) bool {
	for _, stream := range streams {
		if len(stream.Messages) > 0 {
			return true
		}
	}
	return false
}

func (s *EventStore) list(w http.ResponseWriter, r *http.Request) {
	ids, err := s.redis.ZRevRange(r.Context(), indexKey, 0, 99).Result()
	if err != nil {
		writeJSON(w, 500, map[string]string{"error": "internal server error"})
		return
	}
	out := []Event{}
	for _, id := range ids {
		e, err := s.state(r.Context(), id)
		if err == nil && e != nil && e.DeletedAt == nil {
			out = append(out, *e)
		}
	}
	writeJSON(w, 200, out)
}
func (s *EventStore) get(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	e, err := s.state(r.Context(), id)
	if err != nil {
		writeJSON(w, 500, map[string]string{"error": "internal server error"})
		return
	}
	if e != nil {
		_ = s.op(r.Context(), "sync", e, "")
		writeJSON(w, 200, e)
		return
	}
	exists, _ := s.redis.Exists(r.Context(), "callback:event:deleted:"+id).Result()
	if exists > 0 {
		http.NotFound(w, r)
		return
	}
	var dbEvent Event
	var payload string
	err = s.pg.QueryRow(r.Context(), `SELECT "Id",event_type,payload::text,COALESCE(source_id,''),COALESCE(event_unique_hash,''),created_at,updated_at,deleted_at FROM events WHERE "Id"=$1`, id).Scan(&dbEvent.ID, &dbEvent.EventType, &payload, &dbEvent.SourceID, &dbEvent.EventUniqueHash, &dbEvent.CreatedAt, &dbEvent.UpdatedAt, &dbEvent.DeletedAt)
	if err != nil {
		http.NotFound(w, r)
		return
	}
	dbEvent.Payload = []byte(payload)
	_ = s.saveState(r.Context(), dbEvent)
	writeJSON(w, 200, dbEvent)
}
func (s *EventStore) update(w http.ResponseWriter, r *http.Request) {
	in, ok := decodeInput(w, r)
	if !ok {
		return
	}
	e, err := s.state(r.Context(), r.PathValue("id"))
	if err != nil || e == nil {
		http.NotFound(w, r)
		return
	}
	var p map[string]json.RawMessage
	_ = json.Unmarshal(in.Payload, &p)
	p["deleted_at"] = json.RawMessage("null")
	p["deleted_by"] = json.RawMessage("null")
	e.Payload, _ = json.Marshal(p)
	e.EventType = in.EventType
	e.UpdatedAt = time.Now().UTC()
	e.DeletedAt = nil
	if err = s.saveState(r.Context(), *e); err == nil {
		err = s.op(r.Context(), "update", e, "")
	}
	if err != nil {
		writeJSON(w, 500, map[string]string{"error": "internal server error"})
		return
	}
	w.Header().Set("Location", "/v1/events/"+e.ID)
	w.WriteHeader(202)
}
func (s *EventStore) softDelete(w http.ResponseWriter, r *http.Request) {
	e, err := s.state(r.Context(), r.PathValue("id"))
	if err != nil {
		writeJSON(w, 500, map[string]string{"error": "internal server error"})
		return
	}
	if e == nil {
		http.NotFound(w, r)
		return
	}
	if e.DeletedAt != nil {
		writeJSON(w, http.StatusOK, map[string]any{
			"title":  "Event already soft-deleted",
			"status": http.StatusOK,
			"detail": "This event was already soft-deleted. No database operation was queued.",
		})
		return
	}
	now := time.Now().UTC()
	var p map[string]json.RawMessage
	_ = json.Unmarshal(e.Payload, &p)
	stamp, _ := json.Marshal(now)
	p["deleted_at"] = stamp
	p["deleted_by"], _ = json.Marshal("system_action")
	e.Payload, _ = json.Marshal(p)
	e.DeletedAt = &now
	e.UpdatedAt = now
	if err = s.saveState(r.Context(), *e); err == nil {
		err = s.op(r.Context(), "soft_delete", e, "")
	}
	if err != nil {
		writeJSON(w, 500, map[string]string{"error": "internal server error"})
		return
	}
	w.WriteHeader(202)
}
func (s *EventStore) delete(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	e, err := s.state(r.Context(), id)
	if err != nil || e == nil {
		http.NotFound(w, r)
		return
	}
	p := s.redis.TxPipeline()
	p.Set(r.Context(), "callback:event:deleted:"+id, "1", 0)
	p.Del(r.Context(), "callback:event:"+id)
	p.ZRem(r.Context(), indexKey, id)
	_, err = p.Exec(r.Context())
	if err == nil {
		err = s.op(r.Context(), "hard_delete", nil, id)
	}
	if err != nil {
		writeJSON(w, 500, map[string]string{"error": "internal server error"})
		return
	}
	w.WriteHeader(202)
}

func (s *EventStore) runWorker(ctx context.Context) {
	for _, stream := range []string{eventStream, opStream} {
		err := s.redis.XGroupCreateMkStream(ctx, stream, groupName, "0").Err()
		if err != nil && !strings.Contains(err.Error(), "BUSYGROUP") {
			log.Printf("redis group init: %v", err)
		}
	}
	for ctx.Err() == nil {
		n := 0
		for _, stream := range []string{eventStream, opStream} {
			entries, err := s.redis.XReadGroup(ctx, &redis.XReadGroupArgs{Group: groupName, Consumer: consumer, Streams: []string{stream, "0"}, Count: 10}).Result()
			if (err == nil || err == redis.Nil) && !hasStreamMessages(entries) {
				entries, err = s.redis.XReadGroup(ctx, &redis.XReadGroupArgs{Group: groupName, Consumer: consumer, Streams: []string{stream, ">"}, Count: 10, Block: time.Second}).Result()
			}
			if err != nil && err != redis.Nil && ctx.Err() == nil {
				log.Printf("stream read: %v", err)
				continue
			}
			for _, batch := range entries {
				for _, msg := range batch.Messages {
					var err error
					if stream == eventStream {
						err = s.persistEvent(ctx, msg)
					} else {
						err = s.applyOperation(ctx, msg)
					}
					if err == nil {
						_ = s.redis.XAck(ctx, stream, groupName, msg.ID).Err()
						n++
					} else {
						log.Printf("snapshot operation failed: %v", err)
					}
				}
			}
		}
		if n == 0 {
			time.Sleep(time.Second)
		}
	}
}
func (s *EventStore) persistEvent(ctx context.Context, m redis.XMessage) error {
	e, ok := m.Values["event"].(string)
	if !ok {
		return fmt.Errorf("event stream missing event")
	}
	var queued Event
	if err := json.Unmarshal([]byte(e), &queued); err != nil {
		return err
	}
	current, err := s.state(ctx, queued.ID)
	if err != nil {
		return err
	}
	if current == nil {
		return nil
	}
	_, err = s.pg.Exec(ctx, `INSERT INTO events("Id",event_type,payload,source_id,event_unique_hash,created_at,updated_at,deleted_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT("Id") DO NOTHING`, current.ID, current.EventType, current.Payload, current.SourceID, current.EventUniqueHash, current.CreatedAt, current.UpdatedAt, current.DeletedAt)
	return err
}
func (s *EventStore) applyOperation(ctx context.Context, m redis.XMessage) error {
	op, _ := m.Values["operation"].(string)
	if op == "hard_delete" {
		id, _ := m.Values["event_id"].(string)
		_, err := s.pg.Exec(ctx, `DELETE FROM events WHERE "Id"=$1`, id)
		if err == nil {
			err = s.redis.Del(ctx, "callback:event:deleted:"+id).Err()
		}
		return err
	}
	raw, _ := m.Values["event"].(string)
	var e Event
	if err := json.Unmarshal([]byte(raw), &e); err != nil {
		return err
	}
	switch op {
	case "update":
		_, err := s.pg.Exec(ctx, `UPDATE events SET event_type=$2,payload=$3,updated_at=$4,deleted_at=NULL WHERE "Id"=$1`, e.ID, e.EventType, e.Payload, e.UpdatedAt)
		return err
	case "soft_delete":
		_, err := s.pg.Exec(ctx, `UPDATE events SET payload=$2,updated_at=$3,deleted_at=$4 WHERE "Id"=$1 AND deleted_at IS NULL`, e.ID, e.Payload, e.UpdatedAt, e.DeletedAt)
		return err
	case "sync":
		_, err := s.pg.Exec(ctx, `UPDATE events SET event_type=$2,payload=$3,updated_at=$4,deleted_at=$5 WHERE "Id"=$1 AND updated_at<>$4`, e.ID, e.EventType, e.Payload, e.UpdatedAt, e.DeletedAt)
		return err
	}
	return fmt.Errorf("unknown operation %q", op)
}
func newUUID() string {
	b := make([]byte, 16)
	if _, err := randRead(b); err != nil {
		panic(err)
	}
	b[6] = (b[6] & 0x0f) | 0x40
	b[8] = (b[8] & 0x3f) | 0x80
	h := fmt.Sprintf("%x", b)
	return h[:8] + "-" + h[8:12] + "-" + h[12:16] + "-" + h[16:20] + "-" + h[20:]
}

var randRead = rand.Read
