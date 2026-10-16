import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { createInboundMessageOutbox } from '../src/inbound-message-outbox.js';

const silentLogger = { error() {} };

test('channel updates never start customer delivery or media enrichment', async context => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-inbound-channels-'));
    context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    const outbox = createInboundMessageOutbox({
        directory,
        forwardMessage: async () => assert.fail('a non-customer chat must not reach the backend'),
        logger: silentLogger
    });
    context.after(() => outbox.close());

    for (const sender of ['123456789@newsletter', 'status@broadcast', '123456789@g.us']) {
        await outbox.captureAndForward({ ...inboundMessage('account-a'), sender },
            async () => assert.fail('a non-customer chat must not upload media'));
    }
    assert.equal(jsonFileCount(directory), 0);
});

test('legacy channel retry backlog is cleared while phone and LID messages still deliver', async context => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-inbound-legacy-channels-'));
    context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    const forwarded = [];
    const senders = ['123456789@newsletter', 'status@broadcast', '123456789@g.us', '201000000001', '123456789@lid'];
    for (const [index, sender] of senders.entries()) {
        fs.writeFileSync(path.join(directory, `${index}.json`), JSON.stringify({
            version: 1,
            message: { ...inboundMessage('account-a', `provider-${index}`), sender, senderJid: sender },
            attempts: 3790,
            nextAttemptAt: index < 3 ? Date.now() + 300_000 : null
        }));
    }
    const outbox = createInboundMessageOutbox({
        directory,
        forwardMessage: async message => forwarded.push(message.sender),
        logger: silentLogger
    });
    context.after(() => outbox.close());
    await outbox.restore();

    assert.deepEqual(forwarded.sort(), ['123456789@lid', '201000000001']);
    assert.equal(jsonFileCount(directory), 0);
});

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

test('a retained Baileys long timestamp is normalized before backend delivery', async context => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-inbound-timestamp-'));
    context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
    const forwarded = [];
    const outbox = createInboundMessageOutbox({
        directory,
        forwardMessage: async message => forwarded.push(message),
        logger: silentLogger
    });
    context.after(() => outbox.close());
    const message = {
        ...inboundMessage('account-a', 'provider-long-timestamp'),
        timestamp: { low: 1_788_225_600, high: 0, unsigned: true }
    };

    await outbox.captureAndForward(message, async captured => captured);

    assert.equal(forwarded[0].timestamp, 1_788_225_600);
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
