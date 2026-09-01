import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import { once } from 'node:events';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
    createGroupResultJournal,
    groupJournalDirectory
} from '../src/group-result-journal.js';
import { resolveSessionIdentity } from '../src/session-identity.js';

test('keeps two account sessions and mock traffic isolated through the HTTP contract', async (context) => {
    const baseUrl = await startGateway(context);

    await postJson(`${baseUrl}/api/whatsapp/session/mock`, {
        projectId: 'project-a',
        whatsappAccountId: 'account-a',
        status: 'Connected',
        phoneNumber: '201111111111'
    });
    await postJson(`${baseUrl}/api/whatsapp/session/mock`, {
        projectId: 'project-a',
        whatsappAccountId: 'account-b',
        status: 'Connected',
        phoneNumber: '202222222222'
    });

    await postJson(`${baseUrl}/api/whatsapp/send`, {
        projectId: 'project-a',
        whatsappAccountId: 'account-a',
        to: '201000000001',
        message: 'from-a'
    });
    await postJson(`${baseUrl}/api/whatsapp/send`, {
        projectId: 'project-a',
        whatsappAccountId: 'account-b',
        to: '201000000002',
        message: 'from-b'
    });

    const accountAMessages = await getJson(
        `${baseUrl}/api/whatsapp/mock/sent?projectId=project-a&whatsappAccountId=account-a`);
    const accountBMessages = await getJson(
        `${baseUrl}/api/whatsapp/mock/sent?projectId=project-a&whatsappAccountId=account-b`);
    assert.deepEqual(accountAMessages.map(message => message.message), ['from-a']);
    assert.deepEqual(accountBMessages.map(message => message.message), ['from-b']);

    await postJson(`${baseUrl}/api/whatsapp/session/disconnect`, {
        projectId: 'project-a',
        whatsappAccountId: 'account-a'
    });
    const accountAStatus = await getJson(
        `${baseUrl}/api/whatsapp/session/status?projectId=project-a&whatsappAccountId=account-a`);
    const accountBStatus = await getJson(
        `${baseUrl}/api/whatsapp/session/status?projectId=project-a&whatsappAccountId=account-b`);
    assert.equal(accountAStatus.status, 'Disconnected');
    assert.equal(accountBStatus.status, 'Connected');

    await postJson(`${baseUrl}/api/whatsapp/mock/clear`, {
        projectId: 'project-a',
        whatsappAccountId: 'account-a'
    });
    assert.deepEqual(await getJson(
        `${baseUrl}/api/whatsapp/mock/sent?projectId=project-a&whatsappAccountId=account-a`), []);
    assert.equal((await getJson(
        `${baseUrl}/api/whatsapp/mock/sent?projectId=project-a&whatsappAccountId=account-b`)).length, 1);
});

test('durable group result replays before session and Redis checks', async context => {
    const groupResult = {
        jid: 'provider-group-replay@g.us',
        inviteLink: null,
        enrichmentError: 'Invite link is pending and can be loaded separately.'
    };
    const baseUrl = await startGateway(context, async runtimeDirectory => {
        const sessionsDirectory = path.join(runtimeDirectory, 'sessions');
        const journal = createGroupResultJournal(groupJournalDirectory(sessionsDirectory));
        await journal.record(
            resolveSessionIdentity('project-a', 'account-a'),
            'group:replay-1',
            groupResult);
    });

    const replay = await postJson(`${baseUrl}/api/whatsapp/group/create`, {
        projectId: 'project-a',
        whatsappAccountId: 'account-a',
        subject: 'Support',
        participants: ['201000000001'],
        idempotencyKey: 'group:replay-1'
    });

    assert.equal(replay.jid, groupResult.jid);
    assert.equal(replay.idempotentReplay, true);
    assert.equal(replay.reconciledFromJournal, true);
});

async function startGateway(context, initializeRuntime = async () => {}) {
    const port = await availablePort();
    const runtimeDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-http-runtime-'));
    await initializeRuntime(runtimeDirectory);
    const gatewayEntryPoint = fileURLToPath(new URL('../src/index.js', import.meta.url));
    const gateway = spawn(process.execPath, [gatewayEntryPoint], {
        cwd: runtimeDirectory,
        env: {
            ...process.env,
            PORT: String(port),
            AUTO_RESTORE_SESSIONS: 'false',
            WHATSAPP_GATEWAY_WEBHOOK_SECRET: 'multi-account-http-test-secret'
        },
        stdio: ['ignore', 'ignore', 'pipe']
    });
    context.after(async () => {
        if (gateway.exitCode === null) gateway.kill('SIGTERM');
        if (gateway.exitCode === null) await once(gateway, 'exit');
        fs.rmSync(runtimeDirectory, { recursive: true, force: true });
    });
    const baseUrl = `http://127.0.0.1:${port}`;
    await waitUntilReady(`${baseUrl}/api/whatsapp/session/status?projectId=project-a`);
    return baseUrl;
}

async function availablePort() {
    const server = net.createServer();
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const address = server.address();
    const port = typeof address === 'object' && address ? address.port : 0;
    server.close();
    await once(server, 'close');
    return port;
}

async function waitUntilReady(url) {
    for (let attempt = 0; attempt < 100; attempt += 1) {
        try {
            const response = await fetch(url);
            if (response.ok) return;
        } catch {
            // The child is still binding its local test port.
        }
        await new Promise(resolve => setTimeout(resolve, 25));
    }
    throw new Error('Gateway test server did not become ready');
}

async function getJson(url) {
    const response = await fetch(url);
    const body = await response.text();
    assert.equal(response.ok, true, body);
    return JSON.parse(body);
}

async function postJson(url, body) {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(body)
    });
    const responseBody = await response.text();
    assert.equal(response.ok, true, responseBody);
    return JSON.parse(responseBody);
}
