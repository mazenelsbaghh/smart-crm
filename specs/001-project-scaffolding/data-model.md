# Data Model: Project Scaffolding & DevOps Foundation

Phase 0 has no application-level data model. The data entities are Docker services and their configuration.

## Docker Services

| Service | Image | Ports (Dev) | Health Check | Volume |
|---------|-------|-------------|-------------|--------|
| postgres | pgvector/pgvector:pg16 | 5432:5432 | `pg_isready` | `pgdata:/var/lib/postgresql/data` |
| redis | redis:7-alpine | 6379:6379 | `redis-cli ping` | `redisdata:/data` |
| rabbitmq | rabbitmq:3.13-management-alpine | 5672:5672, 15672:15672 | `rabbitmq-diagnostics -q ping` | `rabbitmqdata:/var/lib/rabbitmq` |
| elasticsearch | elasticsearch:8.14.0 | 9200:9200 | `curl localhost:9200/_cluster/health` | `esdata:/usr/share/elasticsearch/data` |
| minio | minio/minio:latest | 9000:9000, 9001:9001 | `curl localhost:9000/minio/health/live` | `miniodata:/data` |
| nginx | nginx:1.25-alpine | 80:80 | `curl localhost:80` | config bind mount |

## Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| POSTGRES_USER | Database superuser | `smartcore` |
| POSTGRES_PASSWORD | Database password | `changeme` |
| POSTGRES_DB | Default database name | `smartcustomercore` |
| REDIS_URL | Redis connection string | `redis://redis:6379/0` |
| RABBITMQ_DEFAULT_USER | RabbitMQ admin user | `admin` |
| RABBITMQ_DEFAULT_PASS | RabbitMQ admin password | `changeme` |
| ELASTICSEARCH_URL | Elasticsearch endpoint | `http://elasticsearch:9200` |
| MINIO_ROOT_USER | MinIO access key | `minioadmin` |
| MINIO_ROOT_PASSWORD | MinIO secret key | `changeme` |
| MINIO_BUCKET | Default bucket | `smartcore-media` |
