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

- Docker Engine 24+ with Compose v2, or [Docker Desktop](https://www.docker.com/products/docker-desktop/) with those components
- [Python](https://www.python.org/) 3.11+ (for tests)
- [GNU Make](https://www.gnu.org/software/make/)
- [Git](https://git-scm.com/)

## Quick Start

```bash
# 1. Clone the repository
git clone <repository-url> smart-customer-core
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

`make env` generates independent `JWT_SECRET` and `WHATSAPP_GATEWAY_WEBHOOK_SECRET`
values in the ignored `.env` file when either is missing, too short, or still the
old public default. The backend rejects unsafe JWT signing keys at startup.
Access tokens last 15 minutes and require an active
server-side refresh session; logout, account removal, and role changes invalidate
access. The session-family migration `20261016040000_AddAccessSessionFamilies`
adds session tracking. Previously issued access tokens without a session claim
are rejected; clients with a valid refresh token can obtain a new access token,
while clients without one must sign in again.

For SSH deployment, copy `.env.deploy.example` (or `.env.deploy.sales.example`)
to its ignored local counterpart and use SSH keys or set `SSH_PASS` locally.
Keep TLS private keys outside Git. Removing a secret from the current files does
not remove it from Git history or revoke it: exposed SSH credentials and TLS keys
must be rotated, and shared history cleanup must be coordinated separately.

Raster processing uses Magick.NET 15.0.0. The backend Docker image uses Debian
Bookworm for its native runtime; deployments must rebuild the backend image after
updating dependencies.

As of 2026-10-16, the frontend development dependency audit reports five entries
for the single unpatched [braces nesting advisory](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm).
Its dependency chain is `eslint-config-next` → `@next/eslint-plugin-next` →
`fast-glob` → `micromatch` → `braces`. The Next.js lint plugin only calls that
glob helper when `settings.next.rootDir` is configured; this project's
`frontend/eslint.config.mjs` leaves that setting unset. This assessment does not
remove the audit entries or claim that the dependency itself is patched.

## Available Commands

Run `make help` to see all available commands:

| Command | Description |
|---------|-------------|
| `make env` | Create `.env` if absent and replace unsafe service secrets |
| `make up` | Start all Docker services |
| `make down` | Stop all services |
| `make restart` | Restart all services |
| `make logs` | Tail all service logs |
| `make ps` | Show running containers |
| `make clean` | Remove this Compose project's containers, volumes and local images |
| `make health` | Check all service health |
| `make test-setup` | Install Python test dependencies |
| `make test-all` | Run the Python tests under `tests/` |
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
