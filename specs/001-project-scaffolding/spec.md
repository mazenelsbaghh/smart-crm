# Feature Specification: Project Scaffolding & DevOps Foundation

**Feature Branch**: `001-project-scaffolding`

**Created**: 2026-05-24

**Status**: Draft

**Input**: Phase 0 of Smart Customer Core — set up the full infrastructure foundation with Docker, Makefile, Git workflow, Python test harness, and all dependent services (PostgreSQL, Redis, RabbitMQ, Elasticsearch, MinIO, Nginx) so that all subsequent phases build on a verified, testable base.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Developer Bootstraps Environment (Priority: P1)

A developer clones the repository and runs a single command (`make up`) to have the entire infrastructure stack running locally with all services healthy and connectable.

**Why this priority**: Without working infrastructure, no subsequent feature development is possible. This is the absolute foundation.

**Independent Test**: Can be fully tested by running `make up` followed by `make health` — if all infrastructure services respond with healthy status, the story is complete.

**Acceptance Scenarios**:

1. **Given** a freshly cloned repository and Docker installed, **When** the developer runs `make env && make up`, **Then** all 6 infrastructure services (PostgreSQL, Redis, RabbitMQ, Elasticsearch, MinIO, Nginx) start and become healthy within 60 seconds.
2. **Given** all services are running, **When** the developer runs `make health`, **Then** each service returns a successful health check (connection verified).
3. **Given** all services are running, **When** the developer runs `make down`, **Then** all services stop cleanly with zero errors.

---

### User Story 2 - Developer Runs Full Test Suite (Priority: P1)

A developer can run `make test-phase-0` to execute Python-based infrastructure health tests that verify every service is accessible and correctly configured.

**Why this priority**: Testing from day one is a non-negotiable project requirement. Every phase must be independently verifiable.

**Independent Test**: Can be fully tested by running `make test-phase-0` — if all pytest tests pass, the story is complete.

**Acceptance Scenarios**:

1. **Given** infrastructure is running via `make up`, **When** the developer runs `make test-phase-0`, **Then** all 5 test cases pass (PostgreSQL + pgvector, Redis, RabbitMQ, Elasticsearch, MinIO).
2. **Given** infrastructure is NOT running, **When** the developer runs `make test-phase-0`, **Then** tests fail with clear connection error messages (not cryptic stack traces).
3. **Given** tests have run, **When** the developer runs `make test-coverage`, **Then** a coverage report is generated showing test coverage percentage.

---

### User Story 3 - Developer Uses Makefile for All Operations (Priority: P2)

All common development operations are accessible through `make` targets, providing a single consistent interface for the entire team.

**Why this priority**: Consistency in developer experience reduces onboarding time and prevents environment-specific issues.

**Independent Test**: Can be tested by running `make help` and verifying all documented targets exist and are functional.

**Acceptance Scenarios**:

1. **Given** a freshly cloned repository, **When** the developer runs `make help`, **Then** all available targets are listed with descriptions.
2. **Given** services are running, **When** the developer runs `make logs`, **Then** aggregated logs from all services stream to the terminal.
3. **Given** services are running, **When** the developer runs `make clean`, **Then** all containers, volumes, and images are removed.

---

### User Story 4 - Git Workflow Enforced (Priority: P2)

The repository has a proper `.gitignore`, conventional commit configuration, and branch strategy documented so that all developers follow the same workflow.

**Why this priority**: Clean git history and branch management is essential for multi-phase, multi-developer projects.

**Independent Test**: Can be tested by verifying `.gitignore` excludes sensitive files, and that the README documents the branch strategy.

**Acceptance Scenarios**:

1. **Given** the repository is initialized, **When** a developer checks `.gitignore`, **Then** it covers C#, Node.js, Python, Docker, IDE files, `.env`, and `node_modules`.
2. **Given** the repository exists, **When** a developer reads `README.md`, **Then** it contains: project overview, architecture diagram, getting-started instructions, and git workflow documentation.

---

### Edge Cases

- What happens when Docker is not installed? → `make up` shows clear error message.
- What happens when a port is already in use? → Docker compose reports the conflict; `docker-compose.override.yml` allows port customization.
- What happens when `.env` doesn't exist? → `make env` creates it from `.env.example`.
- What happens when a service fails to start? → Health checks detect it; `make health` reports which service is unhealthy.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a `docker-compose.yml` that defines PostgreSQL 16 with pgvector, Redis 7, RabbitMQ 3.13, Elasticsearch 8.x, MinIO, and Nginx services.
- **FR-002**: System MUST provide a `Makefile` with targets: `up`, `down`, `restart`, `logs`, `ps`, `clean`, `health`, `env`, `test-setup`, `test-all`, `test-phase-0`, `test-coverage`.
- **FR-003**: System MUST provide a `.env.example` with all required environment variables (database credentials, Redis URL, RabbitMQ URL, Elasticsearch URL, MinIO credentials, API ports).
- **FR-004**: System MUST provide a Python test suite using `pytest` that verifies connectivity to all 5 infrastructure services.
- **FR-005**: System MUST provide a `.gitignore` covering C#, Node.js, Python, Docker, and IDE files.
- **FR-006**: System MUST provide a `README.md` with project overview, architecture diagram, prerequisites, getting-started instructions, and git workflow.
- **FR-007**: System MUST provide health checks in `docker-compose.yml` for every service.
- **FR-008**: System MUST provide an `.editorconfig` for consistent formatting across editors.
- **FR-009**: System MUST provide a `docker-compose.override.yml` with development-specific port mappings.
- **FR-010**: System MUST store all secrets exclusively in `.env` (gitignored) and never in committed code.

### Key Entities

- **Docker Service**: A containerized infrastructure component (name, image, ports, volumes, health check).
- **Makefile Target**: A command shortcut (name, description, dependencies, shell commands).
- **Test Case**: A Python pytest function that verifies a specific infrastructure service (service name, connection method, assertion).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `make up` brings all 6 services to healthy state within 90 seconds on a machine with 8GB RAM.
- **SC-002**: `make test-phase-0` runs all 5 infrastructure tests and passes with zero failures.
- **SC-003**: `make down && make up` (restart cycle) completes within 60 seconds with no data loss in named volumes.
- **SC-004**: A new developer can go from `git clone` to running tests in under 5 minutes following README instructions.
- **SC-005**: `make clean` removes all Docker artifacts and returns to a clean state.

## Assumptions

- Developer has Docker Desktop or Docker Engine installed (version 24+).
- Developer has `make` available (standard on macOS/Linux; installable on Windows via chocolatey).
- Developer has Python 3.11+ installed for running tests.
- Ports 5432, 6379, 5672, 15672, 9200, 9000, 9001, 80 are available on the host machine (or overridden in `.env`).
- The project runs on a single machine for development; production deployment is a later phase.
