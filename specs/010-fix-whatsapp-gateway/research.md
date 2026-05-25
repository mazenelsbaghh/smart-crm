# Research: WhatsApp Gateway Fixes (Baileys)

## Decision: Robust JID Formatting and Connection Validation

To ensure reliable message delivery and connection status management, we make the following decisions:

1. **JID Sanitization**:
   - Strip any non-numeric characters (like `+`, spaces, hyphens, parenthesis) from the recipient's phone number before building the JID.
   - If the incoming number already ends with `@s.whatsapp.net` or `@g.us`, preserve it as a full JID, otherwise append `@s.whatsapp.net` after sanitization.
   - This ensures WhatsApp's servers accept the recipient ID without throwing "invalid JID" protocol errors.

2. **Connection Status Handling**:
   - Implement structured error handling in `sendMessage`. If the socket is not initialized, or the status is not 'Connected', reject the request immediately with a clear error message.
   - Listen to connection states (`open`, `connecting`, `close`) and correctly map them to the `statuses` map so the API returns accurate state information.

3. **Authentication State & Credentials Persistence**:
   - Ensure the session folder is created properly.
   - Bind `creds.update` to `saveCreds` to automatically write the credentials update to disk.
   - Gracefully handle version mismatches or connection closed reasons (e.g. reconnect if not logged out).

## Rationale

- **JID Errors**: Baileys passes raw strings to the WhatsApp server. If a number contains formatting like `+20 100-...`, the protocol message is invalid and fails silently or throws.
- **Auto-Reconnect**: The gateway runs inside Docker. If the internet drops or WhatsApp forces a disconnect, the socket must attempt to reconnect automatically with backoff, which is supported by the `shouldReconnect` logic in connection close handler.

## Alternatives Considered

- **Using a different WhatsApp library (e.g., Whatsapp-web.js)**:
  - Rejected because Whatsapp-web.js requires a headless browser (Puppeteer) which has a high memory footprint and CPU overhead, unsuitable for our single Ubuntu server constraints. Baileys interacts directly with WhatsApp Web sockets, which is much faster and lightweight.
