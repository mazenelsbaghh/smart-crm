# Smart Customer Core

> 🧠 Deployable AI Customer Operations Core — a modular monolith for intelligent customer service management via WhatsApp.

## Architecture

```text
Smart Customer Core
│
├── Web Dashboard (React/Next.js)
├── ASP.NET Core Modular Backend
├── Baileys WhatsApp Gateway (Node.js)
├── Gemini 3.5 Flash AI Layer
├── CRM Engine
├── Follow-up Engine
├── Workflow Engine
├── Campaign Engine
├── Analytics Engine
├── RabbitMQ Message Queue
├── PostgreSQL + pgvector Database
├── Redis Cache
├── Elasticsearch Search
├── MinIO Object Storage
└── Monitoring & Audit
```

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) 24+ or Docker Engine with Compose v2
- [Python](https://www.python.org/) 3.11+ (for tests)
- [GNU Make](https://www.gnu.org/software/make/)
- [Git](https://git-scm.com/)

## Quick Start

```bash
# 1. Clone the repository
git clone <repository-url>
cd smart-customer-core

# 2. Set up environment
make env

# 3. Start infrastructure
make up

# 4. Verify everything is healthy
make health

# 5. Run tests
make test-setup
make test-phase-0
```

## Available Commands

Run `make help` to see all available commands:

| Command | Description |
|---------|-------------|
| `make env` | Create `.env` from template |
| `make up` | Start all Docker services |
| `make down` | Stop all services |
| `make restart` | Restart all services |
| `make logs` | Tail all service logs |
| `make ps` | Show running containers |
| `make clean` | Remove all Docker artifacts |
| `make health` | Check all service health |
| `make test-setup` | Install Python test dependencies |
| `make test-all` | Run all tests |
| `make test-phase-0` | Run infrastructure tests |
| `make test-coverage` | Run tests with coverage |

## Service Access (Development)

| Service | URL | Default Credentials |
|---------|-----|-------------------|
| PostgreSQL | `localhost:5432` | `smartcore` / see `.env` |
| Redis | `localhost:6379` | No auth |
| RabbitMQ Management | [localhost:15672](http://localhost:15672) | `admin` / see `.env` |
| Elasticsearch | [localhost:9200](http://localhost:9200) | No auth |
| MinIO Console | [localhost:9001](http://localhost:9001) | `minioadmin` / see `.env` |
| Nginx | [localhost:80](http://localhost:80) | N/A |

## Git Workflow

### Branch Strategy

```text
main ──────────────────────────────────►
  │         │         │         │
  └─ 001-* ─┘         │         │
            └─ 002-* ─┘         │
                      └─ 003-* ─┘
```

- Each phase/feature gets a numbered branch (e.g., `001-project-scaffolding`).
- Feature branches merge to `main` after all tests pass.
- Tags mark phase completions: `v0.1.0`, `v1.0.0`, `v2.0.0`, etc.

### Commit Convention

Use [Conventional Commits](https://www.conventionalcommits.org/):

- `feat:` — New feature
- `fix:` — Bug fix
- `docs:` — Documentation
- `test:` — Tests
- `chore:` — Maintenance

## Project Structure

```text
.
├── docker-compose.yml          # Infrastructure services
├── docker-compose.override.yml # Dev port mappings
├── Makefile                    # All dev commands
├── .env.example                # Environment template
├── nginx/                      # Nginx configuration
├── tests/                      # Python test suite
│   ├── conftest.py             # Shared fixtures
│   └── phase_0/                # Infrastructure tests
├── docs/                       # Documentation
├── specs/                      # Feature specifications
└── .agents/                    # AI agent skills
```

## License

Proprietary — All rights reserved.
