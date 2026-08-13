IMAGE_NAME=callback-postgres
CONTAINER_NAME=callback-postgres
ENV_FILE=.env
PORT=5432
VOLUME=postgres_data

build:
	docker build -t $(IMAGE_NAME) .

run:
	docker run -d \
		--name $(CONTAINER_NAME) \
		--env-file $(ENV_FILE) \
		-p $(PORT):5432 \
		-v $(VOLUME):/var/lib/postgresql/data \
		$(IMAGE_NAME)

stop:
	docker stop $(CONTAINER_NAME)

start:
	docker start $(CONTAINER_NAME)

logs:
	docker logs -f $(CONTAINER_NAME)

remove:
	docker rm -f $(CONTAINER_NAME)

reset: remove
	docker volume rm $(VOLUME)
