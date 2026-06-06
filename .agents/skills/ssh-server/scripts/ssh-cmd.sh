#!/bin/bash
# Executes a command on the remote production server

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$(dirname "$(dirname "$SCRIPT_DIR")")")"

# Load credentials
if [ -f "${PROJECT_DIR}/.env.deploy" ]; then
    source "${PROJECT_DIR}/.env.deploy"
fi

SSH_HOST="${SSH_HOST:-147.93.86.206}"
SSH_USER="${SSH_USER:-root}"
SSH_PASS="${SSH_PASS:-MazenElsbagh.12}"

if [ -z "$1" ]; then
    echo "Usage: $0 <command>"
    exit 1
fi

sshpass -p "$SSH_PASS" ssh -o StrictHostKeyChecking=no "${SSH_USER}@${SSH_HOST}" "$@"
