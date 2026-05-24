# API Contracts: Frontend Dashboard, Realtime & Production Hardening

This document defines the schema of the endpoints exposed to the frontend, real-time routing structures, and proxy endpoints.

## 1. Authentication Proxy Endpoints

| Method | Endpoint | Request Body | Response Success (200 OK) |
|--------|----------|--------------|---------------------------|
| `POST` | `/api/auth/login` | `{ "email": "user@example.com", "password": "securepassword" }` | `{ "accessToken": "jwt_token", "refreshToken": "refresh_token", "user": { "id": "uuid", "role": "Agent" } }` |
| `POST` | `/api/auth/register` | `{ "email": "user@example.com", "password": "securepassword", "fullName": "John Doe" }` | `{ "id": "uuid", "email": "user@example.com" }` |
| `POST` | `/api/auth/refresh` | `{ "refreshToken": "refresh_token" }` | `{ "accessToken": "new_jwt_token", "refreshToken": "new_refresh_token" }` |

---

## 2. Real-Time Hub Routing

- **Hub URL**: `/hubs/notifications`
- **Transport Mode**: WebSockets preferred; Server-Sent Events / Long Polling fallback.
- **Protocol**: JSON.
- **Headers Required**:
  - `Authorization: Bearer <accessToken>` (passed as query string parameter `access_token` in WebSockets fallback).

---

## 3. Production Hardening Rules

### CORS Rules
Nginx CORS directives enforce:
- `Access-Control-Allow-Origin`: Explicitly matched whitelist (e.g. `https://smartwhatsapp.domain.com` or `http://localhost:3000` in dev).
- `Access-Control-Allow-Methods`: `GET, POST, PUT, DELETE, OPTIONS`.
- `Access-Control-Allow-Headers`: `Content-Type, Authorization, X-Project-Id`.

### Rate Limiting Limits
- **Zone IP limit**: Limit rate to `100 req/m` per individual client IP.
- **Burst Buffer**: Support `burst=20` requests with `nodelay` to allow dynamic page loading behavior.
