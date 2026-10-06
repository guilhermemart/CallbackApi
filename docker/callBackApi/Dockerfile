FROM golang:1.25-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
RUN go install github.com/jackc/tern/v2@v2.4.3
COPY . .
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /out/callback-api .

FROM alpine:3.21
RUN apk add --no-cache ca-certificates
WORKDIR /app
ENV HTTP_ADDRESS=:8080
EXPOSE 8080
COPY --from=build /out/callback-api /app/callback-api
COPY --from=build /go/bin/tern /usr/local/bin/tern
COPY tern.conf /app/tern.conf
COPY migrations /app/migrations
ENTRYPOINT ["/app/callback-api"]
