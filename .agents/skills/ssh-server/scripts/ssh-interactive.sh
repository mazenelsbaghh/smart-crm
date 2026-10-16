#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/../../../.." && pwd)"
if [ -f "$PROJECT_DIR/.env.deploy" ]; then source "$PROJECT_DIR/.env.deploy"; fi
SSH_HOST="${SSH_HOST:-147.93.86.206}"
SSH_USER="${SSH_USER:-root}"
if [ -n "${SSH_PASS:-}" ]; then
    export SSHPASS="$SSH_PASS"
    exec sshpass -e ssh -t -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 "$SSH_USER@$SSH_HOST" "$@"
fi
exec ssh -t -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 "$SSH_USER@$SSH_HOST" "$@"
