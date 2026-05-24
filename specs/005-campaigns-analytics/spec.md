# Feature Specification: Campaigns, Advanced Analytics & Reporting

**Feature Branch**: `phase/4-campaigns-analytics`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "Phase 4: Campaigns, Advanced Analytics & Reporting"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Multi-Channel Campaign Launch & Anti-Ban Delivery (Priority: P1)

As a Marketing Manager, I want to create a dynamically segmented target audience and launch an automated WhatsApp message campaign, using AI-generated personalized copies and a safe anti-ban delay mechanism, so that I can reach out to multiple prospects without getting the WhatsApp number banned.

**Why this priority**: Campaigns are the primary revenue driver for marketing automation and represent the MVP focus of Phase 4.

**Independent Test**: The user builds a segment of "Hot Leads" in a specific city, creates a campaign, requests AI copies for a product launch, schedules it for immediate delivery, and verifies that the messages are sent to the target contacts with staggered delays (e.g. 5-15 seconds) and that delivery status updates from "Pending" to "Sent" to "Delivered".

**Acceptance Scenarios**:

1. **Given** a set of 10 customers segmented by target criteria, **When** the manager launches the campaign, **Then** the campaign state transitions to "Running" and messages are sent sequentially with random intervals.
2. **Given** a running campaign, **When** the WhatsApp gateway becomes disconnected, **Then** the campaign automatically pauses, logs the current progress, and alerts the administrator.
3. **Given** a recipient replies to a campaign message, **When** the message is received, **Then** the campaign tracks this response and tags the customer as "Campaign Respondent".

---

### User Story 2 - Advanced Analytics Dashboard & Automated Reporting (Priority: P2)

As a Business Director, I want to view analytical metrics about sales pipelines, lead conversions, AI accuracy, and agent performance, and receive daily/weekly automated operational reports, so that I can optimize our customer service and marketing budget.

**Why this priority**: High value for management visibility, helping optimize the team's operations.

**Independent Test**: The user visits the analytics workspace, views acquisition and conversion funnels, triggers an on-demand "AI Performance Report", and checks that it generates a comprehensive summary including cost metrics, handoff rate, and average resolution time.

**Acceptance Scenarios**:

1. **Given** past conversation history, **When** the analytics engine runs its daily job, **Then** it aggregates metrics (e.g., average response time, AI accuracy) and stores a snapshot for historical trends.
2. **Given** a scheduled reporting configuration, **When** the weekly reporting time is reached, **Then** the system generates an executive summary report and distributes it to the designated supervisor emails.

---

### User Story 3 - Instant Unified Search (Priority: P3)

As a Customer Support Agent, I want to search across all historical conversations, customer names, notes, and individual messages using full-text search, so that I can instantly recall the context of previous interactions.

**Why this priority**: Improves agent productivity when dealing with large volumes of customer history.

**Independent Test**: The user inputs a keyword (e.g. a specific product name or customer city) into the search bar, selects "Conversations" or "Customers", and immediately sees search results highlighted by relevance.

**Acceptance Scenarios**:

1. **Given** a new message is received in a conversation, **When** it is saved, **Then** the indexing background job immediately pushes it to the search index so it is searchable within seconds.
2. **Given** an invalid query or search error, **When** a search is performed, **Then** the system handles it gracefully and returns an empty result set with a user-friendly error message.

---

### Edge Cases

- **Rate-Limiting and Temporary Ban Warnings**: If the WhatsApp network indicates rate limits or temporary blockages, the campaign engine must instantly pause itself and notify the project owner.
- **Concurrent Editing/Modification of Campaigns**: If a campaign is scheduled and the target audience changes just before execution, the campaign builder must rebuild the list before sending the first message.
- **Elasticsearch Synchronization Lag**: If the search cluster is temporarily offline, database records must be marked as "Unindexed" and a scheduled synchronization worker must re-index them once connection is restored.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow building target segments based on dynamic queries (e.g. Lead Score, tags, location, activity).
- **FR-002**: The system MUST provide an AI text generation interface to draft campaign messages with personalized placeholder variables (e.g. `{{CustomerName}}`).
- **FR-003**: The system MUST run campaigns using an anti-ban throttle controller that spaces messages with a randomized interval (e.g., between 5 and 15 seconds) to mimic human behavior.
- **FR-004**: The system MUST track delivery statuses (Pending, Sent, Delivered, Read, Failed) and track direct response rates of campaign recipients.
- **FR-005**: The system MUST automatically calculate advanced analytical metrics including: acquisition rates, sales funnel conversion, complaint volume, average team response times, and AI accuracy/handoff rates.
- **FR-006**: The system MUST support dynamic lead pipeline stages (New, Contacted, Qualified, Proposal, Negotiation, Won, Lost) and allow moving customers between stages.
- **FR-007**: The system MUST synchronize all conversations, messages, notes, and customer records with a high-performance search index for instantaneous full-text searches.

### Key Entities

- **Campaign**: Represents a broadcast message action targeted at a specific customer Segment. Tracks status (Draft, Scheduled, Running, Paused, Completed), schedule details, message templates (including variants for A/B testing), and overall metrics (sent count, delivery count, response count).
- **Segment**: Represents a dynamic subset of customers defined by set filters (tags, Lead Score ranges, location, etc.).
- **AnalyticsSnapshot**: Holds pre-calculated daily/weekly metric aggregates for fast analytics querying and reporting.
- **Report**: Represents generated document outputs summarizing operations (AI performance, campaigns, sales pipeline status).
- **PipelineStage / Deal**: Represents sales opportunities and stages in the lead conversion pipeline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Target campaign delivery handles batches of up to 10,000 customers without dropping messages or exceeding maximum queue sizes.
- **SC-002**: Message delays adjust dynamically to keep sending rate below 250 messages per hour per WhatsApp line, preventing account blocking.
- **SC-003**: Full-text queries across 1 million messages return matching results in less than 500 milliseconds.
- **SC-004**: Analytics dashboards load within 1.5 seconds, utilizing pre-aggregated metrics.

## Assumptions

- **A-001**: The underlying WhatsApp Gateway (Baileys Node.js) has stable connectivity to WhatsApp servers and propagates delivery receipts.
- **A-002**: Gemini 3.5 Flash is used as the primary engine for generating personalized campaign variants and classifying customer intents.
- **A-003**: Dynamic CRM fields (tags, lead scores) are automatically kept up-to-date by the Phase 2/3 CRM workers.
