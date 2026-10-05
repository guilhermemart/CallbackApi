package main

import (
	"context"
	"encoding/json"
	"errors"
	"log"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/redis/go-redis/v9"
)

func main() {
	ctx := context.Background()
	pgURL := postgresURL()
	rdb := redis.NewClient(&redis.Options{Addr: env("REDIS_ADDRESS", "redis:6379"), Password: os.Getenv("REDIS_PASSWORD")})
	defer rdb.Close()
	pool, err := pgxpool.New(ctx, pgURL)
	if err != nil { log.Fatal(err) }
	defer pool.Close()
	if len(os.Args)>1 && os.Args[1] == "--migrate" { if err := migrate(ctx,pool); err != nil { log.Fatal(err) }; return }

	store := &EventStore{redis:rdb, pg:pool}
	go store.runWorker(ctx)
	mux := http.NewServeMux()
	mux.HandleFunc("GET /",func(w http.ResponseWriter,r *http.Request){http.Redirect(w,r,"/v1/health",http.StatusTemporaryRedirect)})
	mux.HandleFunc("GET /v1/health",func(w http.ResponseWriter,r *http.Request){writeJSON(w,200,map[string]string{"status":"ok","service":"callback-api"})})
	mux.HandleFunc("GET /v1/health/ready",func(w http.ResponseWriter,r *http.Request){
		c,cancel:=context.WithTimeout(r.Context(),2*time.Second); defer cancel()
		if err:=pool.Ping(c); err!=nil {writeJSON(w,503,map[string]string{"status":"not_ready","database":"unavailable","redis":"unknown"});return}
		if err:=rdb.Ping(c).Err(); err!=nil {writeJSON(w,503,map[string]string{"status":"not_ready","database":"ok","redis":"unavailable"});return}
		writeJSON(w,200,map[string]string{"status":"ready","database":"ok","redis":"ok"})
	})
	mux.HandleFunc("POST /v1/event/save",store.save)
	mux.HandleFunc("GET /v1/events/list",store.list)
	mux.HandleFunc("GET /v1/event/get/{id}",store.get)
	mux.HandleFunc("POST /v1/event/update/{id}",store.update)
	mux.HandleFunc("PATCH /v1/events/softdelete/{id}",store.softDelete)
	mux.HandleFunc("DELETE /v1/events/delete/{id}",store.delete)
	server:=&http.Server{Addr:env("HTTP_ADDRESS",":8080"),Handler:recoverer(timing(mux))}
	sig,cancel:=signal.NotifyContext(ctx,os.Interrupt,syscall.SIGTERM); defer cancel()
	go func(){<-sig.Done(); c,cc:=context.WithTimeout(context.Background(),5*time.Second);defer cc();_ = server.Shutdown(c)}()
	log.Printf("callback-api listening on %s",server.Addr)
	if err:=server.ListenAndServe();err!=nil && !errors.Is(err,http.ErrServerClosed){log.Fatal(err)}
}

func postgresURL() string {
	if u:=os.Getenv("POSTGRES_URL");u!="" {return u}
	if u:=os.Getenv("ConnectionStrings__Postgres");strings.HasPrefix(u,"postgres://")||strings.HasPrefix(u,"postgresql://"){return u}
	host:=env("POSTGRES_HOST","postgres");port:=env("POSTGRES_PORT","5432")
	user:=env("POSTGRES_USER","postgres");password:=os.Getenv("POSTGRES_PASSWORD");db:=env("POSTGRES_DB","postgres")
	return "postgres://"+user+":"+password+"@"+host+":"+port+"/"+db+"?sslmode="+env("POSTGRES_SSLMODE","disable")
}
func env(k,d string)string{if v:=os.Getenv(k);v!=""{return v};return d}
func writeJSON(w http.ResponseWriter,status int,v any){w.Header().Set("Content-Type","application/json");w.WriteHeader(status);_ = json.NewEncoder(w).Encode(v)}
func timing(next http.Handler)http.Handler{return http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){start:=time.Now();rw:=&statusWriter{ResponseWriter:w,status:200};next.ServeHTTP(rw,r);log.Printf("request_completed method=%s path=%s status=%d duration_ms=%d",r.Method,r.URL.Path,rw.status,time.Since(start).Milliseconds())})}
type statusWriter struct{http.ResponseWriter;status int}
func(w *statusWriter)WriteHeader(s int){w.status=s;w.ResponseWriter.WriteHeader(s)}
func recoverer(next http.Handler)http.Handler{return http.HandlerFunc(func(w http.ResponseWriter,r *http.Request){defer func(){if recover()!=nil{writeJSON(w,500,map[string]any{"type":"about:blank","title":"An unexpected error occurred","status":500})}}();next.ServeHTTP(w,r)})}
