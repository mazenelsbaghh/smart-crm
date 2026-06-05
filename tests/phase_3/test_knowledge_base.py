import pytest
import httpx
import uuid
import socket

def get_base_urls():
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(1.0)
    try:
        s.connect(("localhost", 443))
        s.close()
        return "https://localhost:443/api", True
    except Exception:
        return "http://localhost:80/api", False

BASE_URL, IS_HTTPS = get_base_urls()

def get_client_kwargs():
    kwargs = {}
    if IS_HTTPS:
        kwargs["verify"] = False
    return kwargs

@pytest.mark.asyncio
async def test_knowledge_base_approval_and_search():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
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

        # 3. Approve Document -> Should publish/approve it
        approve_resp = await client.put(f"{BASE_URL}/knowledge/{doc_id}/approve", headers=headers)
        assert approve_resp.status_code == 200
        assert approve_resp.json()["status"] == "Approved"

        # 4. Search Brain again - should now retrieve the document
        search_resp2 = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=promo+code+SECRET20",
            headers=headers
        )
        assert search_resp2.status_code == 200
        assert len(search_resp2.json()) > 0
        assert "SECRET20" in search_resp2.json()[0]["chunkText"]

        # 5. Reject/Demote Document -> Should return to Rejected status
        reject_resp = await client.put(f"{BASE_URL}/knowledge/{doc_id}/reject", headers=headers)
        assert reject_resp.status_code == 200
        assert reject_resp.json()["status"] == "Rejected"

        # 6. Search Brain again - should NOT retrieve it anymore
        search_resp3 = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=promo+code+SECRET20",
            headers=headers
        )
        assert search_resp3.status_code == 200
        assert len(search_resp3.json()) == 0

        # 7. Create a Suggested Knowledge Document (should default to PendingApproval)
        suggest_doc_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/knowledge/suggest",
            headers=headers,
            json={
                "title": "Suggested Refund Rule",
                "content": "Refund requests are processed in exactly 24 hours.",
                "sourceUrl": "https://example.com/refunds-suggest"
            }
        )
        assert suggest_doc_resp.status_code == 201
        suggest_data = suggest_doc_resp.json()
        suggest_id = suggest_data["id"]
        assert suggest_data["status"] == "PendingApproval"

        # 8. Search Brain - should NOT retrieve the PendingApproval document
        search_resp4 = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=refund+requests",
            headers=headers
        )
        assert search_resp4.status_code == 200
        assert len(search_resp4.json()) == 0

        # 9. Approve the Suggestion
        approve_suggest_resp = await client.put(f"{BASE_URL}/knowledge/{suggest_id}/approve", headers=headers)
        assert approve_suggest_resp.status_code == 200
        assert approve_suggest_resp.json()["status"] == "Approved"

        # 10. Search Brain again - should now retrieve the approved suggestion
        search_resp5 = await client.get(
            f"{BASE_URL}/projects/{proj_id}/brain/search?q=refund+requests",
            headers=headers
        )
        assert search_resp5.status_code == 200
        assert len(search_resp5.json()) > 0
        assert "exactly 24 hours" in search_resp5.json()[0]["chunkText"]
