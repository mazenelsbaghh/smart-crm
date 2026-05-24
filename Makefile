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

test-coverage: ## Run all tests with coverage report
	@echo "📊 Running tests with coverage..."
	$(PYTEST) tests/ -v --cov=. --cov-report=html --cov-report=term
	@echo "✅ Coverage report generated in htmlcov/"
