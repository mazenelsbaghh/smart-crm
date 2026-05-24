---
name: "phase-0"
description: "Phase 0: Project Scaffolding & DevOps Foundation — Docker infrastructure, Makefile, and Python test harness."
---

# Phase 0: Project Scaffolding & DevOps Foundation

## What Was Built

- Docker Compose stack with 6 infrastructure services:
  - PostgreSQL 16 + pgvector (vector similarity search)
  - Redis 7 (caching, sessions)
  - RabbitMQ 3.13 (message queue)
  - Elasticsearch 8.14 (full-text search)
  - MinIO (S3-compatible object storage)
  - Nginx (reverse proxy)
- Comprehensive Makefile with 12+ targets
- Python pytest test harness with 12 infrastructure tests
- Environment management via .env
- Git workflow with conventional commits

## How to Run

```bash
make env        # Create .env from template
make up         # Start all services
make health     # Verify all services are healthy
```

## How to Test

```bash
make test-setup    # Install Python dependencies (first time only)
make test-phase-0  # Run infrastructure tests
make test-all      # Run ALL tests
make test-coverage # Run with coverage report
```

## Make Targets

| Target | Description |
|--------|-------------|
| `make help` | Show all commands |
| `make env` | Create .env from template |
| `make up` | Start infrastructure |
| `make down` | Stop infrastructure |
| `make restart` | Restart infrastructure |
| `make logs` | Tail logs |
| `make ps` | Show containers |
| `make clean` | Remove everything |
| `make health` | Health check |
| `make test-setup` | Install test deps |
| `make test-all` | Run all tests |
| `make test-phase-0` | Run Phase 0 tests |
| `make test-coverage` | Coverage report |

## API Endpoints

None yet — Phase 0 is infrastructure only. API endpoints arrive in Phase 1.

## Service Access

| Service | URL |
|---------|-----|
| PostgreSQL | localhost:5432 |
| Redis | localhost:6379 |
| RabbitMQ UI | http://localhost:15672 |
| Elasticsearch | http://localhost:9200 |
| MinIO Console | http://localhost:9001 |
| Nginx | http://localhost:80 |

## Verify It's Working

```bash
make up && make health && make test-phase-0
# All 6 services healthy + all 12 tests pass = Phase 0 complete ✅
```
