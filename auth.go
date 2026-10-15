package main

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/mail"
	"strings"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
	"golang.org/x/crypto/argon2"
)

const (
	sessionCookieName = "callback_session"
	sessionLifetime   = 7 * 24 * time.Hour
	passwordMinBytes  = 12
	passwordMaxBytes  = 1024
	argonMemory       = 64 * 1024
	argonIterations   = 3
	argonParallelism  = 2
	argonSaltLength   = 16
	argonKeyLength    = 32
)

type authUser struct {
	ID    string `json:"id"`
	Email string `json:"email"`
}

type authContextKey struct{}

type authService struct {
	pool          *pgxpool.Pool
	secureCookies bool
}

func (a *authService) register(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Cache-Control", "no-store")
	var input struct {
		Email    string `json:"email"`
		Password string `json:"password"`
	}
	if err := decodeJSON(r, &input); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "Invalid request body."})
		return
	}
	email, err := normalizeEmail(input.Email)
	if err != nil || len(input.Password) < passwordMinBytes || len(input.Password) > passwordMaxBytes {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "Enter a valid email and a password between 12 and 1024 bytes."})
		return
	}

	passwordHash, err := hashPassword(input.Password)
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "Could not create account."})
		return
	}
	var user authUser
	err = a.pool.QueryRow(r.Context(),
		`INSERT INTO auth_users (email, password_hash) VALUES ($1, $2) RETURNING id, email`,
		email, passwordHash,
	).Scan(&user.ID, &user.Email)
	if err != nil {
		if isUniqueViolation(err) {
			writeJSON(w, http.StatusConflict, map[string]string{"error": "An account with this email already exists."})
			return
		}
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "Could not create account."})
		return
	}
	if err := a.createSession(w, r, user); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "Account created, but could not start a session. Please sign in."})
		return
	}
	writeJSON(w, http.StatusCreated, user)
}

func (a *authService) login(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Cache-Control", "no-store")
	var input struct {
		Email    string `json:"email"`
		Password string `json:"password"`
	}
	if err := decodeJSON(r, &input); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "Invalid email or password."})
		return
	}
	email, err := normalizeEmail(input.Email)
	if err != nil || len(input.Password) > passwordMaxBytes {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "Invalid email or password."})
		return
	}
	var user authUser
	var encodedHash string
	err = a.pool.QueryRow(r.Context(),
		`SELECT id, email, password_hash FROM auth_users WHERE email = $1`, email,
	).Scan(&user.ID, &user.Email, &encodedHash)
	if errors.Is(err, pgx.ErrNoRows) || err == nil && !verifyPassword(input.Password, encodedHash) {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "Invalid email or password."})
		return
	}
	if err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "Could not sign in."})
		return
	}
	if err := a.createSession(w, r, user); err != nil {
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "Could not start a session."})
		return
	}
	writeJSON(w, http.StatusOK, user)
}

func (a *authService) me(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Cache-Control", "no-store")
	user, ok := r.Context().Value(authContextKey{}).(authUser)
	if !ok {
		writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "Authentication required."})
		return
	}
	writeJSON(w, http.StatusOK, user)
}

func (a *authService) logout(w http.ResponseWriter, r *http.Request) {
	if cookie, err := r.Cookie(sessionCookieName); err == nil {
		hash := sha256.Sum256([]byte(cookie.Value))
		_, _ = a.pool.Exec(r.Context(), `DELETE FROM auth_sessions WHERE token_hash = $1`, hash[:])
	}
	http.SetCookie(w, &http.Cookie{
		Name: sessionCookieName, Value: "", Path: "/v1", MaxAge: -1,
		HttpOnly: true, Secure: a.secureCookies, SameSite: http.SameSiteLaxMode,
	})
	w.WriteHeader(http.StatusNoContent)
}

func (a *authService) requireSession(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Cache-Control", "no-store")
		cookie, err := r.Cookie(sessionCookieName)
		if err != nil || cookie.Value == "" {
			writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "Authentication required."})
			return
		}
		hash := sha256.Sum256([]byte(cookie.Value))
		var user authUser
		err = a.pool.QueryRow(r.Context(), `
			SELECT u.id, u.email
			FROM auth_sessions s JOIN auth_users u ON u.id = s.user_id
			WHERE s.token_hash = $1 AND s.expires_at > now()`, hash[:],
		).Scan(&user.ID, &user.Email)
		if errors.Is(err, pgx.ErrNoRows) {
			writeJSON(w, http.StatusUnauthorized, map[string]string{"error": "Authentication required."})
			return
		}
		if err != nil {
			writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "Could not validate session."})
			return
		}
		next.ServeHTTP(w, r.WithContext(context.WithValue(r.Context(), authContextKey{}, user)))
	})
}

func (a *authService) createSession(w http.ResponseWriter, r *http.Request, user authUser) error {
	secret := make([]byte, 32)
	if _, err := rand.Read(secret); err != nil {
		return err
	}
	token := base64.RawURLEncoding.EncodeToString(secret)
	hash := sha256.Sum256([]byte(token))
	expires := time.Now().Add(sessionLifetime)
	if _, err := a.pool.Exec(r.Context(), `INSERT INTO auth_sessions (token_hash, user_id, expires_at) VALUES ($1, $2, $3)`, hash[:], user.ID, expires); err != nil {
		return err
	}
	http.SetCookie(w, &http.Cookie{
		Name: sessionCookieName, Value: token, Path: "/v1", Expires: expires,
		HttpOnly: true, Secure: a.secureCookies, SameSite: http.SameSiteLaxMode,
	})
	return nil
}

func normalizeEmail(value string) (string, error) {
	value = strings.ToLower(strings.TrimSpace(value))
	address, err := mail.ParseAddress(value)
	if err != nil || address.Address != value || len(value) > 254 {
		return "", errors.New("invalid email")
	}
	return value, nil
}

func decodeJSON(r *http.Request, destination any) error {
	decoder := json.NewDecoder(io.LimitReader(r.Body, 1<<20))
	if err := decoder.Decode(destination); err != nil {
		return err
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		return errors.New("request must contain a single JSON value")
	}
	return nil
}

func hashPassword(password string) (string, error) {
	salt := make([]byte, argonSaltLength)
	if _, err := rand.Read(salt); err != nil {
		return "", err
	}
	key := argon2.IDKey([]byte(password), salt, argonIterations, argonMemory, argonParallelism, argonKeyLength)
	return fmt.Sprintf("argon2id$v=19$m=%d,t=%d,p=%d$%s$%s", argonMemory, argonIterations, argonParallelism,
		base64.RawStdEncoding.EncodeToString(salt), base64.RawStdEncoding.EncodeToString(key)), nil
}

func verifyPassword(password, encoded string) bool {
	var version, memory, iterations, parallelism uint32
	if _, err := fmt.Sscanf(encoded, "argon2id$v=%d$m=%d,t=%d,p=%d$", &version, &memory, &iterations, &parallelism); err != nil || version != 19 {
		return false
	}
	parts := strings.Split(encoded, "$")
	if len(parts) != 5 {
		return false
	}
	salt, err := base64.RawStdEncoding.DecodeString(parts[3])
	if err != nil || len(salt) < 8 || memory != argonMemory || iterations != argonIterations || parallelism != argonParallelism {
		return false
	}
	want, err := base64.RawStdEncoding.DecodeString(parts[4])
	if err != nil || len(want) != argonKeyLength {
		return false
	}
	got := argon2.IDKey([]byte(password), salt, iterations, memory, uint8(parallelism), uint32(len(want)))
	return subtle.ConstantTimeCompare(got, want) == 1
}

func isUniqueViolation(err error) bool {
	var pgErr interface{ SQLState() string }
	return errors.As(err, &pgErr) && pgErr.SQLState() == "23505"
}
