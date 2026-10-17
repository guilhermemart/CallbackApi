package main

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"time"

	"github.com/redis/go-redis/v9"
)

const eventNotificationPrefix = "callback:events:user:"

type eventNotification struct {
	Name string          `json:"name"`
	Data json.RawMessage `json:"data"`
}

func eventNotificationChannel(userID string) string {
	return eventNotificationPrefix + userID
}

// stream subscribes an authenticated account to its new event notifications.
func (s *EventStore) stream(w http.ResponseWriter, r *http.Request) {
	userID, ok := eventRequestUserID(w, r)
	if !ok {
		return
	}
	if _, ok := w.(http.Flusher); !ok {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "Streaming is not supported."})
		return
	}

	pubsub := s.redis.Subscribe(r.Context(), eventNotificationChannel(userID))
	defer func() { _ = pubsub.Close() }()
	if _, err := pubsub.Receive(r.Context()); err != nil {
		writeJSON(w, http.StatusServiceUnavailable, map[string]string{"error": "Event stream is unavailable."})
		return
	}

	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache, no-transform")
	w.Header().Set("Connection", "keep-alive")
	w.Header().Set("X-Accel-Buffering", "no")
	w.WriteHeader(http.StatusOK)
	flusher := w.(http.Flusher)
	_, _ = fmt.Fprint(w, "retry: 3000\nevent: ready\ndata: {}\n\n")
	flusher.Flush()

	ticker := time.NewTicker(25 * time.Second)
	defer ticker.Stop()
	messages := pubsub.Channel(redis.WithChannelSize(32))
	for {
		select {
		case <-r.Context().Done():
			return
		case message, open := <-messages:
			if !open {
				return
			}
			var notification eventNotification
			if err := json.Unmarshal([]byte(message.Payload), &notification); err != nil || !allowedEventNotification(notification.Name) || !json.Valid(notification.Data) {
				continue
			}
			if err := writeSSE(w, flusher, notification.Name, string(notification.Data)); err != nil {
				return
			}
		case <-ticker.C:
			if _, err := fmt.Fprint(w, ": keep-alive\n\n"); err != nil {
				return
			}
			flusher.Flush()
		}
	}
}

func writeSSE(w http.ResponseWriter, flusher http.Flusher, eventName, data string) error {
	if !json.Valid([]byte(data)) {
		return fmt.Errorf("invalid SSE event payload")
	}
	if _, err := fmt.Fprintf(w, "event: %s\ndata: %s\n\n", eventName, data); err != nil {
		return err
	}
	flusher.Flush()
	return nil
}

func publishNewEvent(ctx context.Context, client *redis.Client, event Event) error {
	return publishEventNotification(ctx, client, event.UserID, "new_event", event)
}

func publishEventNotification(ctx context.Context, client *redis.Client, userID, eventName string, data any) error {
	serializedData, err := json.Marshal(data)
	if err != nil {
		return err
	}
	notification, err := json.Marshal(eventNotification{Name: eventName, Data: serializedData})
	if err != nil {
		return err
	}
	return client.Publish(ctx, eventNotificationChannel(userID), string(notification)).Err()
}

func allowedEventNotification(name string) bool {
	switch name {
	case "new_event", "event_updated", "event_soft_deleted", "event_deleted":
		return true
	default:
		return false
	}
}
