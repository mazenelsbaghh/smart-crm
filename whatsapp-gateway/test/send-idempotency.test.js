import assert from 'node:assert/strict';
import test from 'node:test';
import { executeIdempotentSessionCommand } from '../src/idempotent-session-command.js';
import { createSendIdempotency } from '../src/send-idempotency.js';

function fakeRedis() {
    const values = new Map();
    return {
        on() {},
        async connect() {},
        async get(key) { return values.get(key) ?? null; },
        async del(key) { return values.delete(key) ? 1 : 0; },
        async set(key, value, options = {}) {
            if (options.NX && values.has(key)) return null;
            values.set(key, value);
            return 'OK';
        },
    };
}

test('replays the provider result without claiming or sending the same command twice', async () => {
    const redis = fakeRedis();
    const idempotency = createSendIdempotency({
        redisUrl: 'redis://synthetic',
        createRedisClient: () => redis,
        ttlSeconds: 60,
    });

    const first = await idempotency.claim('project-1', 'followup-1');
    assert.equal(first.claimed, true);
    await idempotency.complete(first.key, { messageId: 'provider-message-1', status: 'Sent' });

    const replay = await idempotency.claim('project-1', 'project-1', 'followup-1');
    assert.equal(replay.claimed, false);
    assert.deepEqual(replay.result, { messageId: 'provider-message-1', status: 'Sent' });
});

test('reports an in-flight duplicate without granting a second claim', async () => {
    const redis = fakeRedis();
    const idempotency = createSendIdempotency({
        redisUrl: 'redis://synthetic',
        createRedisClient: () => redis,
    });

    assert.equal((await idempotency.claim('project-1', 'followup-2')).claimed, true);
    const duplicate = await idempotency.claim('project-1', 'followup-2');
    assert.equal(duplicate.claimed, false);
    assert.equal(duplicate.result, undefined);
});

test('scopes the same idempotency key independently for each WhatsApp account', async () => {
    const redis = fakeRedis();
    const idempotency = createSendIdempotency({
        redisUrl: 'redis://synthetic',
        createRedisClient: () => redis,
    });

    const firstAccount = await idempotency.claim('project-1', 'account-a', 'shared-command');
    const secondAccount = await idempotency.claim('project-1', 'account-b', 'shared-command');
    const firstAccountDuplicate = await idempotency.claim('project-1', 'account-a', 'shared-command');

    assert.equal(firstAccount.claimed, true);
    assert.equal(secondAccount.claimed, true);
    assert.equal(firstAccountDuplicate.claimed, false);
});

test('fails closed when durable idempotency storage is not configured', async () => {
    const idempotency = createSendIdempotency({ redisUrl: '' });
    await assert.rejects(
        () => idempotency.claim('project-1', 'followup-3'),
        /REDIS_URL is required/,
    );
});

test('can release a claim that is proven stale before provider delivery', async () => {
    const redis = fakeRedis();
    const idempotency = createSendIdempotency({
        redisUrl: 'redis://synthetic',
        createRedisClient: () => redis,
    });

    const first = await idempotency.claim('project-1', 'followup-stale');
    await idempotency.release(first.key);

    assert.equal((await idempotency.claim('project-1', 'followup-stale')).claimed, true);
});

test('completed send and reaction commands replay after reconnect without provider redispatch', async context => {
    const scenarios = [
        ['send', { status: 'Sent', messageId: 'provider-send-1' }],
        ['reaction', { status: 'Reacted', messageId: 'provider-reaction-1' }]
    ];

    for (const [commandName, providerResponse] of scenarios) {
        await context.test(commandName, async () => {
            const redis = fakeRedis();
            const idempotency = createSendIdempotency({
                redisUrl: 'redis://synthetic',
                createRedisClient: () => redis
            });
            const identity = { projectId: 'project-1', whatsappAccountId: 'account-1' };
            let currentEpoch = 'epoch-1';
            let providerCalls = 0;
            const execute = () => executeIdempotentSessionCommand({
                idempotencyStore: idempotency,
                identity,
                idempotencyKey: `${commandName}-reconnect-1`,
                validateConnection: () => currentEpoch === 'epoch-1' ? 'current' : 'stale',
                dispatch: async () => {
                    providerCalls += 1;
                    return providerResponse;
                }
            });

            const firstAttempt = await execute();
            currentEpoch = 'epoch-2';
            const replay = await execute();

            assert.equal(firstAttempt.state, 'completed');
            assert.equal(replay.state, 'replayed');
            assert.deepEqual(replay.providerResponse, providerResponse);
            assert.equal(providerCalls, 1);
        });
    }
});

test('invalid connection epoch is rejected before claiming idempotency', async () => {
    let claimCalls = 0;
    const command = await executeIdempotentSessionCommand({
        idempotencyStore: {
            async claim() { claimCalls += 1; },
            async complete() {},
            async release() {}
        },
        identity: { projectId: 'project-1', whatsappAccountId: 'account-1' },
        idempotencyKey: 'invalid-epoch-1',
        validateConnection: () => 'invalid',
        dispatch: async () => assert.fail('provider dispatch must not run')
    });

    assert.equal(command.state, 'invalid-epoch');
    assert.equal(claimCalls, 0);
});

test('a newly claimed stale command is released before returning', async () => {
    const redis = fakeRedis();
    const idempotency = createSendIdempotency({
        redisUrl: 'redis://synthetic',
        createRedisClient: () => redis
    });
    const request = {
        idempotencyStore: idempotency,
        identity: { projectId: 'project-1', whatsappAccountId: 'account-1' },
        idempotencyKey: 'stale-after-claim-1',
        validateConnection: () => 'stale',
        dispatch: async () => assert.fail('provider dispatch must not run')
    };

    assert.equal((await executeIdempotentSessionCommand(request)).state, 'stale-epoch');
    assert.equal((await executeIdempotentSessionCommand(request)).state, 'stale-epoch');
});
