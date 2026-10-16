#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

fail() { echo "Deployment stopped: $*" >&2; exit 1; }

[[ $# -eq 3 ]] || fail "Usage: $0 <full-git-sha> <staged-source-directory> <application-directory>"
RELEASE_SHA="$1"
[[ "$RELEASE_SHA" =~ ^[a-f0-9]{40}$ ]] || fail "A full Git SHA is required."
STAGED_DIR="$(realpath "$2")"
APP_DIR="$(realpath "$3")"
[[ "$STAGED_DIR" != "$APP_DIR" && "$STAGED_DIR" != "$APP_DIR/"* ]] || fail "Stage the release outside the running application."
[[ -f "$STAGED_DIR/backend/Dockerfile" && -f "$STAGED_DIR/ops/deploy-excludes.txt" ]] || fail "Staged release is incomplete."

cd "$APP_DIR"
exec 9> .deploy.lock
flock -w 1800 9 || fail "Another deployment still holds the lock."
COMPOSE=(docker compose --project-name smartcustomercore --project-directory "$APP_DIR" --env-file "$APP_DIR/.env" -f "$APP_DIR/docker-compose.yml" -f "$APP_DIR/docker-compose.production.yml")
SERVICES=(backend frontend whatsapp-gateway)
BACKUP_DIR="$APP_DIR/backups/releases/$RELEASE_SHA"
mkdir -p .deployment

trap 'echo "Deployment failed. No automatic database rollback was attempted. Backups: $BACKUP_DIR; preserved images: smartcustomercore-<service>:before-$RELEASE_SHA" >&2' ERR

verify_application() {
    docker exec smartcustomercore-frontend node -e '
        Promise.all([
            fetch("http://127.0.0.1:3000/", {signal: AbortSignal.timeout(10000)}),
            fetch("http://backend:5000/api/system/health", {signal: AbortSignal.timeout(10000)}),
            fetch("http://whatsapp-gateway:3000/api/whatsapp/session/status", {signal: AbortSignal.timeout(10000)})
        ]).then(([ui, api, gateway]) => {
            if (!ui.ok || !api.ok || gateway.status !== 400) process.exit(1);
        }).catch(() => process.exit(1));' >/dev/null 2>&1 &&
    docker exec smartcustomercore-nginx curl -fsS --max-time 10 http://127.0.0.1/nginx-health >/dev/null 2>&1 &&
    docker exec smartcustomercore-nginx curl -fsS --max-time 10 http://127.0.0.1/api/system/health >/dev/null 2>&1 &&
    docker exec smartcustomercore-nginx curl -fsS --max-time 10 http://127.0.0.1/ >/dev/null 2>&1
}

wait_for_application() {
    local deadline=$((SECONDS + 300))
    while (( SECONDS < deadline )); do
        if verify_application; then return; fi
        sleep 5
    done
    fail "Application readiness checks did not pass. Inspect service logs before retrying."
}

[[ -s .env && -s firebase-key.json ]] || fail "Existing production .env and firebase-key.json are required."
[[ -d whatsapp-gateway/sessions ]] || fail "Existing WhatsApp session directory is required."

validate_environment() {
    # Resolve the candidate Compose configuration against the preserved production env without logging secrets.
    docker compose --project-name smartcustomercore --project-directory "$STAGED_DIR" --env-file "$APP_DIR/.env" \
        -f "$STAGED_DIR/docker-compose.yml" -f "$STAGED_DIR/docker-compose.production.yml" config --format json |
    python3 -c '
import json, sys
secret = json.load(sys.stdin)["services"]["backend"]["environment"].get("JWT__Secret", "")
if len(secret.encode()) < 32 or secret == "a_very_long_and_secure_secret_key_that_is_at_least_32_characters_long" or any(word in secret.lower() for word in ("changeme", "change-me", "your-secret", "replace-me")):
    sys.exit("Production JWT_SECRET must contain at least 32 bytes and cannot be a placeholder.")
'
}

database_query() {
    docker exec smartcustomercore-postgres sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "$1"' sh "$1"
}

backup_release() {
    if [[ -f "$BACKUP_DIR/complete" ]]; then return; fi
    local database_bytes free_bytes
    database_bytes="$(database_query 'SELECT pg_database_size(current_database());')"
    free_bytes="$(( $(df -Pk "$APP_DIR" | awk 'NR==2 {print $4}') * 1024 ))"
    (( free_bytes > database_bytes * 2 + 5368709120 )) || fail "Insufficient space for verified backup and builds (need twice the database size plus 5 GiB free)."
    mkdir -p "$BACKUP_DIR"
    docker exec smartcustomercore-postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$BACKUP_DIR/database.dump.partial"
    docker exec -i smartcustomercore-postgres pg_restore --list < "$BACKUP_DIR/database.dump.partial" > "$BACKUP_DIR/database.contents"
    mv "$BACKUP_DIR/database.dump.partial" "$BACKUP_DIR/database.dump"
    backup_source_and_images
    database_query 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";' > "$BACKUP_DIR/migrations-before.txt"
    date -u +%FT%TZ > "$BACKUP_DIR/complete"
}

backup_source_and_images() {
    local service image_id
    sed -n '/^\//! { s@/$@@; p; }' "$STAGED_DIR/ops/deploy-excludes.txt" > "$BACKUP_DIR/source-excludes.txt"
    # Root output/report artifacts must not exclude source packages with the same directory name.
    tar -czf "$BACKUP_DIR/source.tar.gz" --anchored --exclude=./output --exclude=./reports \
        --no-anchored --exclude-from="$BACKUP_DIR/source-excludes.txt" -C "$APP_DIR" .
    tar -czf "$BACKUP_DIR/config.tar.gz" -C "$APP_DIR" .env firebase-key.json docker-compose.yml docker-compose.production.yml nginx
    if [[ -d .git ]]; then git rev-parse HEAD > "$BACKUP_DIR/source-head.txt"; git status --porcelain > "$BACKUP_DIR/source-status.txt"; fi
    : > "$BACKUP_DIR/images-before.tsv"
    for service in "${SERVICES[@]}"; do
        image_id="$(docker inspect --format '{{.Image}}' "smartcustomercore-$service")"
        docker image tag "$image_id" "smartcustomercore-$service:before-$RELEASE_SHA"
        printf '%s\t%s\n' "$service" "$image_id" >> "$BACKUP_DIR/images-before.tsv"
    done
    sha256sum "$BACKUP_DIR/database.dump" "$BACKUP_DIR/source.tar.gz" "$BACKUP_DIR/config.tar.gz" > "$BACKUP_DIR/checksums.txt"
}

verify_migrations() {
    local migration_file migration_id
    database_query 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";' > .deployment/migrations-applied.txt
    for migration_file in backend/Migrations/[0-9]*.cs; do
        [[ "$migration_file" == *.Designer.cs ]] && continue
        migration_id="$(basename "$migration_file" .cs)"
        grep -Fxq "$migration_id" .deployment/migrations-applied.txt || fail "Migration $migration_id is missing; startup health does not prove migration success."
    done
}

release_images_are_running() {
    local service expected_image running_image
    for service in "${SERVICES[@]}"; do
        expected_image="$(docker image inspect --format '{{.Id}}' "smartcustomercore-$service:release-$RELEASE_SHA")" || return 1
        running_image="$(docker inspect --format '{{.Image}}' "smartcustomercore-$service")" || return 1
        [[ "$expected_image" == "$running_image" ]] || return 1
    done
}

if [[ -f .deployment/current && "$(cat .deployment/current)" == "$RELEASE_SHA" ]] && release_images_are_running; then
    wait_for_application
    verify_migrations
    echo "Release $RELEASE_SHA is already deployed and healthy."
    exit 0
fi

validate_environment
backup_release
rsync -a --delete --exclude-from="$STAGED_DIR/ops/deploy-excludes.txt" "$STAGED_DIR/" "$APP_DIR/"
"${COMPOSE[@]}" config --quiet
# Validate the new bind-mounted config before replacing the live proxy.
"${COMPOSE[@]}" run --rm --no-deps --entrypoint nginx nginx -t
for service in "${SERVICES[@]}"; do
    "${COMPOSE[@]}" build "$service"
    docker image tag "smartcustomercore-$service:latest" "smartcustomercore-$service:release-$RELEASE_SHA"
done
"${COMPOSE[@]}" up -d --no-deps --no-build --wait --wait-timeout 300 "${SERVICES[@]}"
# rsync may replace the config inode; recreation guarantees the proxy sees the validated file.
"${COMPOSE[@]}" up -d --no-deps --no-build --force-recreate --wait --wait-timeout 90 nginx
wait_for_application
verify_migrations
for service in "${SERVICES[@]}"; do
    docker image tag "smartcustomercore-$service:before-$RELEASE_SHA" "smartcustomercore-$service:previous"
    docker image tag "smartcustomercore-$service:release-$RELEASE_SHA" "smartcustomercore-$service:current"
done
printf '%s\n' "$RELEASE_SHA" > .deployment/current.next
mv .deployment/current.next .deployment/current
echo "Release $RELEASE_SHA deployed; readiness and all source migrations verified."
