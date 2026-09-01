# Test-quality review

Reviewed on 2026-08-19 (Africa/Cairo) using the project conventions plus the Test Guard rules for pytest, Vitest/Playwright, and LLM applications.

## Review outcome

- Tests assert observable states, decisions, provider payloads, persisted outcomes, visible UI states, or HTTP contracts. No test asserts private helper calls or exact LLM prompt wording.
- Meta, Gemini, browser networking, clocks, and provider failures are isolated only at system boundaries. Domain entities and state records are real instances rather than mocks.
- The frontend API mock is accepted as the network boundary; its abort assertion proves the user-visible project-switch race is cancelled rather than asserting an internal React implementation detail.
- LLM tests cover state transitions through Strategist, Auditor, Judge, WAIT, and rejection states. They do not pin model prose or call counts.
- Migration behavior is assigned to the PostgreSQL integration project, not an in-memory ORM substitute. Verification used real PostgreSQL 17 with pgvector and passed all three integration tests.
- No redundant snapshot tests, framework-guarantee tests, or copy/paste parameter variants were found in the feature suites.

## Fix added during review

`DecisionPipelineTests.Manual_unowned_ad_cannot_enter_the_activation_review_pipeline` protects the ownership boundary that prevents Autopilot from reviewing or activating an operator-owned advertisement. It observes commands, decisions, and AI work rather than internal calls.

The HTTP acceptance assertions for missing `Idempotency-Key` and `If-Match` no longer accept status 500 as an alternative. They require the documented 400 and 428 responses so an unhandled server error cannot masquerade as a passing contract test.

The PostgreSQL classes now share a non-parallel collection fixture. This fixed the observed race where two test classes could migrate the same external test database concurrently and collide in `__EFMigrationsHistory`.

The final HTTP run exposed a concrete integration bug: the outbox dispatcher inferred the abstract `IntegrationEvent` generic type, so an event could be marked published without reaching its concrete subscription. The dispatcher now publishes by runtime contract type, and `Deserialized_outbox_event_is_published_as_its_runtime_contract_type` prevents recurrence.

Mock provider identities are derived from the project OAuth token instead of sharing one global phone/Page identity. The existing-campaign acceptance scenario now creates approved knowledge, waits for the real outbox cadence, authorizes an eligible offer/destination/envelope, and activates that authority before importing; it no longer bypasses the product's safety model. Advertising enum-string parsing is scoped to its request enums rather than changing every API enum response.

The historical message-aggregation regression now asserts the observable CRM classification produced by the complete aggregate, independent of whether the configured event transport is RabbitMQ or the development in-memory bus. Multimodal tests use bounded polling for the asynchronous result, verify persisted transcription and CRM facts, and no longer mistake a generated reply for successful WhatsApp delivery while the gateway is disconnected. The obsolete image-to-Budget expectation was removed because production deliberately disables that CRM field.

## Result

The local feature tests meet the review rules. Focused HTTP acceptance passed 22/22 and the complete historical Phase 1/3/4/5 regression suite passed 68/68 using real PostgreSQL, Redis and MinIO plus the documented development-only in-memory event transport and mock external providers.
