import os
import uuid

import httpx

BASE_URL = os.getenv("TEST_API_BASE_URL", "http://localhost:80/api")
BOOTSTRAP_PROJECT_ID = "d3b07384-d113-4a15-bbf9-000000000000"


async def create_admin(client: httpx.AsyncClient, prefix: str):
    bootstrap_email = f"bootstrap-{prefix}-{uuid.uuid4().hex[:8]}@example.test"
    password = "AdsTestPassword123!"
    registered = await client.post(
        f"{BASE_URL}/auth/register",
        json={"email": bootstrap_email, "password": password, "projectId": BOOTSTRAP_PROJECT_ID, "role": "Admin"},
    )
    registered.raise_for_status()
    login = await client.post(f"{BASE_URL}/auth/login", json={"email": bootstrap_email, "password": password})
    login.raise_for_status()
    bootstrap_headers = {"Authorization": f"Bearer {login.json()['accessToken']}"}

    project = await client.post(
        f"{BASE_URL}/projects",
        headers=bootstrap_headers,
        json={"name": f"{prefix}-{uuid.uuid4().hex[:8]}"},
    )
    project.raise_for_status()
    project_id = project.json()["id"]
    email = f"{prefix}-{uuid.uuid4().hex[:8]}@example.test"
    registered = await client.post(f"{BASE_URL}/auth/register", json={"email": email, "password": password, "projectId": project_id, "role": "Admin"})
    registered.raise_for_status()
    login = await client.post(f"{BASE_URL}/auth/login", json={"email": email, "password": password})
    login.raise_for_status()
    return project_id, {"Authorization": f"Bearer {login.json()['accessToken']}"}


async def connect_mock_facebook(client: httpx.AsyncClient, project_id: str, headers: dict):
    started = await client.post(
        f"{BASE_URL}/projects/{project_id}/ad-manager/facebook/oauth/start",
        headers={**headers, "Idempotency-Key": str(uuid.uuid4())},
    )
    assert started.status_code == 200
    resources = await client.get(f"{BASE_URL}/projects/{project_id}/ad-manager/facebook/resources", headers=headers)
    resources.raise_for_status()
    catalog = resources.json()
    selected = await client.put(
        f"{BASE_URL}/projects/{project_id}/ad-manager/connection",
        headers={**headers, "Idempotency-Key": str(uuid.uuid4())},
        json={
            "adAccountId": catalog["adAccounts"][0]["id"], "pageId": catalog["pages"][0]["id"],
            "datasetId": catalog["datasets"][0]["id"],
            "wabaId": catalog["wabas"][0]["id"],
            "phoneNumberId": catalog["wabas"][0]["phones"][0]["id"],
            "integrationMode": "CloudApiCoexistence",
        },
    )
    assert selected.is_success, f"connection selection failed ({selected.status_code}): {selected.text}"
    return catalog
