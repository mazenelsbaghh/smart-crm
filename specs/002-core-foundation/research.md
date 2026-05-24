# Research and Decision Records: Core Foundation

## 1. Password Hashing Algorithm

- **Decision**: Use `BCrypt.Net-Next` for password hashing and verification in the ASP.NET Core Auth module.
- **Rationale**: BCrypt is a secure, industry-standard, cpu-bound hashing algorithm that includes salt generation automatically. It is highly resistant to brute-force attacks compared to MD5 or SHA256 alone.
- **Alternatives considered**: 
  - PBKDF2: Supported natively by ASP.NET Core Identity. Rejected to avoid pulling in the heavy default ASP.NET Core Identity package, keeping our Auth module lightweight and customized to our modular architecture.
  - Argon2id: Extremely secure but requires external native libraries that increase deployment complexity on Docker. BCrypt provides a perfect balance of security and ease of deployment.

## 2. JWT Generation & Validation

- **Decision**: Use `System.IdentityModel.Tokens.Jwt` with asymmetric HS256 symmetric signing keys defined in environment variables. Access tokens will have a 15-minute expiration, and Refresh tokens will be stored in PostgreSQL with a 7-day expiration.
- **Rationale**: Provides secure stateless authentication for APIs, while using stored refresh tokens allows session revoking (logout) and prevents forcing users to login frequently.
- **Alternatives considered**:
  - ASP.NET Cookie Auth: Not suitable for cross-origin decoupled services (Next.js frontend + backend API). JWT is standard for modern RESTful modular architectures.

## 3. WhatsApp Gateway Library (Baileys vs. WPPConnect vs. Puppeteer-based)

- **Decision**: Use `@whiskeysockets/baileys` (Node.js library that connects directly to the WhatsApp Web WebSocket API).
- **Rationale**: Baileys does not run a headless browser (like Puppeteer or Playwright), resulting in a significantly lower memory footprint (under 100MB per session vs. 500MB+ for browser-based tools) and much faster response times. It supports multi-device logins and is actively maintained.
- **Alternatives considered**:
  - Puppeteer-based wrappers (e.g., WhatsApp-web.js): Heavy resource usage, prone to memory leaks, and highly sensitive to Chrome update breakages.
  - Official WhatsApp Cloud API: Rejected because of session-opening costs, strict template rules for outgoing messages, and high setup barriers for small/medium business projects.

## 4. Message Aggregation Window Logic

- **Decision**: Implement Redis key-value storage with an expiry listener or custom timestamp checking.
  - When a message is received from a customer (e.g., `sender_phone_number`):
    - Increment a Redis counter or append the message to a Redis list under `chat:aggregation:{projectId}:{sender}`.
    - Set/Reset key TTL to 5 seconds.
    - When the key expires, or when the worker checks and finds the key hasn't been modified for 5 seconds, it retrieves all aggregated messages, publishes a `MessageAggregated` event to RabbitMQ, and cleans up the Redis list.
- **Rationale**: Redis provides extremely fast read/write speeds, high concurrency support, and native TTL capabilities, making it ideal for temporary buffers and aggregation windows.
- **Alternatives considered**:
  - PostgreSQL buffering: High database write/read load and complex cleanup jobs. Redis is far more efficient for transient state.
