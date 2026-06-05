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
	@grep -E '^[a-zA-Z0-9_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
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
	  docker exec $$($(COMPOSE) ps -q postgres 2>/dev/null) pg_isready -U smartcore > /dev/null 2>&1 \
	  && echo "✅ Healthy" || echo "❌ Unhealthy"
	@printf "  Redis:         "; \
	  docker exec $$($(COMPOSE) ps -q redis 2>/dev/null) redis-cli ping > /dev/null 2>&1 \
	  && echo "✅ Healthy" || echo "❌ Unhealthy"
	@printf "  RabbitMQ:      "; \
	  docker exec $$($(COMPOSE) ps -q rabbitmq 2>/dev/null) rabbitmq-diagnostics -q ping > /dev/null 2>&1 \
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

test-phase-1: ## Run Phase 1 core tests only
	@echo "🧪 Running Phase 1 core tests..."
	$(PYTEST) tests/phase_1/ -v --tb=short
	@echo "✅ Phase 1 tests complete."

test-phase-2: ## Run Phase 2 AI intelligence tests only
	@echo "🧪 Running Phase 2 AI intelligence tests..."
	$(PYTEST) tests/phase_2/ -v --tb=short
	@echo "✅ Phase 2 tests complete."

test-phase-3: ## Run Phase 3 knowledge & workflows tests only
	@echo "🧪 Running Phase 3 knowledge & workflows tests..."
	$(PYTEST) tests/phase_3/ -v --tb=short
	@echo "✅ Phase 3 tests complete."

test-phase-4: ## Run Phase 4 campaigns & analytics tests only
	@echo "🧪 Running Phase 4 campaigns & analytics tests..."
	$(PYTEST) tests/phase_4/ -v --tb=short
	@echo "✅ Phase 4 tests complete."

test-phase-5: ## Run Phase 5 media & audit tests only
	@echo "🧪 Running Phase 5 media & audit tests..."
	$(PYTEST) tests/phase_5/ -v --tb=short
	@echo "✅ Phase 5 tests complete."

test-coverage: ## Run all tests with coverage report
	@echo "📊 Running tests with coverage..."
	$(PYTEST) tests/ -v --cov=. --cov-report=html --cov-report=term
	@echo "✅ Coverage report generated in htmlcov/"

# === Database ===
db-migrate: ## Run EF Core migrations (restarts backend container to trigger migration)
	$(COMPOSE) restart backend

db-seed: ## Seed database (restarts backend container to trigger seeder)
	$(COMPOSE) restart backend

db-reset: ## Reset the database (drops and recreates backend database)
	$(COMPOSE) down -v
	$(COMPOSE) up -d --build

# === Diagnostics & Session status ===
whatsapp-status: ## Check WhatsApp session status (usage: make whatsapp-status PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make whatsapp-status PROJECT_ID=<uuid>"; \
	else \
		curl -s "http://localhost:80/api/whatsapp/session/status?projectId=$(PROJECT_ID)"; \
	fi

ai-test: ## Test Gemini API connection
	@if [ -z "$(GEMINI_API_KEY)" ]; then \
		echo "⚠️  GEMINI_API_KEY environment variable is not set."; \
	else \
		curl -s -X POST "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent?key=$(GEMINI_API_KEY)" \
			-H 'Content-Type: application/json' \
			-d '{"contents":[{"parts":[{"text":"Hello, response short OK"}]}]}'; \
	fi

scheduler-status: ## Check Hangfire scheduler server status
	@curl -s -I "http://localhost:80/hangfire" | head -n 1

crm-report: ## Generate CRM Daily Operations report (usage: make crm-report PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make crm-report PROJECT_ID=<uuid>"; \
	else \
		curl -s "http://localhost:80/api/projects/$(PROJECT_ID)/reports/daily"; \
	fi

brain-sync: ## Trigger semantic brain synchronization (usage: make brain-sync PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make brain-sync PROJECT_ID=<uuid>"; \
	else \
		curl -s -X POST "http://localhost:80/api/projects/$(PROJECT_ID)/brain/sync"; \
	fi

knowledge-search: ## Search Company Brain (usage: make knowledge-search PROJECT_ID=... Q=...)
	@if [ -z "$(PROJECT_ID)" ] || [ -z "$(Q)" ]; then \
		echo "⚠️  PROJECT_ID and Q are required. Usage: make knowledge-search PROJECT_ID=<uuid> Q=<query>"; \
	else \
		curl -s "http://localhost:80/api/projects/$(PROJECT_ID)/brain/search?q=$(Q)"; \
	fi

approval-queue: ## Get pending approval requests (usage: make approval-queue PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make approval-queue PROJECT_ID=<uuid>"; \
	else \
		curl -s "http://localhost:80/api/projects/$(PROJECT_ID)/approvals?status=Pending"; \
	fi

campaign-status: ## Get campaigns status (usage: make campaign-status PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make campaign-status PROJECT_ID=<uuid>"; \
	else \
		curl -s "http://localhost:80/api/projects/$(PROJECT_ID)/campaigns"; \
	fi

analytics-dashboard: ## Trigger daily analytics snapshot calculation (usage: make analytics-dashboard PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make analytics-dashboard PROJECT_ID=<uuid>"; \
	else \
		curl -s -X POST "http://localhost:80/api/projects/$(PROJECT_ID)/analytics/recalculate"; \
	fi

search-reindex: ## Reindex all project database records to Elasticsearch (usage: make search-reindex PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make search-reindex PROJECT_ID=<uuid>"; \
	else \
		curl -s -X POST "http://localhost:80/api/projects/$(PROJECT_ID)/search/reindex"; \
	fi

asset-stats: ## Get assets metrics
	@curl -s "http://localhost:80/api/system/metrics"

audit-report: ## Extract project audit logs (usage: make audit-report PROJECT_ID=...)
	@if [ -z "$(PROJECT_ID)" ]; then \
		echo "⚠️  PROJECT_ID is required. Usage: make audit-report PROJECT_ID=<uuid>"; \
	else \
		curl -s "http://localhost:80/api/projects/$(PROJECT_ID)/audit"; \
	fi

system-health: ## Fetch system health status
	@curl -s "http://localhost:80/api/system/health"

# === Phase 6 Deployment & Operations ===
deploy: env ## Run the production stack with docker-compose.production.yml
	@echo "🚀 Deploying Smart Customer Core in production mode..."
	$(COMPOSE) -f docker-compose.yml -f docker-compose.production.yml up -d --build
	@echo "✅ Production stack deployed."

backup: ## Run the automated backup utility script
	@chmod +x deploy/backup.sh
	./deploy/backup.sh

restore: ## Run the automated restore utility script (usage: make restore FILE=...)
	@if [ -z "$(FILE)" ]; then \
		echo "⚠️  FILE is required. Usage: make restore FILE=<path_to_archive>"; \
	else \
		chmod +x deploy/restore.sh; \
		./deploy/restore.sh $(FILE); \
	fi

test-phase-6: test-setup ## Run Phase 6 dashboard, SignalR, production & backup tests
	@echo "🧪 Running Phase 6 verification tests..."
	$(PYTEST) tests/phase_6/ -v --tb=short
	@echo "✅ Phase 6 tests complete."

push: ## Commit current branch, merge to main, and push to trigger CI/CD deploy
	@CURRENT_BRANCH=$$(git branch --show-current); \
	if [ -z "$$CURRENT_BRANCH" ]; then \
		echo "⚠️  Could not determine current branch."; \
		exit 1; \
	fi; \
	if [ "$$CURRENT_BRANCH" = "main" ]; then \
		echo "Already on main branch. Committing and pushing..."; \
		git add .; \
		git commit -m "Auto-commit on main before push" || true; \
		git push origin main; \
	else \
		echo "Current branch is $$CURRENT_BRANCH. Committing..."; \
		git add .; \
		git commit -m "Auto-commit on $$CURRENT_BRANCH" || true; \
		echo "Switching to main branch..."; \
		git checkout main; \
		git pull origin main || true; \
		echo "Merging $$CURRENT_BRANCH into main..."; \
		git merge $$CURRENT_BRANCH -m "Merge $$CURRENT_BRANCH into main" || { echo "❌ Merge failed. Please resolve conflicts manually."; git checkout $$CURRENT_BRANCH; exit 1; }; \
		echo "Pushing main to origin..."; \
		git push origin main; \
		echo "Switching back to $$CURRENT_BRANCH..."; \
		git checkout $$CURRENT_BRANCH; \
	fi



