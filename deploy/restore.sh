#!/bin/bash
set -e

BACKUP_FILE="$1"

if [ -z "${BACKUP_FILE}" ]; then
    echo "⚠️ Usage: $0 <path_to_backup_archive.tar.gz>"
    exit 1
fi

if [ ! -f "${BACKUP_FILE}" ]; then
    echo "❌ Backup file not found: ${BACKUP_FILE}"
    exit 1
fi

echo "🏁 Starting restore process from ${BACKUP_FILE}..."

TEMP_RESTORE_DIR="/tmp/smartcore_restore_temp"
rm -rf "${TEMP_RESTORE_DIR}"
mkdir -p "${TEMP_RESTORE_DIR}"

# Extract archive
echo "🗄️ Extracting archive..."
tar -xzf "${BACKUP_FILE}" -C "${TEMP_RESTORE_DIR}"

# Get the name of the extracted folder
EXTRACTED_FOLDER=$(ls "${TEMP_RESTORE_DIR}")
EXTRACTED_PATH="${TEMP_RESTORE_DIR}/${EXTRACTED_FOLDER}"

# 1. Restore PostgreSQL
echo "🐘 Restoring PostgreSQL..."
docker cp "${EXTRACTED_PATH}/db.dump" smartcustomercore-postgres:/tmp/db.restore.dump

# Terminate active DB connections
docker exec smartcustomercore-postgres psql -U smartcore -d postgres -c "SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = 'smartcustomercore' AND pid <> pg_backend_pid();" || true

# Recreate DB and restore
docker exec smartcustomercore-postgres psql -U smartcore -d postgres -c "DROP DATABASE IF EXISTS smartcustomercore;"
docker exec smartcustomercore-postgres psql -U smartcore -d postgres -c "CREATE DATABASE smartcustomercore;"
docker exec smartcustomercore-postgres pg_restore -U smartcore -d smartcustomercore /tmp/db.restore.dump || true
docker exec smartcustomercore-postgres rm /tmp/db.restore.dump

# 2. Restore Redis
echo "💾 Restoring Redis cache..."
docker cp "${EXTRACTED_PATH}/redis.rdb" smartcustomercore-redis:/data/dump.rdb
echo "🔄 Restarting Redis to load restored snapshot..."
docker compose restart redis

# 3. Restore MinIO
echo "📦 Restoring MinIO object storage..."
# Copy contents inside minio_data back to container
docker cp "${EXTRACTED_PATH}/minio_data/." smartcustomercore-minio:/data/
echo "🔄 Restarting MinIO to refresh storage state..."
docker compose restart minio

# 4. Restart Backend to refresh connection pools
echo "🔄 Restarting Backend to refresh connection pools..."
docker compose restart backend
echo "⏳ Waiting for backend to boot up..."
sleep 8

# Cleanup temp files
rm -rf "${TEMP_RESTORE_DIR}"

echo "✅ System restore complete!"
