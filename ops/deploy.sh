#!/bin/bash
# Smart Customer Core - Manual Deployment Script

# Exit immediately if a command exits with a non-zero status
set -e

# Server configuration
SSH_HOST="147.93.86.206"
SSH_USER="root"
SSH_PASS="MazenElsbagh.12"
REMOTE_PATH="/root/smart-crm"

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
