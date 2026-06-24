# Quickstart: Messenger & WhatsApp Follow-Up Integration

## Verification & Testing Flows

### 1. Test Messenger AI Free Session Reminder
1. Send a text message to the Facebook Page's Messenger from a test Facebook account (not the page admin).
2. Check that the AI responder replies and explicitly mentions that the first session is free (e.g., "أول سيشن مجانية").

### 2. Test Phone Number Capture and Transition
1. In the Messenger chat, type an Egyptian phone number (e.g. `01012345678` or `+20 12 1234 5678`).
2. Verify:
   - A WhatsApp message is sent to the phone number with the personalized message: `"أهلاً يا [Name]، منورنا يا فندم! 😊 معاك [اسم البروجكت]... أول جلسة ليك معانا مجانية تماماً! لو تحب تحجزها دلوقتي، قولي الميعاد المناسب ليك وهسجلك فيه فوراً."`
   - A Messenger message is sent back to the customer: `"أنا بعتلك رسالة على الواتساب، خلينا نتواصل هناك. ✨"`
   - The Customer record in the database now has `PhoneNumber` populated with `201012345678`.
   - Any pending Messenger follow-ups in the database are cancelled.
   - A new WhatsApp follow-up is scheduled.

### 3. Test WhatsApp Sending Failure Fallback
1. Send a message on Messenger containing a number that does not exist or fails sending (e.g., triggering a simulated gateway error).
2. Verify:
   - Messenger sends the alert message: `"حاولنا نبعتلك على الواتساب بس غالباً الرقم غلط أو مش عليه واتساب. يا ريت تبعتلي الرقم الصح هنا عشان نتواصل هناك."`
   - Customer's `PhoneNumber` is not set or follow-ups remain active on Messenger.
