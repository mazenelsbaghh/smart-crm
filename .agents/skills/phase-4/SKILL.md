# Phase 4 Skill: Campaigns, Advanced Analytics & Reporting

## Deliverables Built
1. **Campaign Engine**: CRUD segments & campaigns, schedule campaigns, and view split variant A/B metrics. Includes background `CampaignSenderJob` with staggered, random delay scheduling via Hangfire.
2. **Advanced Analytics Engine**: Daily snapshots aggregate sales volumes, conversion rates, team response times, and AI accuracy. Exposes endpoints for analytics history and on-demand PDF/JSON reports generation.
3. **CRM advanced Pipelines**: Track opportunities/deals and move them across custom order stages (New, Qualified, Proposal, Won, Lost).
4. **Elasticsearch Integration**: Auto-indexes Messages, Customers, and Conversations near real-time on DB saves using transaction commit hooks and publishes `EntityIndexedEvent` via RabbitMQ. Supports multi-tenant wildcard searches.

## Make Targets Added
- `make test-phase-4`: Executes the complete test suite.
- `make campaign-status PROJECT_ID=<uuid>`: Queries the campaigns list.
- `make analytics-dashboard PROJECT_ID=<uuid>`: Recalculates snapshots on-demand.
- `make search-reindex PROJECT_ID=<uuid>`: Resynchronizes database entities to Elasticsearch.

## API Endpoints
- `POST /api/projects/{projectId}/segments`
- `GET /api/projects/{projectId}/segments`
- `POST /api/projects/{projectId}/campaigns`
- `GET /api/projects/{projectId}/campaigns`
- `POST /api/campaigns/{id}/schedule`
- `GET /api/campaigns/{id}/results`
- `POST /api/campaigns/generate-copy` (AI generated templates)
- `GET /api/projects/{projectId}/analytics/{type}`
- `POST /api/projects/{projectId}/reports/generate`
- `GET /api/projects/{projectId}/search?q=<query>`
- `POST /api/projects/{projectId}/search/reindex`
- `POST /api/projects/{projectId}/pipelines/stages`
- `GET /api/projects/{projectId}/pipelines/stages`
- `POST /api/projects/{projectId}/deals`
- `GET /api/projects/{projectId}/deals`

## Verification & Testing
To verify this phase, compile the project and execute the integration tests:

```bash
make up
make db-migrate
make test-phase-4
```
