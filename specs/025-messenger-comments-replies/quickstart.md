# Quickstart: Messenger & Comments Integration

**Feature**: 025-messenger-comments-replies  
**Date**: 2026-06-21

## Prerequisites

1. **Facebook Developer Account** with an App created at [developers.facebook.com](https://developers.facebook.com)
2. **Facebook Page** (not personal account) connected to the App
3. **Page Access Token** (long-lived) with permissions: `pages_messaging`, `pages_manage_engagement`, `pages_read_engagement`, `pages_manage_metadata`, `pages_show_list`
4. **App Secret** from the Facebook App Dashboard
5. Running backend with Docker (PostgreSQL, RabbitMQ, Redis)

## Setup Steps

### 1. Database Migration
```bash
# The migration adds Channel to Conversations, FacebookPSID to Customers,
# ConnectedPages table, and new settings fields
cd backend
dotnet ef migrations add AddFacebookChannelSupport
dotnet ef database update
```

### 2. Environment Variables
Add to your `.env` or Docker Compose:
```env
FACEBOOK_APP_ID=your_app_id
FACEBOOK_APP_SECRET=your_app_secret
FACEBOOK_VERIFY_TOKEN=your_custom_verify_token
FACEBOOK_GRAPH_API_VERSION=v20.0
FACEBOOK_OAUTH_REDIRECT_URI=https://your-domain.com/api/facebook/oauth/callback
```

### 3. Connect a Facebook Page (via OAuth — Easy!)
1. Go to **Settings** page in the CRM
2. Click **"ربط صفحة فيسبوك"** button
3. A Facebook Login popup opens → log in with your Facebook account
4. Grant the requested permissions (messaging, comments, page management)
5. Select which Page to connect from the list of your Pages
6. Done! ✅ The system automatically:
   - Obtains the Page Access Token
   - Subscribes to webhooks (messages + feed)
   - Starts receiving Messenger DMs and comments

### 4. Facebook Webhook Configuration
1. Go to your Facebook App Dashboard → Webhooks
2. Subscribe to `Page` object
3. Set Callback URL: `https://your-domain.com/api/webhooks/facebook`
4. Set Verify Token: (same as above)
5. Subscribe to fields: `messages`, `feed`

### 5. Subscribe Page to App
```bash
curl -i -X POST "https://graph.facebook.com/v20.0/{page-id}/subscribed_apps?subscribed_fields=feed,messages&access_token={page-access-token}"
```

## Testing

### Test Messenger DM
1. Open the Facebook Page on Messenger
2. Send a message from a test user account
3. Verify the message appears in `/inbox/messenger`
4. Reply from the CRM and verify it's delivered on Messenger

### Test Comment Reply
1. Create a post on the connected Page
2. Comment on the post from a test user account
3. Verify the comment appears in `/inbox/comments`
4. Reply from the CRM — verify public comment, private DM, and reaction are all applied

### Test AI Auto-Reply
1. Enable Messenger AI Auto-Reply in Settings
2. Send a DM to the Page
3. Verify the AI auto-response is generated and sent after the configured delay
