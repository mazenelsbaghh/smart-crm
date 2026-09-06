import assert from 'node:assert/strict';
import test from 'node:test';
import { createRecentOutboundMessages } from '../src/recent-outbound-messages.js';
import { resolveSessionIdentity } from '../src/session-identity.js';

test('production 2026-09-05 outbound LID echo is consumed once', () => {
    const identity = resolveSessionIdentity('project-1', 'account-1');
    const messages = createRecentOutboundMessages();

    messages.record(identity, '172322745000191@lid', 'provider-20260905-lid-echo');

    assert.equal(messages.consumeEcho(
        identity,
        '172322745000191@lid',
        'provider-20260905-lid-echo'), true);
    assert.equal(messages.consumeEcho(
        identity,
        '172322745000191@lid',
        'provider-20260905-lid-echo'), false);
});

test('expired outbound identity does not hide a later customer message', () => {
    let timestamp = 1_000;
    const identity = resolveSessionIdentity('project-1', 'account-1');
    const messages = createRecentOutboundMessages({ ttlMs: 500, now: () => timestamp });
    messages.record(identity, '201000000001@s.whatsapp.net', 'provider-expired');

    timestamp += 501;

    assert.equal(messages.consumeEcho(identity, '201000000001@s.whatsapp.net', 'provider-expired'), false);
});

test('outbound echoes stay isolated by account and recipient', () => {
    const firstAccount = resolveSessionIdentity('project-1', 'account-1');
    const secondAccount = resolveSessionIdentity('project-1', 'account-2');
    const messages = createRecentOutboundMessages();
    messages.record(firstAccount, '201000000001:7@s.whatsapp.net', 'provider-message-1');

    assert.equal(messages.consumeEcho(secondAccount, '201000000001@s.whatsapp.net', 'provider-message-1'), false);
    assert.equal(messages.consumeEcho(firstAccount, '201000000002@s.whatsapp.net', 'provider-message-1'), false);
    assert.equal(messages.consumeEcho(firstAccount, '201000000001@s.whatsapp.net', 'provider-message-1'), true);
});

// Regression: 2026-09-06 audit, a customer's "تمام" reply must not be discarded as an echo.
test('a new customer message is retained even after a recent outbound message to the same recipient', () => {
    const identity = resolveSessionIdentity('project-1', 'account-1');
    const messages = createRecentOutboundMessages();
    messages.record(identity, '201000000001@s.whatsapp.net', 'outbound-id');
    assert.equal(messages.consumeEcho(identity, '201000000001@s.whatsapp.net', 'customer-reply-id'), false);
    assert.equal(messages.consumeEcho(identity, '201000000001@s.whatsapp.net', 'outbound-id'), true);
});
