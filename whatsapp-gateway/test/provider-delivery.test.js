import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveSessionIdentity, sessionMapKey } from '../src/session-identity.js';

process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET = 'provider-delivery-test-secret';

const {
    getGroupInviteLink,
    sendMessage,
    sendReaction,
    sessions,
    statuses
} = await import('../src/baileys-manager.js');

function connectSocket(context, projectId, accountId, socket) {
    const identity = resolveSessionIdentity(projectId, accountId);
    const key = sessionMapKey(identity);
    sessions.set(key, socket);
    statuses.set(key, 'Connected');
    context.after(() => {
        sessions.delete(key);
        statuses.delete(key);
    });
}

function isAmbiguousDelivery(error) {
    assert.equal(error.code, 'WHATSAPP_DELIVERY_AMBIGUOUS');
    assert.notEqual(error.definitelyNotSent, true);
    return true;
}

test('real sends and reactions without provider IDs remain ambiguous', async context => {
    const projectId = 'provider-project';
    const accountId = 'real-account';
    connectSocket(context, projectId, accountId, {
        sendMessage: async () => ({ key: {} })
    });

    await assert.rejects(
        () => sendMessage(projectId, '201000000001', 'hello', [], accountId),
        isAmbiguousDelivery);
    await assert.rejects(
        () => sendReaction(
            projectId,
            '201000000001',
            '👍',
            'provider-target-1',
            false,
            accountId),
        isAmbiguousDelivery);
});

test('mock sends may synthesize IDs when no provider ID exists', async context => {
    const projectId = 'provider-project';
    const accountId = 'mock-account';
    connectSocket(context, projectId, accountId, {
        isMock: true,
        sendMessage: async () => ({ key: {} })
    });

    const sent = await sendMessage(projectId, '201000000001', 'hello', [], accountId);
    const reactionId = await sendReaction(
        projectId,
        '201000000001',
        '👍',
        'provider-target-1',
        false,
        accountId);

    assert.match(sent.messageId, /^msg_send_mock_/);
    assert.match(reactionId, /^msg_reaction_mock_/);
});

test('missing group invite code is retryable instead of returning an undefined URL', async context => {
    const projectId = 'provider-project';
    const accountId = 'group-account';
    connectSocket(context, projectId, accountId, {
        groupInviteCode: async () => undefined
    });

    await assert.rejects(
        () => getGroupInviteLink(projectId, 'group-1@g.us', accountId),
        error => {
            assert.equal(error.code, 'WHATSAPP_SESSION_UNAVAILABLE');
            assert.equal(error.definitelyNotSent, true);
            assert.doesNotMatch(error.message, /undefined/);
            return true;
        });
});
