# Research: Project Scaffolding & DevOps Foundation

## Decision: PostgreSQL Version & pgvector

- **Decision**: PostgreSQL 16 with `pgvector/pgvector:pg16` Docker image
- **Rationale**: pgvector 0.7+ supports HNSW indexing for fast vector similarity search; PostgreSQL 16 is latest stable with performance improvements
- **Alternatives**: Separate vector DB (Qdrant, Pinecone) — rejected because pgvector simplifies infrastructure by colocating vector data with relational data

## Decision: Redis Version

- **Decision**: Redis 7 (official `redis:7-alpine` image)
- **Rationale**: Redis 7 supports Redis Functions, improved ACLs, and is the current stable release. Alpine image keeps container small.
- **Alternatives**: Valkey, Dragonfly — rejected for simplicity and ecosystem maturity

## Decision: RabbitMQ Version

- **Decision**: RabbitMQ 3.13 with management plugin (`rabbitmq:3.13-management-alpine`)
- **Rationale**: Management plugin provides web UI for queue inspection; 3.13 is latest stable with Khepri metadata store preview
- **Alternatives**: Kafka — rejected as overkill for single-server deployment

## Decision: Elasticsearch Version

- **Decision**: Elasticsearch 8.x single-node (`elasticsearch:8.14.0`)
- **Rationale**: Version 8.x provides improved search relevancy and built-in security. Single-node mode via `discovery.type=single-node` for development.
- **Alternatives**: OpenSearch — viable but Elasticsearch has wider library support in .NET ecosystem

## Decision: Object Storage

- **Decision**: MinIO (`minio/minio:latest`) as S3-compatible storage
- **Rationale**: API-compatible with AWS S3, allowing future migration. Provides web console for management. Local storage without cloud dependency.
- **Alternatives**: Direct filesystem — rejected because S3 API provides programmatic access and future cloud portability

## Decision: Reverse Proxy

- **Decision**: Nginx (`nginx:1.25-alpine`) with basic proxy config
- **Rationale**: Industry standard, lightweight, will handle SSL termination and routing in production
- **Alternatives**: Traefik — viable but Nginx is simpler for initial setup

## Decision: Test Framework

- **Decision**: Python pytest with httpx, psycopg2-binary, redis-py, pika, elasticsearch-py, boto3
- **Rationale**: pytest is the de facto Python testing standard; httpx provides async HTTP client; each library is the official client for its service
- **Alternatives**: unittest — rejected due to less expressive assertions and fixture system
