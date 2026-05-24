# Quickstart: Project Scaffolding & DevOps Foundation

## Prerequisites

- Docker Desktop 24+ or Docker Engine 24+ with Docker Compose v2
- Python 3.11+ (for running tests)
- GNU Make (standard on macOS/Linux)
- Git

## Getting Started

```bash
# 1. Clone the repository
git clone <repository-url>
cd smart-customer-core

# 2. Create environment file
make env

# 3. Start all infrastructure services
make up

# 4. Verify all services are healthy
make health

# 5. Set up test environment
make test-setup

# 6. Run infrastructure tests
make test-phase-0

# 7. View service logs (optional)
make logs
```

## Available Make Targets

| Target | Description |
|--------|-------------|
| `make env` | Create `.env` from `.env.example` |
| `make up` | Start all Docker services |
| `make down` | Stop all Docker services |
| `make restart` | Restart all services |
| `make logs` | Tail logs from all services |
| `make ps` | Show running containers |
| `make clean` | Remove all containers, volumes, images |
| `make health` | Check health of all services |
| `make test-setup` | Create Python venv and install test deps |
| `make test-all` | Run all tests |
| `make test-phase-0` | Run Phase 0 infrastructure tests |
| `make test-coverage` | Run tests with coverage report |
| `make help` | Show all available targets |

## Service Access (Development)

| Service | URL | Credentials |
|---------|-----|-------------|
| PostgreSQL | `localhost:5432` | See `.env` |
| Redis | `localhost:6379` | No auth (dev) |
| RabbitMQ Management | `http://localhost:15672` | See `.env` |
| Elasticsearch | `http://localhost:9200` | No auth (dev) |
| MinIO Console | `http://localhost:9001` | See `.env` |
| Nginx | `http://localhost:80` | N/A |
