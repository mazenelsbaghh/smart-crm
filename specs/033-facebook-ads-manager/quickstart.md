# Quickstart: Facebook Ads Manager

This feature can execute real spend. Complete the mock-provider verification before using real Meta credentials.

## Configuration

Provide secrets through environment/secret storage—never commit values:

```text
FACEBOOK_APP_ID
FACEBOOK_APP_SECRET
FACEBOOK_ADS_OAUTH_REDIRECT_URI
FACEBOOK_GRAPH_API_VERSION=v25.0
Advertising__Meta__UseMock=true        # Development/Test only
Advertising__SafetyReservePercent=15
DataProtection__KeysPath=/app/data-protection-keys
```

The Data Protection path must be a persistent, access-restricted volume. Remove embedded/default Facebook secrets from Compose and rotate any value that was previously exposed.

## Local verification with no spend

1. Build/start PostgreSQL, Redis, RabbitMQ, MinIO, backend and frontend with the mock Meta adapter enabled.
2. Apply the generated EF migration by starting the backend or running `dotnet ef database update` from `backend`.
3. Sign in as Owner/Admin, select a test Project, open `مدير الإعلانات` and confirm the legacy item reads `حملات واتساب`.
4. Complete the mock connection, select mock Ad Account/Page/Dataset, choose one sourced offer and create the signed webhook source.
5. Send a signed test conversion twice with the same ID. Verify one canonical conversion, one delivery identity and a healthy tracking result.
6. Set a small mock daily cap, review the recommended image/video/post candidates and activate. Verify provider entities are first created paused and every placement is Facebook-only.
7. Run the job triggers and confirm allocations remain below usable cap, insufficient evidence produces `WAIT`, and a replayed command is not sent twice.
8. Activate Emergency Stop. Verify pending commands block and all mock system-owned ads pause without deletion; test the explicit recovery flow.

## Real Meta prerequisites

- Meta App Review/appropriate access for the production account model.
- `ads_read`, `ads_management`, required business/resource discovery and Page-read capabilities granted.
- Selected Ad Account active, funded and in expected currency/timezone.
- Page and Dataset belong to/are usable by the selected business/account.
- Valid privacy notice and recorded consent/legal-basis mapping for any customer match data.
- One test Dataset event accepted, conversion webhook signature verified and tracking health green.
- Published project knowledge has a verified offer, destination, markets, price/schedule and prohibited claims.
- Owner/Admin has approved the exact cap, time window, locations and maximum increase.

Turn `Advertising__Meta__UseMock` off only after these checks. The first real activation is a guarded canary with reserve, but it is not shadow mode and can spend real money.

## Build and tests

```bash
dotnet build backend/backend.csproj
dotnet test backend/tests/Advertising.UnitTests/Advertising.UnitTests.csproj
cd frontend && npx eslint 'src/app/(dashboard)/management/ad-manager/page.tsx' src/packages/ad-manager src/config/navigation.ts src/packages/inbox/shared/ThinSidebar.tsx 'src/app/(dashboard)/layout.tsx'
cd frontend && npm run build
docker compose config
TEST_API_BASE_URL=http://localhost:${BACKEND_PORT:-5000}/api .venv/bin/pytest -q tests/phase_1/test_facebook_ads_manager.py tests/phase_1/test_facebook_ads_conversions.py tests/phase_1/test_facebook_ads_safety_jobs.py
```

Run relevant WhatsApp campaigns, knowledge, CRM, booking and media regression suites because this feature changes shared navigation, authorization and integration-event infrastructure.

## Recurring schedule

All schedules are stored as UTC cron expressions. Cairo is UTC+3 on the supported deployment calendar, so the daily rebalance at `01:00 UTC` runs at `04:00 Cairo`.

| Work | Schedule |
|---|---:|
| Conversion delivery and transactional outbox | Every minute |
| Spend guard | Every 5 minutes |
| Provider state reconciliation | Every 10 minutes |
| Tracking health | Every 15 minutes |
| Incremental Meta Insights | At minute 7 and 37 |
| Decision cycle | Hourly |
| Impact review | Every 2 hours |
| Creative fatigue | Every 6 hours |
| New test proposal | Every 2 days |
| Budget rebalance | Daily at 04:00 Cairo |
| Strategy review | Monday at 06:31 Cairo |
| Retention | Daily at 05:43 Cairo |

Each project run obtains a Redis lease and records a durable project/job/time-bucket result. A missing connection, unhealthy tracking, active Emergency Stop, invalid Facebook placement, or exhausted usable cap blocks financial commands while read-only reconciliation continues.

## Operational checks

- Inspect Hangfire for one successful project run per job/time bucket.
- Confirm logs include ProjectId/correlation/command IDs but no credentials or match PII.
- Confirm read-only sync continues during tracking freeze.
- Simulate token expiry, Dataset rejection, delayed insights, command timeout and RabbitMQ unavailability.
- Confirm dead-letter/incident behavior is bounded and actionable.
- Validate desktop/mobile RTL at 375, 768, 1024 and 1440 px, including keyboard focus and reduced motion.
