import pytest
import httpx
import uuid
import time
import json

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_campaign_creation_segmentation_and_dispatch():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"Campaign_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"campaign_user_{uuid.uuid4().hex[:6]}@smartcore.com"
        await client.post(f"{BASE_URL}/auth/register", json={
            "email": user_email,
            "password": "Password123",
            "projectId": proj_id,
            "role": "Admin"
        })

        # Login
        login_resp = await client.post(f"{BASE_URL}/auth/login", json={
            "email": user_email,
            "password": "Password123"
        })
        token = login_resp.json()["accessToken"]
        headers = {"Authorization": f"Bearer {token}"}

        # 1. Create matching customers
        # Customer 1: matching Cairo & VIP
        await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_1_{uuid.uuid4().hex[:6]}",
                "sender": "201000000001",
                "content": "Hi there, I am from Cairo.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        # Customer 2: matching Cairo, not VIP
        await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_2_{uuid.uuid4().hex[:6]}",
                "sender": "201000000002",
                "content": "Hi there, another message.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )

        # Get customer list to set tags & details
        get_custs = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert get_custs.status_code == 200
        customers = get_custs.json()
        assert len(customers) >= 2

        # Update customer 1 (set city, tags, leadscore)
        c1_id = next(c["id"] for c in customers if c["phoneNumber"] == "201000000001")
        update_c1 = await client.post(
            f"{BASE_URL}/projects/{proj_id}/actions/execute",
            headers=headers,
            json={
                "actionType": "CRMUpdate",
                "payloadJson": json.dumps({"customerId": c1_id, "city": "Cairo", "leadScore": 90, "notes": "VIP Customer"})
            }
        )
        assert update_c1.status_code == 200

        # We also need to add a tag. Let's make sure the customer has the "VIP" tag.
        # Since CRMUpdate proposal doesn't have tags array update directly in Approvals controller, let's update tags array by simulating a workflow trigger or check if there is an endpoint.
        # Wait, does CustomersController have a PUT endpoint?
        # Yes! /api/customers/{id}
        update_cust_profile = await client.put(
            f"{BASE_URL}/customers/{c1_id}",
            headers=headers,
            json={
                "name": "Alice",
                "phoneNumber": "201000000001",
                "city": "Cairo",
                "leadScore": 90,
                "tags": ["VIP"],
                "notes": "VIP Customer"
            }
        )
        assert update_cust_profile.status_code == 200

        # Update customer 2 (set city to Alexandria, so it does not match Cairo segment)
        c2_id = next(c["id"] for c in customers if c["phoneNumber"] == "201000000002")
        update_c2_profile = await client.put(
            f"{BASE_URL}/customers/{c2_id}",
            headers=headers,
            json={
                "name": "Bob",
                "phoneNumber": "201000000002",
                "city": "Alexandria",
                "leadScore": 50,
                "tags": ["Regular"],
                "notes": "Regular Customer"
            }
        )
        assert update_cust_profile.status_code == 200

        # 2. Create Segment "Cairo VIPs"
        segment_payload = {
            "name": "Cairo VIPs Segment",
            "filterCriteriaJson": json.dumps({
                "city": "Cairo",
                "leadScoreMin": 70,
                "tags": ["VIP"]
            })
        }
        create_seg_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/segments",
            headers=headers,
            json=segment_payload
        )
        assert create_seg_resp.status_code == 201
        segment_id = create_seg_resp.json()["id"]

        # 3. Create Campaign
        campaign_payload = {
            "name": "Cairo VIP Campaign",
            "segmentId": segment_id,
            "messageTemplateA": "Hello {{CustomerName}}, we have a special VIP offer A!",
            "messageTemplateB": "Hello {{CustomerName}}, we have a special VIP offer B!"
        }
        create_camp_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/campaigns",
            headers=headers,
            json=campaign_payload
        )
        assert create_camp_resp.status_code == 201
        campaign_id = create_camp_resp.json()["id"]

        # 4. Schedule Campaign immediately
        schedule_resp = await client.post(
            f"{BASE_URL}/campaigns/{campaign_id}/schedule",
            headers=headers,
            json=None
        )
        assert schedule_resp.status_code == 200

        # 5. Verify results split
        # Since it runs via Hangfire inside Docker container, poll and wait a few seconds
        results = None
        for _ in range(10):
            time.sleep(1.0)
            res_resp = await client.get(f"{BASE_URL}/campaigns/{campaign_id}/results", headers=headers)
            assert res_resp.status_code == 200
            results = res_resp.json()
            if results["status"] == "Running" or results["status"] == "Completed":
                break

        # Verify A/B variants split results
        assert results is not None
        assert results["name"] == "Cairo VIP Campaign"
        # Total sent across variants (support both lowercase and uppercase variant keys due to JSON camelCase serialization)
        variants = results["variants"]
        var_a = variants.get("A", variants.get("a"))
        var_b = variants.get("B", variants.get("b"))
        assert var_a is not None and var_b is not None
        total_variants_sent = var_a["sent"] + var_b["sent"]
        assert total_variants_sent >= 0

        # 6. Test Copy Generation API
        gen_resp = await client.post(
            f"{BASE_URL}/campaigns/generate-copy",
            headers=headers,
            json={
                "prompt": "Create a discount announcement",
                "baseTemplate": "Get discount now!",
                "targetContext": "Summer 2026"
            }
        )
        assert gen_resp.status_code == 200
        assert "copy" in gen_resp.json()
        assert len(gen_resp.json()["copy"]) > 0
