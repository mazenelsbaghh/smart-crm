import pytest
import httpx
import uuid
import time
import json
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
async def test_risk_analyzer_and_approvals():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # Create Project
        proj_name = f"Approval_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"app_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Create Customer in CRM
        cust_resp = await client.post(
            f"{BASE_URL}/webhooks/whatsapp/message",
            json={
                "projectId": proj_id,
                "messageId": f"msg_{uuid.uuid4().hex[:6]}",
                "sender": f"555{uuid.uuid4().hex[:6]}",
                "content": "I want to buy a product.",
                "messageType": "Text",
                "timestamp": int(time.time())
            }
        )
        assert cust_resp.status_code == 200

        # Retrieve customer ID
        get_custs_resp = await client.get(f"{BASE_URL}/projects/{proj_id}/customers", headers=headers)
        assert get_custs_resp.status_code == 200
        customers = get_custs_resp.json()
        assert len(customers) > 0
        customer = customers[0]
        customer_id = customer["id"]

        # 2. Trigger a Low Risk action (budget = 50,000 <= 1,000,000)
        low_risk_payload = {
            "actionType": "CRMUpdate",
            "payloadJson": json.dumps({"customerId": customer_id, "budget": 50000, "city": "Giza"}),
            "requestedBy": "AI_Worker"
        }
        execute_low = await client.post(
            f"{BASE_URL}/projects/{proj_id}/actions/execute",
            headers=headers,
            json=low_risk_payload
        )
        assert execute_low.status_code == 200
        assert execute_low.json()["status"] == "Executed"
        assert execute_low.json()["riskLevel"] == "Low"

        # Verify low-risk action was executed immediately
        get_cust = await client.get(f"{BASE_URL}/customers/{customer_id}", headers=headers)
        assert get_cust.status_code == 200
        assert get_cust.json()["budget"] == 50000
        assert get_cust.json()["city"] == "Giza"

        # 3. Trigger a High Risk action (budget = 2,500,000 > 1,000,000)
        high_risk_payload = {
            "actionType": "CRMUpdate",
            "payloadJson": json.dumps({"customerId": customer_id, "budget": 2500000, "city": "Alexandria"}),
            "requestedBy": "AI_Worker"
        }
        execute_high = await client.post(
            f"{BASE_URL}/projects/{proj_id}/actions/execute",
            headers=headers,
            json=high_risk_payload
        )
        assert execute_high.status_code == 202
        approval_id = execute_high.json()["id"]
        assert execute_high.json()["status"] == "Pending"
        assert execute_high.json()["riskLevel"] == "High"

        # Verify high-risk action was NOT executed (budget is still 50,000 and city is Giza)
        get_cust = await client.get(f"{BASE_URL}/customers/{customer_id}", headers=headers)
        assert get_cust.json()["budget"] == 50000
        assert get_cust.json()["city"] == "Giza"

        # 4. Get list of pending approvals
        list_approvals = await client.get(f"{BASE_URL}/projects/{proj_id}/approvals?status=Pending", headers=headers)
        assert list_approvals.status_code == 200
        pending_list = list_approvals.json()
        assert any(item["id"] == approval_id for item in pending_list)

        # 5. Reject the first high-risk action request
        reject_resp = await client.post(
            f"{BASE_URL}/approvals/{approval_id}/reject",
            headers=headers,
            json={"notes": "Budget seems abnormally high."}
        )
        assert reject_resp.status_code == 200
        assert reject_resp.json()["status"] == "Rejected"

        # Verify customer remains unchanged
        get_cust = await client.get(f"{BASE_URL}/customers/{customer_id}", headers=headers)
        assert get_cust.json()["budget"] == 50000

        # 6. Trigger another High Risk action (budget = 3,000,000)
        high_risk_payload_2 = {
            "actionType": "CRMUpdate",
            "payloadJson": json.dumps({"customerId": customer_id, "budget": 3000000, "city": "Cairo"}),
            "requestedBy": "AI_Worker"
        }
        execute_high_2 = await client.post(
            f"{BASE_URL}/projects/{proj_id}/actions/execute",
            headers=headers,
            json=high_risk_payload_2
        )
        assert execute_high_2.status_code == 202
        approval_id_2 = execute_high_2.json()["id"]

        # 7. Approve the request
        approve_resp = await client.post(
            f"{BASE_URL}/approvals/{approval_id_2}/approve",
            headers=headers
        )
        assert approve_resp.status_code == 200
        assert approve_resp.json()["status"] == "Approved"

        # Verify action has been executed successfully post-approval
        get_cust = await client.get(f"{BASE_URL}/customers/{customer_id}", headers=headers)
        assert get_cust.json()["budget"] == 3000000
        assert get_cust.json()["city"] == "Cairo"
