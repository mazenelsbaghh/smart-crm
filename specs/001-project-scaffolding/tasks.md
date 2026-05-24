# Tasks: Project Scaffolding & DevOps Foundation

**Feature**: 001-project-scaffolding
**Spec**: [spec.md](./spec.md)
**Plan**: [plan.md](./plan.md)
**Branch**: `001-project-scaffolding`
**Generated**: 2026-05-24
**Target**: Designed for a cheaper LLM to implement without problems — every task is atomic, self-contained, and includes exact file paths and expected content.

---

## Dependencies

```text
Phase 1 (Setup)
  └──► Phase 2 (Foundational: Docker Compose)
        └──► Phase 3 (US1: Developer Bootstraps Environment)
              └──► Phase 4 (US2: Developer Runs Full Test Suite)
                    └──► Phase 5 (US3: Developer Uses Makefile)
                          └──► Phase 6 (US4: Git Workflow Enforced)
                                └──► Phase 7 (Polish & Documentation)
```

## Parallel Execution Map

```text
Phase 1: T001 → T002 → T003, T004 (parallel)
Phase 2: T005 → T006, T007 (parallel)
Phase 3: T008 → T009 → T010 → T011
Phase 4: T012 → T013 → T014 → T015 → T016
Phase 5: T017 → T018
Phase 6: T019 → T020 → T021
Phase 7: T022 → T023 → T024
```

---

## Phase 1: Setup (Project Initialization)

**Goal**: Create the foundational project files and directory structure.

- [x] T001 Create the `.env.example` file at repository root `.env.example`

  **File**: `.env.example`
  **Action**: Create new file with the following exact content:
  ```env
  # === PostgreSQL ===
  POSTGRES_USER=smartcore
  POSTGRES_PASSWORD=changeme_postgres
  POSTGRES_DB=smartcustomercore
  POSTGRES_PORT=5432

  # === Redis ===
  REDIS_PORT=6379

  # === RabbitMQ ===
  RABBITMQ_DEFAULT_USER=admin
  RABBITMQ_DEFAULT_PASS=changeme_rabbitmq
  RABBITMQ_PORT=5672
  RABBITMQ_MGMT_PORT=15672

  # === Elasticsearch ===
  ELASTICSEARCH_PORT=9200
  ES_JAVA_OPTS=-Xms512m -Xmx512m

  # === MinIO ===
  MINIO_ROOT_USER=minioadmin
  MINIO_ROOT_PASSWORD=changeme_minio
  MINIO_API_PORT=9000
  MINIO_CONSOLE_PORT=9001
  MINIO_BUCKET=smartcore-media

  # === Nginx ===
  NGINX_HTTP_PORT=80

  # === General ===
  COMPOSE_PROJECT_NAME=smartcustomercore
  ```
  **Verify**: File exists at `.env.example` and contains all variables listed above.

---

- [x] T002 Create the `.editorconfig` file at repository root `.editorconfig`

  **File**: `.editorconfig`
  **Action**: Create new file with the following exact content:
  ```ini
  root = true

  [*]
  indent_style = space
  indent_size = 4
  end_of_line = lf
  charset = utf-8
  trim_trailing_whitespace = true
  insert_final_newline = true

  [*.{yml,yaml}]
  indent_size = 2

  [*.{json,js,ts}]
  indent_size = 2

  [Makefile]
  indent_style = tab

  [*.md]
  trim_trailing_whitespace = false
  ```
  **Verify**: File exists and has correct indentation rules.

---

- [x] T003 [P] Create Nginx default configuration at `nginx/default.conf`

  **File**: `nginx/default.conf`
  **Action**: Create directory `nginx/` and file `default.conf` with the following exact content:
  ```nginx
  upstream backend {
      server backend:5000;
  }

  server {
      listen 80;
      server_name localhost;

      # Health check endpoint for Nginx itself
      location /nginx-health {
          access_log off;
          return 200 'OK';
          add_header Content-Type text/plain;
      }

      # Proxy to backend API (will be enabled in Phase 1)
      # location /api/ {
      #     proxy_pass http://backend;
      #     proxy_http_version 1.1;
      #     proxy_set_header Upgrade $http_upgrade;
      #     proxy_set_header Connection "upgrade";
      #     proxy_set_header Host $host;
      #     proxy_set_header X-Real-IP $remote_addr;
      #     proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      #     proxy_set_header X-Forwarded-Proto $scheme;
      # }

      location / {
          return 200 'Smart Customer Core - Infrastructure Ready';
          add_header Content-Type text/plain;
      }
  }
  ```
  **Verify**: File exists at `nginx/default.conf`.

---

- [x] T004 [P] Create the Python test requirements at `tests/requirements-test.txt`

  **File**: `tests/requirements-test.txt`
  **Action**: Create directory `tests/` and file `requirements-test.txt` with the following exact content:
  ```text
  pytest==8.2.2
  pytest-asyncio==0.23.7
  pytest-cov==5.0.0
  httpx==0.27.0
  psycopg2-binary==2.9.9
  redis==5.0.7
  pika==1.3.2
  elasticsearch==8.14.0
  boto3==1.34.131
  python-dotenv==1.0.1
  ```
  **Verify**: File exists and contains all listed packages with version pins.

---

## Phase 2: Foundational (Docker Compose)

**Goal**: Create the Docker Compose configuration that defines all 6 infrastructure services with health checks.
**Depends on**: Phase 1 (`.env.example` must exist)

- [x] T005 Create the main `docker-compose.yml` at repository root `docker-compose.yml`

  **File**: `docker-compose.yml`
  **Action**: Create new file with the following exact content:
  ```yaml
  version: "3.8"

  services:
    postgres:
      image: pgvector/pgvector:pg16
      container_name: ${COMPOSE_PROJECT_NAME:-smartcustomercore}-postgres
      restart: unless-stopped
      environment:
        POSTGRES_USER: ${POSTGRES_USER:-smartcore}
        POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-changeme_postgres}
        POSTGRES_DB: ${POSTGRES_DB:-smartcustomercore}
      volumes:
        - pgdata:/var/lib/postgresql/data
      healthcheck:
        test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-smartcore} -d ${POSTGRES_DB:-smartcustomercore}"]
        interval: 10s
        timeout: 5s
        retries: 5
        start_period: 30s
      networks:
        - smartcore-net

    redis:
      image: redis:7-alpine
      container_name: ${COMPOSE_PROJECT_NAME:-smartcustomercore}-redis
      restart: unless-stopped
      command: redis-server --appendonly yes
      volumes:
        - redisdata:/data
      healthcheck:
        test: ["CMD", "redis-cli", "ping"]
        interval: 10s
        timeout: 5s
        retries: 5
        start_period: 10s
      networks:
        - smartcore-net

    rabbitmq:
      image: rabbitmq:3.13-management-alpine
      container_name: ${COMPOSE_PROJECT_NAME:-smartcustomercore}-rabbitmq
      restart: unless-stopped
      environment:
        RABBITMQ_DEFAULT_USER: ${RABBITMQ_DEFAULT_USER:-admin}
        RABBITMQ_DEFAULT_PASS: ${RABBITMQ_DEFAULT_PASS:-changeme_rabbitmq}
      volumes:
        - rabbitmqdata:/var/lib/rabbitmq
      healthcheck:
        test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
        interval: 15s
        timeout: 10s
        retries: 5
        start_period: 30s
      networks:
        - smartcore-net

    elasticsearch:
      image: docker.elastic.co/elasticsearch/elasticsearch:8.14.0
      container_name: ${COMPOSE_PROJECT_NAME:-smartcustomercore}-elasticsearch
      restart: unless-stopped
      environment:
        - discovery.type=single-node
        - xpack.security.enabled=false
        - ES_JAVA_OPTS=${ES_JAVA_OPTS:--Xms512m -Xmx512m}
      volumes:
        - esdata:/usr/share/elasticsearch/data
      healthcheck:
        test: ["CMD-SHELL", "curl -sf http://localhost:9200/_cluster/health || exit 1"]
        interval: 15s
        timeout: 10s
        retries: 5
        start_period: 45s
      networks:
        - smartcore-net

    minio:
      image: minio/minio:latest
      container_name: ${COMPOSE_PROJECT_NAME:-smartcustomercore}-minio
      restart: unless-stopped
      command: server /data --console-address ":9001"
      environment:
        MINIO_ROOT_USER: ${MINIO_ROOT_USER:-minioadmin}
        MINIO_ROOT_PASSWORD: ${MINIO_ROOT_PASSWORD:-changeme_minio}
      volumes:
        - miniodata:/data
      healthcheck:
        test: ["CMD", "curl", "-sf", "http://localhost:9000/minio/health/live"]
        interval: 15s
        timeout: 10s
        retries: 5
        start_period: 15s
      networks:
        - smartcore-net

    nginx:
      image: nginx:1.25-alpine
      container_name: ${COMPOSE_PROJECT_NAME:-smartcustomercore}-nginx
      restart: unless-stopped
      volumes:
        - ./nginx/default.conf:/etc/nginx/conf.d/default.conf:ro
      healthcheck:
        test: ["CMD-SHELL", "curl -sf http://localhost:80/nginx-health || exit 1"]
        interval: 10s
        timeout: 5s
        retries: 5
        start_period: 10s
      depends_on:
        postgres:
          condition: service_healthy
        redis:
          condition: service_healthy
      networks:
        - smartcore-net

  volumes:
    pgdata:
    redisdata:
    rabbitmqdata:
    esdata:
    miniodata:

  networks:
    smartcore-net:
      driver: bridge
  ```
  **Verify**: Run `docker compose config` from repo root — it should validate without errors.

---

- [x] T006 [P] Create `docker-compose.override.yml` at repository root `docker-compose.override.yml`

  **File**: `docker-compose.override.yml`
  **Action**: Create new file with the following exact content:
  ```yaml
  version: "3.8"

  # Development-only port mappings
  # Override these in .env if ports conflict
  services:
    postgres:
      ports:
        - "${POSTGRES_PORT:-5432}:5432"

    redis:
      ports:
        - "${REDIS_PORT:-6379}:6379"

    rabbitmq:
      ports:
        - "${RABBITMQ_PORT:-5672}:5672"
        - "${RABBITMQ_MGMT_PORT:-15672}:15672"

    elasticsearch:
      ports:
        - "${ELASTICSEARCH_PORT:-9200}:9200"

    minio:
      ports:
        - "${MINIO_API_PORT:-9000}:9000"
        - "${MINIO_CONSOLE_PORT:-9001}:9001"

    nginx:
      ports:
        - "${NGINX_HTTP_PORT:-80}:80"
  ```
  **Verify**: Run `docker compose config` — should merge correctly with `docker-compose.yml`.

---

- [x] T007 [P] Create the MinIO bucket initialization script at `scripts/init-minio.sh`

  **File**: `scripts/init-minio.sh`
  **Action**: Create directory `scripts/` and file `init-minio.sh` with the following exact content. Set the file as executable (`chmod +x scripts/init-minio.sh`):
  ```bash
  #!/bin/bash
  # Initialize MinIO bucket for Smart Customer Core
  set -e

  MINIO_HOST="${MINIO_HOST:-localhost}"
  MINIO_PORT="${MINIO_API_PORT:-9000}"
  MINIO_USER="${MINIO_ROOT_USER:-minioadmin}"
  MINIO_PASS="${MINIO_ROOT_PASSWORD:-changeme_minio}"
  BUCKET="${MINIO_BUCKET:-smartcore-media}"

  echo "Waiting for MinIO to be ready..."
  for i in $(seq 1 30); do
      if curl -sf "http://${MINIO_HOST}:${MINIO_PORT}/minio/health/live" > /dev/null 2>&1; then
          echo "MinIO is ready."
          break
      fi
      echo "Attempt $i/30 - MinIO not ready yet..."
      sleep 2
  done

  # Install mc (MinIO Client) if not available
  if ! command -v mc &> /dev/null; then
      echo "mc (MinIO Client) not found. Skipping bucket creation."
      echo "To create the bucket manually, use the MinIO Console at http://${MINIO_HOST}:${MINIO_CONSOLE_PORT:-9001}"
      exit 0
  fi

  # Configure mc alias
  mc alias set smartcore "http://${MINIO_HOST}:${MINIO_PORT}" "${MINIO_USER}" "${MINIO_PASS}"

  # Create bucket if it doesn't exist
  if mc ls smartcore/"${BUCKET}" > /dev/null 2>&1; then
      echo "Bucket '${BUCKET}' already exists."
  else
      mc mb "smartcore/${BUCKET}"
      echo "Bucket '${BUCKET}' created successfully."
  fi
  ```
  **Verify**: File exists, is executable, and runs without syntax errors (`bash -n scripts/init-minio.sh`).

---

## Phase 3: User Story 1 — Developer Bootstraps Environment

**Goal**: Create Makefile targets so a developer can run `make up`, `make down`, `make health`.
**Depends on**: Phase 2 (Docker Compose must exist)
**Independent Test**: `make up && make health && make down` — all succeed.

- [x] T008 [US1] Create the `Makefile` at repository root `Makefile`

  **File**: `Makefile`
  **Action**: Create new file with the following exact content (NOTE: all indented lines MUST use actual TAB characters, not spaces):
  ```makefile
  # ============================================================================
  # Smart Customer Core — Makefile
  # ============================================================================
  # Usage: make help
  # ============================================================================

  .PHONY: help env up down restart logs ps clean health \
          test-setup test-all test-phase-0 test-coverage

  SHELL := /bin/bash
  COMPOSE := docker compose
  PYTHON := python3
  VENV := .venv
  PIP := $(VENV)/bin/pip
  PYTEST := $(VENV)/bin/pytest

  # === Default target ===
  .DEFAULT_GOAL := help

  # === Help ===
  help: ## Show all available targets with descriptions
  	@echo ""
  	@echo "╔══════════════════════════════════════════════════════════════╗"
  	@echo "║         Smart Customer Core — Available Commands           ║"
  	@echo "╚══════════════════════════════════════════════════════════════╝"
  	@echo ""
  	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
  		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-20s\033[0m %s\n", $$1, $$2}'
  	@echo ""

  # === Environment ===
  env: ## Create .env from .env.example (if .env doesn't exist)
  	@if [ ! -f .env ]; then \
  		cp .env.example .env; \
  		echo "✅ Created .env from .env.example"; \
  		echo "⚠️  Please review and update passwords in .env"; \
  	else \
  		echo "ℹ️  .env already exists. Skipping."; \
  	fi

  # === Docker Compose ===
  up: ## Start all Docker services (build if needed)
  	@echo "🚀 Starting Smart Customer Core infrastructure..."
  	$(COMPOSE) up -d --build
  	@echo "✅ All services started. Run 'make health' to verify."

  down: ## Stop all Docker services
  	@echo "🛑 Stopping all services..."
  	$(COMPOSE) down
  	@echo "✅ All services stopped."

  restart: ## Restart all Docker services
  	@echo "🔄 Restarting all services..."
  	$(COMPOSE) down
  	$(COMPOSE) up -d --build
  	@echo "✅ All services restarted."

  logs: ## Tail logs from all Docker services
  	$(COMPOSE) logs -f

  ps: ## Show running Docker containers
  	$(COMPOSE) ps

  clean: ## Remove all containers, volumes, and networks
  	@echo "🧹 Cleaning up all Docker resources..."
  	$(COMPOSE) down -v --rmi local --remove-orphans
  	@echo "✅ Cleanup complete."

  # === Health Checks ===
  health: ## Check health status of all infrastructure services
  	@echo ""
  	@echo "🏥 Checking infrastructure health..."
  	@echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  	@printf "  PostgreSQL:    "; \
  	  docker exec $$(docker compose ps -q postgres 2>/dev/null) pg_isready -U smartcore > /dev/null 2>&1 \
  	  && echo "✅ Healthy" || echo "❌ Unhealthy"
  	@printf "  Redis:         "; \
  	  docker exec $$(docker compose ps -q redis 2>/dev/null) redis-cli ping > /dev/null 2>&1 \
  	  && echo "✅ Healthy" || echo "❌ Unhealthy"
  	@printf "  RabbitMQ:      "; \
  	  docker exec $$(docker compose ps -q rabbitmq 2>/dev/null) rabbitmq-diagnostics -q ping > /dev/null 2>&1 \
  	  && echo "✅ Healthy" || echo "❌ Unhealthy"
  	@printf "  Elasticsearch: "; \
  	  curl -sf http://localhost:$${ELASTICSEARCH_PORT:-9200}/_cluster/health > /dev/null 2>&1 \
  	  && echo "✅ Healthy" || echo "❌ Unhealthy"
  	@printf "  MinIO:         "; \
  	  curl -sf http://localhost:$${MINIO_API_PORT:-9000}/minio/health/live > /dev/null 2>&1 \
  	  && echo "✅ Healthy" || echo "❌ Unhealthy"
  	@printf "  Nginx:         "; \
  	  curl -sf http://localhost:$${NGINX_HTTP_PORT:-80}/nginx-health > /dev/null 2>&1 \
  	  && echo "✅ Healthy" || echo "❌ Unhealthy"
  	@echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  	@echo ""

  # === Testing ===
  test-setup: ## Create Python virtual environment and install test dependencies
  	@echo "📦 Setting up test environment..."
  	@$(PYTHON) -m venv $(VENV)
  	@$(PIP) install --upgrade pip -q
  	@$(PIP) install -r tests/requirements-test.txt -q
  	@echo "✅ Test environment ready."

  test-all: ## Run ALL tests (all phases)
  	@echo "🧪 Running all tests..."
  	$(PYTEST) tests/ -v --tb=short
  	@echo "✅ All tests complete."

  test-phase-0: ## Run Phase 0 infrastructure tests only
  	@echo "🧪 Running Phase 0 infrastructure tests..."
  	$(PYTEST) tests/phase_0/ -v --tb=short
  	@echo "✅ Phase 0 tests complete."

  test-coverage: ## Run all tests with coverage report
  	@echo "📊 Running tests with coverage..."
  	$(PYTEST) tests/ -v --cov=. --cov-report=html --cov-report=term
  	@echo "✅ Coverage report generated in htmlcov/"
  ```
  **Verify**: Run `make help` — it should print all available targets with descriptions.

---

- [x] T009 [US1] Create the Makefile `.env` auto-copy logic verification

  **Action**: Test that the `make env` target works correctly:
  1. Ensure `.env` does NOT exist (delete if it does).
  2. Run `make env`.
  3. Verify `.env` was created with same content as `.env.example`.
  4. Run `make env` again — verify it prints "already exists" message and does NOT overwrite.
  **This is a manual verification step, not a file creation.**

---

- [x] T010 [US1] Add `.env` to `.gitignore` at repository root `.gitignore`

  **File**: `.gitignore`
  **Action**: Open the existing `.gitignore` and ensure it contains the following sections. If the file does not exist, create it. Append any missing entries:
  ```gitignore
  # === Environment ===
  .env
  .env.local
  .env.*.local

  # === Python ===
  __pycache__/
  *.py[cod]
  *$py.class
  .venv/
  venv/
  *.egg-info/
  dist/
  build/
  htmlcov/
  .coverage
  .pytest_cache/

  # === Node.js ===
  node_modules/
  npm-debug.log*
  yarn-debug.log*

  # === C# / .NET ===
  bin/
  obj/
  *.user
  *.suo
  *.vs/
  packages/

  # === Docker ===
  docker-compose.override.local.yml

  # === IDE ===
  .idea/
  .vscode/settings.json
  *.swp
  *.swo
  *~
  .DS_Store
  Thumbs.db

  # === Logs ===
  *.log
  logs/

  # === OS ===
  .DS_Store
  Thumbs.db
  ```
  **Verify**: `.env` is not tracked by git. Run `git check-ignore .env` — should return `.env`.

---

- [x] T011 [US1] Verify full bootstrap workflow

  **Action**: Run the complete bootstrap workflow and verify:
  ```bash
  make env           # Creates .env
  make up            # Starts all 6 services
  make ps            # All 6 containers are running
  make health        # All 6 services report healthy
  make down          # All services stop cleanly
  ```
  **This is a verification checkpoint, not a file creation.**

---

## Phase 4: User Story 2 — Developer Runs Full Test Suite

**Goal**: Create pytest infrastructure tests that verify connectivity to all services.
**Depends on**: Phase 3 (Makefile and Docker services must work)
**Independent Test**: `make test-setup && make up && make test-phase-0` — all tests pass.

- [x] T012 [US2] Create `tests/__init__.py` at `tests/__init__.py`

  **File**: `tests/__init__.py`
  **Action**: Create an empty file (just to make tests/ a Python package):
  ```python
  ```
  **Verify**: File exists (can be empty).

---

- [x] T013 [US2] Create `tests/conftest.py` at `tests/conftest.py`

  **File**: `tests/conftest.py`
  **Action**: Create new file with the following exact content:
  ```python
  """
  Global pytest configuration and fixtures for Smart Customer Core tests.
  """
  import os
  import pytest
  from dotenv import load_dotenv

  # Load environment variables from .env file
  load_dotenv()


  @pytest.fixture(scope="session")
  def postgres_config():
      """PostgreSQL connection configuration from environment."""
      return {
          "host": os.getenv("POSTGRES_HOST", "localhost"),
          "port": int(os.getenv("POSTGRES_PORT", "5432")),
          "user": os.getenv("POSTGRES_USER", "smartcore"),
          "password": os.getenv("POSTGRES_PASSWORD", "changeme_postgres"),
          "dbname": os.getenv("POSTGRES_DB", "smartcustomercore"),
      }


  @pytest.fixture(scope="session")
  def redis_config():
      """Redis connection configuration from environment."""
      return {
          "host": os.getenv("REDIS_HOST", "localhost"),
          "port": int(os.getenv("REDIS_PORT", "6379")),
      }


  @pytest.fixture(scope="session")
  def rabbitmq_config():
      """RabbitMQ connection configuration from environment."""
      return {
          "host": os.getenv("RABBITMQ_HOST", "localhost"),
          "port": int(os.getenv("RABBITMQ_PORT", "5672")),
          "user": os.getenv("RABBITMQ_DEFAULT_USER", "admin"),
          "password": os.getenv("RABBITMQ_DEFAULT_PASS", "changeme_rabbitmq"),
          "mgmt_port": int(os.getenv("RABBITMQ_MGMT_PORT", "15672")),
      }


  @pytest.fixture(scope="session")
  def elasticsearch_config():
      """Elasticsearch connection configuration from environment."""
      return {
          "host": os.getenv("ELASTICSEARCH_HOST", "localhost"),
          "port": int(os.getenv("ELASTICSEARCH_PORT", "9200")),
      }


  @pytest.fixture(scope="session")
  def minio_config():
      """MinIO connection configuration from environment."""
      return {
          "host": os.getenv("MINIO_HOST", "localhost"),
          "port": int(os.getenv("MINIO_API_PORT", "9000")),
          "access_key": os.getenv("MINIO_ROOT_USER", "minioadmin"),
          "secret_key": os.getenv("MINIO_ROOT_PASSWORD", "changeme_minio"),
          "bucket": os.getenv("MINIO_BUCKET", "smartcore-media"),
      }
  ```
  **Verify**: Python syntax is valid — run `python3 -c "import ast; ast.parse(open('tests/conftest.py').read())"`.

---

- [x] T014 [US2] Create `tests/phase_0/__init__.py` at `tests/phase_0/__init__.py`

  **File**: `tests/phase_0/__init__.py`
  **Action**: Create an empty file:
  ```python
  ```
  **Verify**: File exists.

---

- [x] T015 [US2] Create `tests/phase_0/test_infrastructure.py` at `tests/phase_0/test_infrastructure.py`

  **File**: `tests/phase_0/test_infrastructure.py`
  **Action**: Create new file with the following exact content:
  ```python
  """
  Phase 0: Infrastructure Health Tests
  
  Verifies that all infrastructure services started by Docker Compose
  are accessible and correctly configured.
  
  Run with: make test-phase-0
  Requires: make up (services must be running)
  """
  import pytest
  import psycopg2
  import redis
  import pika
  from elasticsearch import Elasticsearch
  import boto3
  from botocore.client import Config as BotoConfig


  class TestPostgreSQL:
      """Test PostgreSQL connectivity and pgvector extension."""

      def test_connection(self, postgres_config):
          """Test that PostgreSQL is accessible and accepts connections."""
          conn = psycopg2.connect(
              host=postgres_config["host"],
              port=postgres_config["port"],
              user=postgres_config["user"],
              password=postgres_config["password"],
              dbname=postgres_config["dbname"],
          )
          assert conn is not None
          conn.close()

      def test_pgvector_extension(self, postgres_config):
          """Test that the pgvector extension is available."""
          conn = psycopg2.connect(
              host=postgres_config["host"],
              port=postgres_config["port"],
              user=postgres_config["user"],
              password=postgres_config["password"],
              dbname=postgres_config["dbname"],
          )
          cur = conn.cursor()
          cur.execute("CREATE EXTENSION IF NOT EXISTS vector;")
          conn.commit()
          cur.execute("SELECT extname FROM pg_extension WHERE extname = 'vector';")
          result = cur.fetchone()
          assert result is not None
          assert result[0] == "vector"
          cur.close()
          conn.close()

      def test_database_exists(self, postgres_config):
          """Test that the configured database exists."""
          conn = psycopg2.connect(
              host=postgres_config["host"],
              port=postgres_config["port"],
              user=postgres_config["user"],
              password=postgres_config["password"],
              dbname=postgres_config["dbname"],
          )
          cur = conn.cursor()
          cur.execute("SELECT current_database();")
          result = cur.fetchone()
          assert result[0] == postgres_config["dbname"]
          cur.close()
          conn.close()


  class TestRedis:
      """Test Redis connectivity."""

      def test_ping(self, redis_config):
          """Test that Redis responds to PING."""
          r = redis.Redis(
              host=redis_config["host"],
              port=redis_config["port"],
          )
          assert r.ping() is True

      def test_set_and_get(self, redis_config):
          """Test basic set/get operations."""
          r = redis.Redis(
              host=redis_config["host"],
              port=redis_config["port"],
          )
          r.set("test_key", "test_value")
          value = r.get("test_key")
          assert value == b"test_value"
          r.delete("test_key")


  class TestRabbitMQ:
      """Test RabbitMQ connectivity."""

      def test_connection(self, rabbitmq_config):
          """Test that RabbitMQ accepts connections."""
          credentials = pika.PlainCredentials(
              rabbitmq_config["user"],
              rabbitmq_config["password"],
          )
          params = pika.ConnectionParameters(
              host=rabbitmq_config["host"],
              port=rabbitmq_config["port"],
              credentials=credentials,
          )
          connection = pika.BlockingConnection(params)
          assert connection.is_open
          connection.close()

      def test_channel_creation(self, rabbitmq_config):
          """Test that a channel can be created."""
          credentials = pika.PlainCredentials(
              rabbitmq_config["user"],
              rabbitmq_config["password"],
          )
          params = pika.ConnectionParameters(
              host=rabbitmq_config["host"],
              port=rabbitmq_config["port"],
              credentials=credentials,
          )
          connection = pika.BlockingConnection(params)
          channel = connection.channel()
          assert channel.is_open
          channel.close()
          connection.close()

      def test_queue_declare(self, rabbitmq_config):
          """Test that a queue can be declared and deleted."""
          credentials = pika.PlainCredentials(
              rabbitmq_config["user"],
              rabbitmq_config["password"],
          )
          params = pika.ConnectionParameters(
              host=rabbitmq_config["host"],
              port=rabbitmq_config["port"],
              credentials=credentials,
          )
          connection = pika.BlockingConnection(params)
          channel = connection.channel()
          result = channel.queue_declare(queue="test_queue", auto_delete=True)
          assert result.method.queue == "test_queue"
          channel.queue_delete(queue="test_queue")
          channel.close()
          connection.close()


  class TestElasticsearch:
      """Test Elasticsearch connectivity."""

      def test_cluster_health(self, elasticsearch_config):
          """Test that Elasticsearch cluster is healthy."""
          es = Elasticsearch(
              f"http://{elasticsearch_config['host']}:{elasticsearch_config['port']}"
          )
          health = es.cluster.health()
          assert health["status"] in ("green", "yellow")
          assert health["cluster_name"] is not None

      def test_index_operations(self, elasticsearch_config):
          """Test basic index create/delete operations."""
          es = Elasticsearch(
              f"http://{elasticsearch_config['host']}:{elasticsearch_config['port']}"
          )
          index_name = "test_smartcore_index"
          # Create index
          if es.indices.exists(index=index_name):
              es.indices.delete(index=index_name)
          es.indices.create(index=index_name)
          assert es.indices.exists(index=index_name)
          # Cleanup
          es.indices.delete(index=index_name)


  class TestMinIO:
      """Test MinIO (S3-compatible) connectivity."""

      def test_connection(self, minio_config):
          """Test that MinIO is accessible."""
          s3 = boto3.client(
              "s3",
              endpoint_url=f"http://{minio_config['host']}:{minio_config['port']}",
              aws_access_key_id=minio_config["access_key"],
              aws_secret_access_key=minio_config["secret_key"],
              config=BotoConfig(signature_version="s3v4"),
              region_name="us-east-1",
          )
          # List buckets — should not raise
          response = s3.list_buckets()
          assert "Buckets" in response

      def test_bucket_operations(self, minio_config):
          """Test bucket create/delete operations."""
          s3 = boto3.client(
              "s3",
              endpoint_url=f"http://{minio_config['host']}:{minio_config['port']}",
              aws_access_key_id=minio_config["access_key"],
              aws_secret_access_key=minio_config["secret_key"],
              config=BotoConfig(signature_version="s3v4"),
              region_name="us-east-1",
          )
          bucket_name = "test-smartcore-bucket"
          # Create bucket
          s3.create_bucket(Bucket=bucket_name)
          # Verify it exists
          response = s3.list_buckets()
          bucket_names = [b["Name"] for b in response["Buckets"]]
          assert bucket_name in bucket_names
          # Cleanup
          s3.delete_bucket(Bucket=bucket_name)
  ```
  **Verify**: Python syntax is valid — run `python3 -c "import ast; ast.parse(open('tests/phase_0/test_infrastructure.py').read())"`.

---

- [x] T016 [US2] Verify test suite runs successfully

  **Action**: Execute the full test flow:
  ```bash
  make env              # Ensure .env exists
  make up               # Start services
  # Wait ~30 seconds for all services to become healthy
  make health           # Verify health
  make test-setup       # Install Python deps
  make test-phase-0     # Run infrastructure tests — ALL must pass
  ```
  **Expected**: All 12 test cases across 5 test classes pass.
  **This is a verification checkpoint.**

---

## Phase 5: User Story 3 — Developer Uses Makefile for All Operations

**Goal**: Ensure all Makefile targets work correctly and provide a consistent developer experience.
**Depends on**: Phase 4 (test infrastructure must work)
**Independent Test**: `make help` lists all targets; each target runs without error.

- [x] T017 [US3] Verify all Makefile targets work

  **Action**: Run each target and verify it works:
  ```bash
  make help         # Lists all targets with descriptions
  make env          # Creates or reports .env
  make up           # Services start
  make ps           # Shows 6 running containers
  make logs         # Streams logs (Ctrl+C to stop)
  make health       # All services healthy
  make restart      # Services restart cleanly
  make test-phase-0 # Tests pass
  make test-all     # Tests pass
  make down         # Services stop
  make clean        # Everything removed
  ```
  **This is a verification checkpoint.**

---

- [x] T018 [US3] Add `make test-coverage` verification

  **Action**: Run `make test-coverage` and verify:
  1. Tests run with coverage tracking.
  2. Coverage report is printed to terminal.
  3. HTML coverage report is generated in `htmlcov/` directory.
  **This is a verification checkpoint.**

---

## Phase 6: User Story 4 — Git Workflow Enforced

**Goal**: Ensure `.gitignore`, README, and git workflow documentation are complete.
**Depends on**: Phase 5
**Independent Test**: `.gitignore` blocks sensitive files; README has all required sections.

- [x] T019 [US4] Verify `.gitignore` blocks sensitive files

  **Action**: Run these checks:
  ```bash
  git check-ignore .env              # Should return ".env"
  git check-ignore .env.local        # Should return ".env.local"
  git check-ignore __pycache__       # Should return "__pycache__"
  git check-ignore node_modules      # Should return "node_modules"
  git check-ignore .venv             # Should return ".venv"
  ```
  **All commands must return the file/directory name (meaning it IS ignored).**

---

- [x] T020 [US4] Create `README.md` at repository root `README.md`

  **File**: `README.md`
  **Action**: Create new file with the following exact content:
  ```markdown
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
  ```
  **Verify**: File exists and contains Architecture, Prerequisites, Quick Start, Git Workflow sections.

---

- [x] T021 [US4] Create `LICENSE` file at repository root `LICENSE`

  **File**: `LICENSE`
  **Action**: Create new file with the following content:
  ```text
  Copyright (c) 2026 Smart Customer Core

  All rights reserved.

  This software and associated documentation files (the "Software") are proprietary
  and confidential. Unauthorized copying, modification, distribution, or use of this
  Software, via any medium, is strictly prohibited.

  The Software is provided "AS IS", without warranty of any kind.
  ```
  **Verify**: File exists at `LICENSE`.

---

## Phase 7: Polish & Documentation

**Goal**: Create the Phase 0 skill and finalize all documentation.
**Depends on**: Phase 6

- [x] T022 Create Phase 0 skill at `.agents/skills/phase-0/SKILL.md`

  **File**: `.agents/skills/phase-0/SKILL.md`
  **Action**: Create directory `.agents/skills/phase-0/` and file `SKILL.md` with the following exact content:
  ```markdown
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
  ```
  **Verify**: File exists at `.agents/skills/phase-0/SKILL.md`.

---

- [x] T023 Verify full Phase 0 end-to-end workflow

  **Action**: Execute the complete workflow from scratch:
  ```bash
  make clean             # Start fresh
  make env               # Create .env
  make up                # Start all services
  sleep 30               # Wait for services to initialize
  make health            # All 6 services healthy
  make test-setup        # Install test deps
  make test-phase-0      # All tests pass
  make test-all          # All tests pass
  make test-coverage     # Coverage report generated
  make down              # Clean shutdown
  ```
  **Expected**: Zero errors, zero test failures.
  **This is the FINAL verification checkpoint for Phase 0.**

---

- [x] T024 Git commit and tag Phase 0

  **Action**: Commit all changes and tag the release:
  ```bash
  git add -A
  git commit -m "feat: Phase 0 — project scaffolding and DevOps foundation"
  git tag v0.1.0
  ```
  **Verify**: `git log --oneline -5` shows the commit; `git tag` shows `v0.1.0`.

---

## Summary

| Metric | Value |
|--------|-------|
| Total Tasks | 24 |
| File Creation Tasks | 12 |
| Verification Tasks | 8 |
| Configuration Tasks | 4 |
| Parallelizable Tasks | 4 (T003+T004, T006+T007) |
| Test Classes | 5 (PostgreSQL, Redis, RabbitMQ, Elasticsearch, MinIO) |
| Total Test Cases | 12 |
| Makefile Targets | 12 |
| Docker Services | 6 |

## Implementation Strategy

1. **MVP**: Complete Phases 1-3 first (Setup + Docker + Makefile) — this gives a running infrastructure.
2. **Incremental**: Add tests (Phase 4), verify Makefile (Phase 5), then documentation (Phase 6-7).
3. **Final**: End-to-end verification (T023) and git tag (T024).
