package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/cookiejar"
	"net/url"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/stretchr/testify/require"
	"github.com/stretchr/testify/suite"
)

// Run against a running API with CALLBACK_API_TEST_URL, for example:
// $env:CALLBACK_API_TEST_URL = "http://localhost:8080"; go test ./...
type eventOwnershipAPISuite struct {
	suite.Suite
	baseURL  string
	clientA  *http.Client
	clientA2 *http.Client
	clientB  *http.Client
	emailA   string
	emailB   string
	password string
	eventID  string
}

func TestEventOwnershipAPI(t *testing.T) {
	suite.Run(t, new(eventOwnershipAPISuite))
}

func (s *eventOwnershipAPISuite) SetupSuite() {
	s.baseURL = strings.TrimRight(os.Getenv("CALLBACK_API_TEST_URL"), "/")
	if s.baseURL == "" {
		s.T().Skip("set CALLBACK_API_TEST_URL to run API integration tests")
	}
	if _, err := url.ParseRequestURI(s.baseURL); err != nil {
		s.T().Fatalf("invalid CALLBACK_API_TEST_URL: %v", err)
	}
	client := func() *http.Client {
		jar, err := cookiejar.New(nil)
		require.NoError(s.T(), err)
		return &http.Client{Timeout: 10 * time.Second, Jar: jar}
	}
	s.clientA, s.clientA2, s.clientB = client(), client(), client()

	response := s.request(s.clientA, http.MethodGet, "/v1/health/ready", nil)
	require.Equal(s.T(), http.StatusOK, response.status,
		"API must be ready before running integration tests; response: %s", response.body)
}

func (s *eventOwnershipAPISuite) TestAuthenticationAndEventCRUDAreAccountScoped() {
	require := require.New(s.T())
	suffix := fmt.Sprintf("%d", time.Now().UnixNano())
	s.emailA = "api-integration-a-" + suffix + "@example.test"
	s.emailB = "api-integration-b-" + suffix + "@example.test"
	s.password = "Integration-Test-" + suffix

	// Event endpoints must reject requests that have no session cookie.
	unauthenticated := s.request(s.clientA, http.MethodGet, "/v1/events/list", nil)
	require.Equal(http.StatusUnauthorized, unauthenticated.status)
	require.Equal(http.StatusUnauthorized, s.request(s.clientA, http.MethodDelete, "/v1/auth/account", nil).status)

	// Register through the API, log out, then authenticate with the API login route.
	registered := s.request(s.clientA, http.MethodPost, "/v1/auth/register", map[string]string{
		"email": s.emailA, "password": s.password,
	})
	require.Equal(http.StatusCreated, registered.status, registered.body)
	var accountA struct {
		ID string `json:"id"`
	}
	require.NoError(json.Unmarshal([]byte(registered.body), &accountA))
	require.NotEmpty(accountA.ID)

	loggedOut := s.request(s.clientA, http.MethodPost, "/v1/auth/logout", nil)
	require.Equal(http.StatusNoContent, loggedOut.status)
	require.Equal(http.StatusUnauthorized, s.request(s.clientA, http.MethodGet, "/v1/auth/me", nil).status)
	login := s.request(s.clientA, http.MethodPost, "/v1/auth/login", map[string]string{
		"email": s.emailA, "password": s.password,
	})
	require.Equal(http.StatusOK, login.status, login.body)
	me := s.request(s.clientA, http.MethodGet, "/v1/auth/me", nil)
	require.Equal(http.StatusOK, me.status, me.body)
	secondSession := s.request(s.clientA2, http.MethodPost, "/v1/auth/login", map[string]string{
		"email": s.emailA, "password": s.password,
	})
	require.Equal(http.StatusOK, secondSession.status, secondSession.body)
	require.Equal(http.StatusOK, s.request(s.clientA2, http.MethodGet, "/v1/auth/me", nil).status)

	// Create and read an event as its owner.
	payload := map[string]any{
		"source":     []string{"go-integration-test"},
		"source_id":  "integration-" + suffix,
		"created_at": time.Now().UTC().Format(time.RFC3339Nano),
		"created_by": "testify",
		"data":       map[string]any{"state": "created"},
	}
	created := s.request(s.clientA, http.MethodPost, "/v1/event/save", map[string]any{
		"event_type": "integration.created", "event_unique_hash": "hash-" + suffix, "payload": payload,
	})
	require.Equal(http.StatusAccepted, created.status, created.body)
	var createdEvent struct {
		ID string `json:"id"`
	}
	require.NoError(json.Unmarshal([]byte(created.body), &createdEvent))
	require.NotEmpty(createdEvent.ID)
	s.eventID = createdEvent.ID

	list := s.request(s.clientA, http.MethodGet, "/v1/events/list", nil)
	require.Equal(http.StatusOK, list.status, list.body)
	var events []struct {
		ID     string `json:"id"`
		UserID string `json:"userId"`
	}
	require.NoError(json.Unmarshal([]byte(list.body), &events))
	require.Contains(eventIDs(events), s.eventID)

	got := s.request(s.clientA, http.MethodGet, "/v1/event/get/"+s.eventID, nil)
	require.Equal(http.StatusOK, got.status, got.body)
	var event struct {
		ID        string `json:"id"`
		UserID    string `json:"userId"`
		EventType string `json:"eventType"`
	}
	require.NoError(json.Unmarshal([]byte(got.body), &event))
	require.Equal(s.eventID, event.ID)
	require.Equal(accountA.ID, event.UserID)

	// A different account must not list, retrieve, update, soft-delete, or delete this event.
	otherAccount := s.request(s.clientB, http.MethodPost, "/v1/auth/register", map[string]string{
		"email": s.emailB, "password": s.password,
	})
	require.Equal(http.StatusCreated, otherAccount.status, otherAccount.body)
	otherList := s.request(s.clientB, http.MethodGet, "/v1/events/list", nil)
	require.Equal(http.StatusOK, otherList.status, otherList.body)
	require.NotContains(otherList.body, s.eventID)
	require.Equal(http.StatusNotFound, s.request(s.clientB, http.MethodGet, "/v1/event/get/"+s.eventID, nil).status)
	updateBody := map[string]any{
		"event_type": "integration.foreign-update", "event_unique_hash": "hash-" + suffix, "payload": payload,
	}
	require.Equal(http.StatusNotFound, s.request(s.clientB, http.MethodPost, "/v1/event/update/"+s.eventID, updateBody).status)
	require.Equal(http.StatusNotFound, s.request(s.clientB, http.MethodPatch, "/v1/events/softdelete/"+s.eventID, nil).status)
	require.Equal(http.StatusNotFound, s.request(s.clientB, http.MethodDelete, "/v1/events/delete/"+s.eventID, nil).status)

	// The owner can update, soft-delete, and permanently delete the event.
	payload["data"] = map[string]any{"state": "updated"}
	updated := s.request(s.clientA, http.MethodPost, "/v1/event/update/"+s.eventID, map[string]any{
		"event_type": "integration.updated", "event_unique_hash": "hash-" + suffix, "payload": payload,
	})
	require.Equal(http.StatusAccepted, updated.status, updated.body)
	updatedEvent := s.request(s.clientA, http.MethodGet, "/v1/event/get/"+s.eventID, nil)
	require.Equal(http.StatusOK, updatedEvent.status, updatedEvent.body)
	var updatedValue struct {
		EventType string `json:"eventType"`
	}
	require.NoError(json.Unmarshal([]byte(updatedEvent.body), &updatedValue))
	require.Equal("integration.updated", updatedValue.EventType)

	softDeleted := s.request(s.clientA, http.MethodPatch, "/v1/events/softdelete/"+s.eventID, nil)
	require.Equal(http.StatusAccepted, softDeleted.status, softDeleted.body)
	permanentlyDeleted := s.request(s.clientA, http.MethodDelete, "/v1/events/delete/"+s.eventID, nil)
	require.Equal(http.StatusAccepted, permanentlyDeleted.status, permanentlyDeleted.body)
	s.eventID = "" // Hard delete has been queued; cleanup no longer needs the event ID.
	accountEvent := s.request(s.clientA, http.MethodPost, "/v1/event/save", map[string]any{
		"event_type": "integration.account-delete", "event_unique_hash": "account-delete-" + suffix,
		"payload": map[string]any{
			"source": []string{"go-integration-test"}, "source_id": "account-delete-" + suffix,
			"created_at": time.Now().UTC().Format(time.RFC3339Nano), "created_by": "testify",
			"data": map[string]any{"state": "will-be-deleted-with-account"},
		},
	})
	require.Equal(http.StatusAccepted, accountEvent.status, accountEvent.body)
	var accountEventValue struct {
		ID string `json:"id"`
	}
	require.NoError(json.Unmarshal([]byte(accountEvent.body), &accountEventValue))
	require.NotEmpty(accountEventValue.ID)

	deletedA := s.request(s.clientA, http.MethodDelete, "/v1/auth/account", nil)
	require.Equal(http.StatusNoContent, deletedA.status, deletedA.body)
	require.Equal(http.StatusUnauthorized, s.request(s.clientA, http.MethodGet, "/v1/auth/me", nil).status)
	require.Equal(http.StatusUnauthorized, s.request(s.clientA2, http.MethodGet, "/v1/auth/me", nil).status)
	require.Equal(http.StatusNotFound, s.request(s.clientB, http.MethodGet, "/v1/event/get/"+accountEventValue.ID, nil).status)
	deletedB := s.request(s.clientB, http.MethodDelete, "/v1/auth/account", nil)
	require.Equal(http.StatusNoContent, deletedB.status, deletedB.body)
}

func (s *eventOwnershipAPISuite) TearDownTest() {
	// Best-effort cleanup removes any event and invalidates both sessions.
	if s.eventID != "" && s.clientA != nil {
		_ = s.request(s.clientA, http.MethodDelete, "/v1/events/delete/"+s.eventID, nil)
	}
	if s.clientA != nil && s.emailA != "" {
		_ = s.request(s.clientA, http.MethodDelete, "/v1/auth/account", nil)
	}
	if s.clientA2 != nil {
		_ = s.request(s.clientA2, http.MethodPost, "/v1/auth/logout", nil)
	}
	if s.clientB != nil && s.emailB != "" {
		_ = s.request(s.clientB, http.MethodDelete, "/v1/auth/account", nil)
	}
}

type apiResponse struct {
	status int
	body   string
}

func (s *eventOwnershipAPISuite) request(client *http.Client, method, path string, body any) apiResponse {
	var requestBody io.Reader
	if body != nil {
		encoded, err := json.Marshal(body)
		require.NoError(s.T(), err)
		requestBody = bytes.NewReader(encoded)
	}
	request, err := http.NewRequest(method, s.baseURL+path, requestBody)
	require.NoError(s.T(), err)
	if body != nil {
		request.Header.Set("Content-Type", "application/json")
	}
	response, err := client.Do(request)
	require.NoError(s.T(), err)
	defer response.Body.Close()
	responseBytes, err := io.ReadAll(response.Body)
	require.NoError(s.T(), err)
	return apiResponse{status: response.StatusCode, body: string(responseBytes)}
}

func eventIDs(events []struct {
	ID     string `json:"id"`
	UserID string `json:"userId"`
}) []string {
	ids := make([]string, 0, len(events))
	for _, event := range events {
		ids = append(ids, event.ID)
	}
	return ids
}
