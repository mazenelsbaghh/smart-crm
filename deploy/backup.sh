#!/bin/bash
set -e

# Configuration
BACKUP_DIR="${BACKUP_DIR:-/tmp/smartcore_backups}"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_NAME="smartcore_backup_${TIMESTAMP}"
TEMP_DIR="/tmp/${BACKUP_NAME}"

echo "🏁 Starting Smart Customer Core backup..."
mkdir -p "${BACKUP_DIR}"
mkdir -p "${TEMP_DIR}"

# 1. PostgreSQL Backup
echo "🐘 Backing up PostgreSQL..."
docker exec smartcustomercore-postgres pg_dump -U smartcore -d smartcustomercore -F c -b -v -f /tmp/db.dump
docker cp smartcustomercore-postgres:/tmp/db.dump "${TEMP_DIR}/db.dump"
docker exec smartcustomercore-postgres rm /tmp/db.dump

# 2. Redis Backup
echo "💾 Backing up Redis cache..."
docker exec smartcustomercore-redis redis-cli SAVE
docker cp smartcustomercore-redis:/data/dump.rdb "${TEMP_DIR}/redis.rdb"

# 3. MinIO Backup
echo "📦 Backing up MinIO object storage..."
docker cp smartcustomercore-minio:/data "${TEMP_DIR}/minio_data"

# 4. Create single archive
echo "🗄️ Packaging backups..."
tar -czf "${BACKUP_DIR}/${BACKUP_NAME}.tar.gz" -C "/tmp" "${BACKUP_NAME}"

# Cleanup temp files
rm -rf "${TEMP_DIR}"

echo "✅ Backup complete: ${BACKUP_DIR}/${BACKUP_NAME}.tar.gz"
