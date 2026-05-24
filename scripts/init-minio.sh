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
