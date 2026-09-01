import assert from 'node:assert/strict';
import test from 'node:test';
import { replaceWithMockSession } from '../src/mock-session.js';

test('mock replacement awaits the real socket and establishes a fresh epoch', async context => {
    const key = 'account:project-1:account-a';
    const sessions = new Map();
    const statuses = new Map([[key, 'Connected']]);
    const previousEpoch = '2026-09-01T09:00:00.000Z';
    const connectionOpenedAt = new Map([[key, previousEpoch]]);
    let releaseClose;
    let reportCloseStarted;
    const closeGate = new Promise(resolve => { releaseClose = resolve; });
    const closeStarted = new Promise(resolve => { reportCloseStarted = resolve; });
    context.after(() => releaseClose());
    const realSocket = {
        end: async () => {
            reportCloseStarted();
            await closeGate;
        }
    };
    const mockSocket = { isMock: true };
    sessions.set(key, realSocket);

    const replacing = replaceWithMockSession({
        key,
        status: 'Connected',
        mockSocket,
        sessions,
        statuses,
        connectionOpenedAt,
        now: () => Date.parse(previousEpoch)
    });
    await closeStarted;

    assert.equal(sessions.has(key), false);
    assert.equal(statuses.get(key), 'Disconnected');
    releaseClose();
    const connectedAt = await replacing;

    assert.equal(sessions.get(key), mockSocket);
    assert.equal(statuses.get(key), 'Connected');
    assert.equal(connectedAt, '2026-09-01T09:00:00.001Z');
});
