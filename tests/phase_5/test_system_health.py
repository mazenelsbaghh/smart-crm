import pytest
import httpx

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_system_health_endpoint():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # 1. Health endpoint
        health_resp = await client.get(f"{BASE_URL}/system/health")
        assert health_resp.status_code in (200, 503)
        health_data = health_resp.json()
        assert "status" in health_data
        assert "components" in health_data
        
        # Verify component list has standard systems
        components = health_data["components"]
        assert "PostgreSQL" in components
        assert "Redis" in components
        assert "RabbitMQ" in components

@pytest.mark.asyncio
async def test_system_metrics_endpoint():
    async with httpx.AsyncClient(timeout=15.0) as client:
        # 2. Metrics endpoint
        metrics_resp = await client.get(f"{BASE_URL}/system/metrics")
        assert metrics_resp.status_code == 200
        metrics_data = metrics_resp.json()
        assert "rabbitMQ" in metrics_data
        assert "redis" in metrics_data
        assert "postgreSQL" in metrics_data
        assert "gemini" in metrics_data
        assert "timestamp" in metrics_data
