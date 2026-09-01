import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { createInboundMessageOutbox } from '../src/inbound-message-outbox.js';

const silentLogger = { error() {} };

function inboundMessage(accountId, messageId = 'provider-message-1') {
    return {
        projectId: 'project-1',
        whatsappAccountId: accountId,
        messageId,
        sender: '201000000001',
        content: 'hello',
        messageType: 'Text',
        timestamp: 1_788_225_600,
        connectionOpenedAt: '2026-09-01T09:00:00.000Z',
        assetId: null
    };
}

function jsonFileCount(directory) {
    if (!fs.existsSync(directory)) return 0;
    return fs.readdirSync(directory, { recursive: true })
        .filter(entry => entry.endsWith('.json'))
        .length;
}

test('an inbound envelope survives restart before media enrichment completes', async context => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-inbound-stage-'));
    context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    const capturedMessage = inboundMessage('account-a');
    const firstProcess = createInboundMessageOutbox({
        directory,
        forwardMessage: async () => assert.fail('staged message must not forward yet'),
        logger: silentLogger
    });
    await assert.rejects(
        () => firstProcess.captureAndForward(capturedMessage, async () => {
            assert.equal(jsonFileCount(directory), 1);
            throw new Error('media enrichment interrupted');
        }),
        /media enrichment interrupted/);
    firstProcess.close();

    const forwarded = [];
    const restartedProcess = createInboundMessageOutbox({
        directory,
        forwardMessage: async message => forwarded.push(message),
        logger: silentLogger
    });
    context.after(() => restartedProcess.close());
    await restartedProcess.restore();

    assert.deepEqual(forwarded, [capturedMessage]);
    assert.equal(jsonFileCount(directory), 0);
});

test('failed delivery retries durably and scopes the same provider ID per account', async context => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-inbound-retry-'));
    context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    let currentTime = 1_000;
    let failedAttempts = 0;
    const firstProcess = createInboundMessageOutbox({
        directory,
        forwardMessage: async () => {
            failedAttempts += 1;
            throw new Error('backend unavailable');
        },
        retryDelaysMs: [60_000],
        now: () => currentTime,
        logger: silentLogger
    });
    const accountA = inboundMessage('account-a');
    const accountB = inboundMessage('account-b');
    for (const message of [accountA, accountB]) {
        await firstProcess.captureAndForward(message, async captured => captured);
    }
    await firstProcess.captureAndForward(accountA, async captured => captured);

    assert.equal(failedAttempts, 2);
    assert.equal(jsonFileCount(directory), 2);
    firstProcess.close();

    currentTime += 60_000;
    const forwardedAccounts = [];
    const restartedProcess = createInboundMessageOutbox({
        directory,
        forwardMessage: async message => forwardedAccounts.push(message.whatsappAccountId),
        now: () => currentTime,
        logger: silentLogger
    });
    context.after(() => restartedProcess.close());
    await restartedProcess.restore();

    assert.deepEqual(forwardedAccounts.sort(), ['account-a', 'account-b']);
    assert.equal(jsonFileCount(directory), 0);
});

test('a retained inbound message retries while the gateway remains running', async context => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-inbound-live-retry-'));
    context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    let attempts = 0;
    let reportDelivered;
    const delivered = new Promise(resolve => { reportDelivered = resolve; });
    const outbox = createInboundMessageOutbox({
        directory,
        forwardMessage: async () => {
            attempts += 1;
            if (attempts === 1) throw new Error('backend restarting');
            reportDelivered();
        },
        retryDelaysMs: [10],
        logger: silentLogger
    });
    context.after(() => outbox.close());

    const message = inboundMessage('account-a', 'provider-live-retry');
    await outbox.captureAndForward(message, async captured => captured);
    let timeout;
    try {
        await Promise.race([
            delivered,
            new Promise((_, reject) => {
                timeout = setTimeout(
                    () => reject(new Error('inbound retry did not run')),
                    1_000);
            })
        ]);
    } finally {
        clearTimeout(timeout);
    }
    await outbox.restore();

    assert.equal(attempts, 2);
    assert.equal(jsonFileCount(directory), 0);
});

test('duplicate upsert waits for media enrichment and forwards one complete envelope', async context => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-inbound-duplicate-'));
    context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    const forwarded = [];
    const outbox = createInboundMessageOutbox({
        directory,
        forwardMessage: async message => forwarded.push(message),
        logger: silentLogger
    });
    context.after(() => outbox.close());
    let releaseMedia;
    let reportMediaStarted;
    const mediaGate = new Promise(resolve => { releaseMedia = resolve; });
    const mediaStarted = new Promise(resolve => { reportMediaStarted = resolve; });
    const message = inboundMessage('account-a', 'provider-duplicate-media');

    const firstUpsert = outbox.captureAndForward(message, async captured => {
        reportMediaStarted();
        await mediaGate;
        return { ...captured, assetId: 'asset-1' };
    });
    await mediaStarted;
    const duplicateUpsert = outbox.captureAndForward(message, async () => {
        assert.fail('duplicate must not start a second media upload');
    });
    assert.equal(forwarded.length, 0);

    releaseMedia();
    await Promise.all([firstUpsert, duplicateUpsert]);

    assert.equal(forwarded.length, 1);
    assert.equal(forwarded[0].assetId, 'asset-1');
});
