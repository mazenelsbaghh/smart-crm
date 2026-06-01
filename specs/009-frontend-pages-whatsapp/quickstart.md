# Developer Quickstart & Verification Commands

## 1. Local Development Run

To test the newly added routes in development, start the Next.js local server:

```bash
# In frontend directory
npm run dev
```

The pages will be accessible at:
- `/settings` (WhatsApp connectivity status and QR scanner)
- `/management/follow-ups` (Pending and overdue follow-up reminders list)
- `/management/campaigns` (Scheduled marketing campaigns lists)
- `/management/workflows` (Automation workflows list)
- `/management/knowledge` (Company knowledge base documents and brain sync)
- `/management/approvals` (AI proposals verification queue)
- `/management/reports` (Operations and performance dashboard)

---

## 2. Docker Production Run

To compile and execute inside the Docker stack:

```bash
# Deploy production composition
make deploy

# Check running container statuses
make ps
```

---

## 3. End-to-End Verification

Verify the pages compiled successfully and the API routing is healthy:
1. Log in at `https://localhost` using `admin@smartcore.com` / `Password123`.
2. Open Settings, click "Link WhatsApp", and verify the QR code initializes.
3. Click "Mock Connect" to simulate a successful connection, and verify the UI updates to show status "Connected".
4. Navigate through all management pages in the sidebar to ensure zero routing errors.
