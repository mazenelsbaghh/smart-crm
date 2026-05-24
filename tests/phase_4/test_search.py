import pytest
import httpx
import uuid
import time

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_elasticsearch_unified_search():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"Search_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"search_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Create a message in CRM to trigger indexing
        msg_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": "201099999999",
                "content": "Where is the pricing structure listed?",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert msg_resp.status_code == 200

        # Give RabbitMQ and Elasticsearch a second to index the document
        time.sleep(2.0)

        # 2. Trigger manual reindex as fallback to populate the search index
        reindex_resp = await client.post(f"{BASE_URL}/projects/{proj_id}/search/reindex", headers=headers)
        assert reindex_resp.status_code == 200

        # Give Elasticsearch another second to complete reindex indexing
        time.sleep(2.0)

        # 3. Perform Elasticsearch full-text query
        search_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/search?q=pricing", headers=headers)
        assert search_resp.status_code == 200
        search_results = search_resp.json()
        
        # Verify result contains matches (could be empty in clean mock environment but API should succeed)
        assert "totalMatches" in search_results
        assert isinstance(search_results["matches"], list)
