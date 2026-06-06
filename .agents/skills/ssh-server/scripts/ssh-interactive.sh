#!/bin/bash
# Opens an interactive SSH terminal on the remote production server

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$(dirname "$(dirname "$SCRIPT_DIR")")")"

# Load credentials
if [ -f "${PROJECT_DIR}/.env.deploy" ]; then
    source "${PROJECT_DIR}/.env.deploy"
fi

SSH_HOST="${SSH_HOST:-147.93.86.206}"
SSH_USER="${SSH_USER:-root}"
SSH_PASS="${SSH_PASS:-MazenElsbagh.12}"

echo "🔌 Connecting interactively to ${SSH_USER}@${SSH_HOST}..."
sshpass -p "$SSH_PASS" ssh -t -o StrictHostKeyChecking=no "${SSH_USER}@${SSH_HOST}"
