import pytest
import httpx
import uuid
import json

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_advanced_report_generation():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # Create Project
        proj_name = f"Reports_Project_{uuid.uuid4().hex[:6]}"
        create_proj = await client.post(f"{BASE_URL}/projects", json={"name": proj_name})
        assert create_proj.status_code == 201
        proj_id = create_proj.json()["id"]

        # Register User
        user_email = f"reports_user_{uuid.uuid4().hex[:6]}@smartcore.com"
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

        # 1. Recalculate to generate initial snapshots
        await client.post(f"{BASE_URL}/projects/{proj_id}/analytics/recalculate", headers=headers)

        # 2. Call generate report API
        report_payload = {
            "reportType": "ExecutiveInsights",
            "startDate": "2026-05-01T00:00:00Z",
            "endDate": "2026-05-31T00:00:00Z"
        }
        report_resp = await client.post(
            f"{BASE_URL}/projects/{proj_id}/reports/generate",
            headers=headers,
            json=report_payload
        )
        assert report_resp.status_code == 200
        report = report_resp.json()
        assert report["reportType"] == "ExecutiveInsights"
        assert "generatedAt" in report
        assert "summaryMetrics" in report
