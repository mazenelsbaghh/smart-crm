import hashlib
import hmac
import json
import time

import httpx
import pytest

from ._ads_test_support import BASE_URL, create_admin


@pytest.mark.asyncio
async def test_signed_conversion_is_canonical_and_duplicate_is_idempotent():
    async with httpx.AsyncClient(timeout=20) as client:
        project_id, headers = await create_admin(client, "ads-conversion")
        source = await client.post(f"{BASE_URL}/projects/{project_id}/ad-manager/webhook-sources", headers={**headers, "Idempotency-Key": "create-checkout"}, json={"sourceKey": "checkout"})
        source.raise_for_status()
        secret = source.json()["signingSecret"]
        body = json.dumps({"schemaVersion": 2, "externalEventId": "payment-1", "businessAggregate": {"type": "Order", "id": "order-1"}, "eventType": "Purchase", "journeyLocation": "WhatsAppThread", "occurredAtUtc": "2026-08-17T12:00:00Z", "value": 750, "currency": "EGP", "customer": {"externalId": "customer-1"}, "privacy": {"consentState": "Denied"}}, separators=(",", ":"))
        timestamp = int(time.time())
        signature = hmac.new(secret.encode(), f"{timestamp}.{body}".encode(), hashlib.sha256).hexdigest()
        endpoint = f"{BASE_URL}/integrations/ad-manager/{project_id}/conversions/checkout"
        request_headers = {"Idempotency-Key": "payment-1", "X-Ads-Timestamp": str(timestamp), "X-Ads-Signature": f"v1={signature}", "Content-Type": "application/json"}
        first = await client.post(endpoint, headers=request_headers, content=body)
        second = await client.post(endpoint, headers=request_headers, content=body)
        assert first.status_code == second.status_code == 202
        assert first.json()["conversionId"] == second.json()["conversionId"]
        assert second.json()["duplicate"] is True
        listed = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/conversions", headers=headers)
        assert len([item for item in listed.json() if item["eventType"] == "Purchase"]) == 1


@pytest.mark.asyncio
async def test_tampered_conversion_signature_is_rejected():
    async with httpx.AsyncClient(timeout=20) as client:
        project_id, headers = await create_admin(client, "ads-signature")
        await client.post(f"{BASE_URL}/projects/{project_id}/ad-manager/webhook-sources", headers={**headers, "Idempotency-Key": "create-crm"}, json={"sourceKey": "crm"})
        response = await client.post(f"{BASE_URL}/integrations/ad-manager/{project_id}/conversions/crm", headers={"Idempotency-Key": "tampered-1", "X-Ads-Timestamp": str(int(time.time())), "X-Ads-Signature": "v1=bad"}, json={"schemaVersion": 1})
        assert response.status_code == 401


@pytest.mark.asyncio
async def test_webhook_secret_rotation_is_one_time_and_revoke_blocks_future_events():
    async with httpx.AsyncClient(timeout=20) as client:
        project_id, headers = await create_admin(client, "ads-source-lifecycle")
        created = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/webhook-sources",
            headers={**headers, "Idempotency-Key": "create-orders"},
            json={"sourceKey": "orders", "allowedEventTypes": ["Purchase", "Refund"]},
        )
        created.raise_for_status()
        source_id = created.json()["id"]
        assert created.json()["shownOnce"] is True
        listed = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/webhook-sources", headers=headers)
        assert "signingSecret" not in listed.text

        rotated = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/webhook-sources/{source_id}/rotate",
            headers={**headers, "Idempotency-Key": "rotate-orders"},
        )
        rotated.raise_for_status()
        assert rotated.json()["shownOnce"] is True
        revoked = await client.post(
            f"{BASE_URL}/projects/{project_id}/ad-manager/webhook-sources/{source_id}/revoke",
            headers={**headers, "Idempotency-Key": "revoke-orders"},
        )
        assert revoked.status_code == 204
