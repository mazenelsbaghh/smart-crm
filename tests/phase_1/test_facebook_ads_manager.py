import httpx
import pytest

from ._ads_test_support import BASE_URL, connect_mock_facebook, create_admin


@pytest.mark.asyncio
async def test_mock_connection_discovers_only_project_scoped_facebook_resources():
    async with httpx.AsyncClient(timeout=20) as client:
        project_a, headers_a = await create_admin(client, "ads-a")
        project_b, _ = await create_admin(client, "ads-b")
        catalog = await connect_mock_facebook(client, project_a, headers_a)
        assert catalog["adAccounts"] and catalog["pages"] and catalog["datasets"]
        assert all("instagram" not in str(resource).lower() for group in catalog.values() for resource in group)
        cross_project = await client.get(f"{BASE_URL}/projects/{project_b}/ad-manager/overview", headers=headers_a)
        assert cross_project.status_code == 403


@pytest.mark.asyncio
async def test_page_posts_include_images_and_videos_without_non_facebook_placements():
    async with httpx.AsyncClient(timeout=20) as client:
        project_id, headers = await create_admin(client, "ads-posts")
        await connect_mock_facebook(client, project_id, headers)
        response = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/facebook/page-posts", headers=headers)
        response.raise_for_status()
        assert {post["mediaType"] for post in response.json()} == {"Image", "Video"}


@pytest.mark.asyncio
async def test_existing_facebook_campaign_can_be_imported_once_for_ai_management():
    async with httpx.AsyncClient(timeout=20) as client:
        project_id, headers = await create_admin(client, "ads-existing")
        await connect_mock_facebook(client, project_id, headers)
        envelope = await client.put(
            f"{BASE_URL}/projects/{project_id}/ad-manager/envelope",
            headers={**headers, "Idempotency-Key": "existing-envelope"},
            json={"dailyCap": 500, "currency": "EGP", "safetyReservePercent": 10,
                  "maximumIncreasePercent": 20, "cooldownHours": 12, "allowedCountries": ["EG"]},
        )
        envelope.raise_for_status()

        preview = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns/facebook-existing", headers=headers)
        preview.raise_for_status()
        candidate = preview.json()[0]
        assert candidate["eligible"] is True
        assert candidate["publisherPlatforms"] == ["facebook"]

        imported = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns/import-facebook",
            headers=headers,
            json={"adIds": [candidate["adId"]]},
        )
        imported.raise_for_status()
        assert imported.json()["importedAds"] == 1

        repeated = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns/import-facebook",
            headers=headers,
            json={"adIds": [candidate["adId"]]},
        )
        repeated.raise_for_status()
        assert repeated.json()["importedAds"] == 0

        campaigns = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns", headers=headers)
        campaigns.raise_for_status()
        assert campaigns.json()[0]["managementSource"] == "ImportedFromMeta"
