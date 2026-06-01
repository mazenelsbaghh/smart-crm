# Research: Frontend Pages & QR Connectivity

## 1. WhatsApp QR Code Rendering

- **Decision**: Render Baileys raw connection strings using the public Google Charts API or QRServer API (`https://api.qrserver.com/v1/create-qr-code/`).
- **Rationale**: The backend `/api/whatsapp/session/qr` returns the raw text representation of the WhatsApp QR code. To display it to the user without installing heavy npm dependencies (like `qrcode.react` or canvas libraries), we can pass this string directly to a public QR code generation API as an image source.
- **Alternatives considered**: Client-side canvas generation. Rejected due to bundle size overhead and unnecessary compilation dependencies.

## 2. Session Status Polling

- **Decision**: Use a standard `setInterval` polling mechanism (every 5 seconds) to check status `/api/whatsapp/session/status?projectId=...` when the connection is not active or initializing.
- **Rationale**: While WebSockets/SignalR could push state, connection state switches (like unlinking or starting a new session) are infrequent setting adjustments. Polling is highly robust, recovers automatically from temporary network dropouts, and avoids complex socket state synchronization for static settings layouts.
- **Alternatives considered**: SignalR notifications for settings connection updates. Rejected to avoid socket connection lifecycle overhead on settings pages.

## 3. UI/UX Aesthetics

- **Decision**: Keep styling highly encapsulated using Vanilla CSS Modules (`.module.css`) to enforce zero-leak styles. Enforce absolute compliance with visual hierarchy, using smooth transitions (`transition: all 0.2s ease`), glassmorphic panels (`backdrop-filter: blur(10px)`), and curated HSL theme tokens (e.g., `hsl(var(--accent-secondary))` for WhatsApp brand indicators).
