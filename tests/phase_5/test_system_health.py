import pytest
import httpx
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
async def test_system_health_endpoint():
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
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
    async with httpx.AsyncClient(timeout=15.0, **get_client_kwargs()) as client:
        # 2. Metrics endpoint
        metrics_resp = await client.get(f"{BASE_URL}/system/metrics")
        assert metrics_resp.status_code == 200
        metrics_data = metrics_resp.json()
        assert "rabbitMQ" in metrics_data
        assert "redis" in metrics_data
        assert "postgreSQL" in metrics_data
        assert "gemini" in metrics_data
        assert "timestamp" in metrics_data
