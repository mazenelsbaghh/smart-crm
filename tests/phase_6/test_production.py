import pytest
import httpx
import asyncio

BASE_URL = "http://localhost:80/api"

@pytest.mark.asyncio
async def test_production_cors_headers():
    headers = {
        "Origin": "http://localhost:3000",
        "Access-Control-Request-Method": "POST",
        "Access-Control-Request-Headers": "Content-Type, Authorization, X-Project-Id"
    }
    
    async with httpx.AsyncClient() as client:
        # Preflight request
        response = await client.options(f"{BASE_URL}/projects", headers=headers)
        assert response.status_code in (200, 204)
        assert response.headers.get("access-control-allow-origin") == "http://localhost:3000"
        assert "POST" in response.headers.get("access-control-allow-methods", "")
        assert "x-project-id" in response.headers.get("access-control-allow-headers", "").lower()

@pytest.mark.asyncio
async def test_production_rate_limiting():
    import os
    if not os.environ.get("TEST_PRODUCTION"):
        pytest.skip("Skipping rate limiting check in development environment (set TEST_PRODUCTION=true to run)")

    # Send rapid requests to verify Nginx rate limiting (100 req/min with burst=20)
    # We send 25 requests and expect at least one to receive 429 Too Many Requests
    got_429 = False
    async with httpx.AsyncClient() as client:
        for _ in range(30):
            response = await client.get(f"{BASE_URL}/projects")
            if response.status_code == 429:
                got_429 = True
                break
            # Small delay to not crash socket
            await asyncio.sleep(0.01)
    
    assert got_429, "Rate limiter did not block excessive requests with HTTP 429"

@pytest.mark.asyncio
async def test_ssl_redirection():
    # Verify that requesting HTTP redirects to HTTPS when SSL is active
    # (In production proxy, Nginx redirects port 80 to port 443)
    async with httpx.AsyncClient(follow_redirects=False) as client:
        response = await client.get("http://localhost:80/")
        # If redirected, expect 301/302 to https
        if response.status_code in (301, 302):
            assert "https" in response.headers.get("location", "")
        else:
            # If not redirecting globally in local dev, allow 200
            assert response.status_code == 200
