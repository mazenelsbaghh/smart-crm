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
        source = await client.post(f"{BASE_URL}/projects/{project_id}/ad-manager/conversion-sources", headers=headers, json={"sourceKey": "checkout"})
        source.raise_for_status()
        secret = source.json()["signingSecret"]
        body = json.dumps({"schemaVersion": 1, "externalEventId": "payment-1", "eventType": "Purchase", "occurredAtUtc": "2026-08-17T12:00:00Z", "value": 750, "currency": "EGP", "customer": {"externalId": "customer-1"}, "privacy": {"consentState": "Denied"}}, separators=(",", ":"))
        timestamp = int(time.time())
        signature = hmac.new(secret.encode(), f"{timestamp}.{body}".encode(), hashlib.sha256).hexdigest()
        endpoint = f"{BASE_URL}/integrations/ad-manager/{project_id}/conversions/checkout"
        request_headers = {"X-Ads-Timestamp": str(timestamp), "X-Ads-Signature": f"v1={signature}", "Content-Type": "application/json"}
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
        await client.post(f"{BASE_URL}/projects/{project_id}/ad-manager/conversion-sources", headers=headers, json={"sourceKey": "crm"})
        response = await client.post(f"{BASE_URL}/integrations/ad-manager/{project_id}/conversions/crm", headers={"X-Ads-Timestamp": str(int(time.time())), "X-Ads-Signature": "v1=bad"}, json={"schemaVersion": 1})
        assert response.status_code == 401
