#!/bin/bash
# Smart Customer Core - Sales Server Deployment Script

# Exit immediately if a command exits with a non-zero status
set -e

# Load deploy configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARENT_DIR="$(dirname "$SCRIPT_DIR")"
ENV_FILE="$PARENT_DIR/.env.deploy.sales"

if [ -f "$ENV_FILE" ]; then
    source "$ENV_FILE"
else
    # Fallback to hardcoded defaults just in case
    SSH_HOST="76.13.42.100"
    SSH_USER="root"
    SSH_PASS="Pro.1212##AA"
    REMOTE_DIR="/root/smart-crm"
fi

# Ensure REMOTE_DIR maps to REMOTE_PATH
REMOTE_PATH="${REMOTE_DIR:-/root/smart-crm}"
DOMAIN="prosmartsales.com"

echo "🚀 Starting manual deployment to $SSH_HOST ($DOMAIN)..."

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

echo "🔧 Preparing remote server dependencies..."
# On the remote server, check and install Docker, Docker Compose, Certbot, Make, Rsync
# Also verify if Certbot has already generated certificates for prosmartsales.com
sshpass -p "$SSH_PASS" ssh -o StrictHostKeyChecking=no "$SSH_USER@$SSH_HOST" bash -s <<EOF
    set -e
    
    # 1. Update package list
    apt-get update -y
    
    # 2. Install make, rsync, curl, psmisc (for fuser) if missing
    apt-get install -y make rsync curl certbot psmisc
    
    # 3. Check/Install Docker CE
    if ! command -v docker &> /dev/null; then
        echo "🐳 Installing Docker..."
        curl -fsSL https://get.docker.com | sh
    fi
    
    # 4. Check/Install Docker Compose
    if ! docker compose version &> /dev/null; then
        echo "🐳 Installing Docker Compose plugin..."
        apt-get install -y docker-compose-plugin
    fi

    # 5. Check/Generate SSL Certificates via Certbot Standalone
    if [ ! -f "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ]; then
        echo "🔒 Generating SSL Certificate for $DOMAIN and www.$DOMAIN..."
        # Stop any process listening on port 80 (like nginx container) to free up port for Certbot standalone
        docker compose -f $REMOTE_PATH/docker-compose.yml -f $REMOTE_PATH/docker-compose.production_sales.yml down || true
        # Also kill anything else on port 80 just in case
        fuser -k 80/tcp || true
        
        # Run certbot standalone
        certbot certonly --standalone \
            -d $DOMAIN \
            -d www.$DOMAIN \
            --non-interactive \
            --agree-tos \
            --register-unsafely-without-email
    else
        echo "✅ SSL Certificate for $DOMAIN already exists."
    fi

    # 6. Prepare .env if missing
    cd $REMOTE_PATH
    if [ ! -f .env ]; then
        cp .env.example .env
        # Replace GEMINI_API_KEY, JWT settings, etc. in .env later if needed
    fi
    
    # 7. Start the Docker production stack with the sales compose file
    echo "🚀 Starting docker containers on remote server..."
    docker compose -f docker-compose.yml -f docker-compose.production_sales.yml down
    docker compose -f docker-compose.yml -f docker-compose.production_sales.yml up -d --build
    docker compose -f docker-compose.yml -f docker-compose.production_sales.yml restart nginx

EOF

echo "✅ Deployment completed successfully for $DOMAIN!"
