#!/bin/bash
# Smart Customer Core - Manual Deployment Script

# Exit immediately if a command exits with a non-zero status
set -e

# Server configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
if [ -f "$PROJECT_DIR/.env.deploy" ]; then
    set -a
    source "$PROJECT_DIR/.env.deploy"
    set +a
fi

SSH_HOST="${SSH_HOST:-147.93.86.206}"
SSH_USER="${SSH_USER:-root}"
SSH_PASS="${SSH_PASS:-}"
REMOTE_PATH="${REMOTE_PATH:-/root/smart-crm}"

if [ -z "$SSH_PASS" ]; then
    echo "❌ SSH_PASS is required. Load it from .env.deploy or your secret manager."
    exit 1
fi

echo "🚀 Starting manual deployment to $SSH_HOST..."

# Check if sshpass is installed
if ! command -v sshpass &> /dev/null; then
    echo "⚠️  sshpass is not installed locally. Trying to install via Homebrew..."
    if command -v brew &> /dev/null; then
        brew install hudochenkov/sshpass/sshpass
    else
        echo "❌ Homebrew not found. Please install sshpass manually."
        exit 1;
    fi
fi

echo "📦 Syncing files via rsync..."
sshpass -p "$SSH_PASS" rsync -avz -e "ssh -o StrictHostKeyChecking=no" \
    --exclude='.git' \
    --exclude='.venv' \
    --exclude='.env' \
    --exclude='node_modules' \
    --exclude='bin' \
    --exclude='obj' \
    --exclude='.next' \
    --exclude='sessions' \
    --exclude='.pytest_cache' \
    --exclude='.vscode' \
    --exclude='.DS_Store' \
    --exclude='*.log' \
    ./ "$SSH_USER@$SSH_HOST:$REMOTE_PATH/"

echo "🔄 Restarting application on the remote server..."
sshpass -p "$SSH_PASS" ssh -o StrictHostKeyChecking=no "$SSH_USER@$SSH_HOST" \
    "cd $REMOTE_PATH && if [ ! -f .env ]; then cp .env.example .env; fi && make deploy"

echo "✅ Deployment completed successfully!"
