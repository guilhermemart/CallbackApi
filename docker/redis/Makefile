IMAGE_NAME=callback-redis
CONTAINER_NAME=callback-redis
PORT=6379
VOLUME=callback_redis_data

build:
	docker build -t $(IMAGE_NAME) .

run:
	docker run -d \
		--name $(CONTAINER_NAME) \
		-p $(PORT):6379 \
		-v $(VOLUME):/data \
		$(IMAGE_NAME)

stop:
	docker stop $(CONTAINER_NAME)

start:
	docker start $(CONTAINER_NAME)

logs:
	docker logs -f $(CONTAINER_NAME)

