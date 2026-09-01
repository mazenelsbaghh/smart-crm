import asyncio

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

        document = await client.post(
            f"{BASE_URL}/projects/{project_id}/knowledge",
            headers=headers,
            json={
                "title": "WhatsApp Course",
                "content": "كورس عملي متاح في مصر بسعر 1000 جنيه والتسجيل عبر واتساب.",
                "sourceUrl": "https://example.test/whatsapp-course",
            },
        )
        document.raise_for_status()
        approved = await client.put(f"{BASE_URL}/knowledge/{document.json()['id']}/approve", headers=headers)
        approved.raise_for_status()

        offers = []
        # The production outbox is dispatched on the minute; accept its real cadence here.
        for _ in range(130):
            response = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/offers", headers=headers)
            response.raise_for_status()
            offers = [offer for offer in response.json() if offer["state"] == "Eligible"]
            if offers:
                break
            await asyncio.sleep(0.5)
        assert offers, "approved knowledge was not projected into an eligible advertising offer"

        destinations = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/destinations", headers=headers)
        destinations.raise_for_status()
        destination_id = destinations.json()[0]["id"]
        envelope = await client.put(
            f"{BASE_URL}/projects/{project_id}/ad-manager/envelope",
            headers={**headers, "Idempotency-Key": "existing-envelope"},
            json={
                "offerId": offers[0]["id"],
                "destinationId": destination_id,
                "dailyCap": 500,
                "periodCap": 5000,
                "periodCapKind": "Monthly",
                "currency": "EGP",
                "safetyReservePercent": 10,
                "maximumIncreasePercent": 20,
                "cooldownHours": 12,
                "allowedCountries": ["EG"],
                "excludedCountries": [],
                "minimumAge": 18,
                "requiredLanguages": ["ar"],
                "customAudienceExclusions": [],
                "reportingTimezoneIana": "Africa/Cairo",
                "startsAtUtc": None,
                "endsAtUtc": None,
            },
        )
        envelope.raise_for_status()
        activated = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/envelope/{envelope.json()['id']}/activate",
            headers={**headers, "Idempotency-Key": "activate-existing-envelope", "If-Match": f'"{envelope.json()["version"]}"'},
        )
        activated.raise_for_status()

        preview = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns/facebook-existing", headers=headers)
        preview.raise_for_status()
        candidate = preview.json()[0]
        assert candidate["eligible"] is True
        assert candidate["publisherPlatforms"] == ["facebook"]

        imported = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns/import-facebook",
            headers=headers,
            json={"adIds": [candidate["adId"]], "confirmOwnershipTransfer": True},
        )
        imported.raise_for_status()
        assert imported.json()["importedAds"] == 1

        repeated = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns/import-facebook",
            headers=headers,
            json={"adIds": [candidate["adId"]], "confirmOwnershipTransfer": True},
        )
        repeated.raise_for_status()
        assert repeated.json()["importedAds"] == 0

        campaigns = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/campaigns", headers=headers)
        campaigns.raise_for_status()
        assert campaigns.json()[0]["managementSource"] == "ImportedWithAuthority"
