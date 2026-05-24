import pytest
import httpx
import uuid

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_knowledge_base_approval_and_search():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"KB_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"kb_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Create a Knowledge Base Document (should default to Draft)
        create_doc_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/knowledge",
            headers=headers,
            json={
                "title": "Secret Discount Policy",
                "content": "Use promo code SECRET20 to get 20% off all subscriptions.",
                "sourceUrl": "https://example.com/secret",
                "status": "Draft"
            }
        )
        assert create_doc_resp.status_code == 201
        doc_data = create_doc_resp.json()
        doc_id = doc_data["id"]
        assert doc_data["status"] == "Draft"

        # 2. Search Brain - should NOT retrieve the Draft document
        search_resp = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=promo+code+SECRET20",
            headers=headers
        )
        assert search_resp.status_code == 200
        assert len(search_resp.json()) == 0

        # 3. Approve Document -> Should publish it
        approve_resp = await client.put(f"{BASE_URL}/knowledge/{doc_id}/approve", headers=headers)
        assert approve_resp.status_code == 200
        assert approve_resp.json()["status"] == "Published"

        # 4. Search Brain again - should now retrieve the document
        search_resp2 = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=promo+code+SECRET20",
            headers=headers
        )
        assert search_resp2.status_code == 200
        assert len(search_resp2.json()) > 0
        assert "SECRET20" in search_resp2.json()[0]["chunkText"]

        # 5. Reject/Demote Document -> Should return to Draft status
        reject_resp = await client.put(f"{BASE_URL}/knowledge/{doc_id}/reject", headers=headers)
        assert reject_resp.status_code == 200
        assert reject_resp.json()["status"] == "Draft"

        # 6. Search Brain again - should NOT retrieve it anymore
        search_resp3 = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=promo+code+SECRET20",
            headers=headers
        )
        assert search_resp3.status_code == 200
        assert len(search_resp3.json()) == 0
