# Technical Research: Messenger & WhatsApp Follow-Up Integration

## Decisions & Rationale

### 1. Phone Number Extraction Strategy
- **Decision**: Strip all whitespace, dashes, parenthetical formatting, and leading plus/double-zero prefix from the incoming text, normalize Eastern Arabic digits to Western digits, and apply a regular expression targeting Egyptian mobile prefixes (`10`, `11`, `12`, `15`) followed by exactly 8 digits.
- **Rationale**: User typing habits vary widely. Formatted inputs like `+20 101-234-5678` or `٠١٠١٢٣٤٥٦٧٨` are normal. Stripping noise characters first and normalizing digits ensures highly accurate matching without complex LLM parses.
- **Alternative Considered**: Sending the text to Gemini to extract the phone number in JSON. Rejected because it incurs API cost, adds ~2 seconds of latency, and has a failure rate.

### 2. Transition Flow Execution
- **Decision**: Trigger the transition immediately in the `AIReplyWorker.cs` handler thread. If a phone number is detected in Messenger:
  1. Attempt to send a WhatsApp message to the E.164 number.
  2. If the HTTP request to the WhatsApp Gateway succeeds:
     - Save the phone number to the customer profile.
     - Send a Messenger confirmation: `"أنا بعتلك رسالة على الواتساب، خلينا نتواصل هناك. ✨"`.
     - Stop all pending follow-ups for the customer and mark them "Cancelled".
     - Schedule a new WhatsApp follow-up.
  3. If the HTTP request fails (e.g. gateway error, number not on WhatsApp):
     - Keep the customer on the Messenger channel.
     - Send a Messenger alert: `"حاولنا نبعتلك على الواتساب بس غالباً الرقم غلط أو مش عليه واتساب. يا ريت تبعتلي الرقم الصح هنا عشان نتواصل هناك."`.
- **Rationale**: Guarantees that the customer only transitions to WhatsApp if the channel is actually functional and reachable, avoiding dead transitions.

### 3. Messenger Follow-Up Dispatching
- **Decision**: Extend the minutely recurring Hangfire job in `FollowUpScheduler.cs`. If `string.IsNullOrEmpty(customer.PhoneNumber)` and `!string.IsNullOrEmpty(customer.FacebookPSID)`, route the follow-up text to the Facebook page's Graph API instead of the WhatsApp Gateway.
- **Rationale**: Reuses the existing follow-up scheduler and databases, keeping follow-ups unified and preventing duplicate engines.
