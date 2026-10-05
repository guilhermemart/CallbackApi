FROM golang:1.24-alpine AS build
WORKDIR /src
COPY go.mod ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /out/callback-api .

FROM alpine:3.21
RUN apk add --no-cache ca-certificates
WORKDIR /app
ENV HTTP_ADDRESS=:8080
EXPOSE 8080
COPY --from=build /out/callback-api /app/callback-api
ENTRYPOINT ["/app/callback-api"]
