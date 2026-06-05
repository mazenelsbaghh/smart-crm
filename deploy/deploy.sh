#!/bin/bash
set -e

# ==============================================================================
# Smart Customer - Direct Deploy Script
# Syncs code to server, rebuilds containers, and cleans old Docker images
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

# Load credentials from .env.deploy if it exists
ENV_DEPLOY_FILE="${PROJECT_DIR}/.env.deploy"
if [ -f "$ENV_DEPLOY_FILE" ]; then
    source "$ENV_DEPLOY_FILE"
fi

# Prompt for credentials if not set
if [ -z "$SSH_HOST" ]; then
    read -p "🌐 Server IP/Host: " SSH_HOST
fi

if [ -z "$SSH_USER" ]; then
    SSH_USER="root"
fi

if [ -z "$SSH_PASS" ]; then
    read -sp "🔑 SSH Password: " SSH_PASS
    echo ""
fi

REMOTE_DIR="${REMOTE_DIR:-/root/smart-crm}"

echo ""
echo "🚀 Smart Customer - Deploying to $SSH_HOST"
echo "============================================"
echo "📂 Local:  $PROJECT_DIR"
echo "📂 Remote: $SSH_USER@$SSH_HOST:$REMOTE_DIR"
echo ""

# Step 1: Ensure remote directory exists
echo "📁 Step 1/5: Ensuring remote directory exists..."
sshpass -p "$SSH_PASS" ssh -o StrictHostKeyChecking=no "$SSH_USER@$SSH_HOST" "mkdir -p $REMOTE_DIR"

# Step 2: Sync files (excluding unnecessary dirs)
echo "📤 Step 2/5: Syncing files to server..."
sshpass -p "$SSH_PASS" rsync -avz --progress -e "ssh -o StrictHostKeyChecking=no" \
    --exclude='.git' \
    --exclude='.venv' \
    --exclude='.env' \
    --exclude='.env.deploy' \
    --exclude='node_modules' \
    --exclude='bin' \
    --exclude='obj' \
    --exclude='.next' \
    --exclude='sessions' \
    --exclude='.pytest_cache' \
    --exclude='.vscode' \
    --exclude='.DS_Store' \
    --exclude='*.log' \
    --exclude='.gemini' \
    --exclude='.agents' \
    --exclude='specs' \
    --exclude='tests' \
    "$PROJECT_DIR/" "$SSH_USER@$SSH_HOST:$REMOTE_DIR/"

echo "✅ Files synced."

# Step 3: Initialize .env if needed
echo "⚙️  Running remote deployment commands (ENV setup, container rebuild, prune)..."
sshpass -p "$SSH_PASS" ssh -o StrictHostKeyChecking=no "$SSH_USER@$SSH_HOST" \
    "cd $REMOTE_DIR && \
     if [ ! -f .env ]; then cp .env.example .env && echo '📝 Created .env from .env.example'; else echo '✅ .env already exists'; fi && \
     echo '🔄 Rebuilding and restarting containers...' && \
     docker compose -f docker-compose.yml -f docker-compose.production.yml down && \
     docker compose -f docker-compose.yml -f docker-compose.production.yml up -d --build && \
     echo '🧹 Cleaning old Docker images...' && \
     docker image prune -af"

echo ""
echo "============================================"
echo "✅ Deployment complete!"
echo "============================================"
