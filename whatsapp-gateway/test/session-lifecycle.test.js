import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
    isSessionDisabled,
    restorableSessionIdentities
} from '../src/session-lifecycle.js';
import {
    resolveSessionIdentity,
    sessionAuthDirectory,
    sessionMapKey
} from '../src/session-identity.js';

const originalDirectory = process.cwd();
const isolatedDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-disconnect-'));
process.chdir(isolatedDirectory);
process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET = 'session-lifecycle-test-secret';

const {
    disconnectSession,
    sessions,
    startSession,
    statuses
} = await import('../src/baileys-manager.js');

function createCredentials(sessionsDirectory, identity) {
    const credentialsDirectory = sessionAuthDirectory(sessionsDirectory, identity);
    fs.mkdirSync(credentialsDirectory, { recursive: true });
    fs.writeFileSync(path.join(credentialsDirectory, 'creds.json'), '{}');
    return credentialsDirectory;
}

test('disconnect tombstone blocks exact-account restore until close and cleanup finish', async context => {
    context.after(() => {
        process.chdir(originalDirectory);
        fs.rmSync(isolatedDirectory, { recursive: true, force: true });
    });
    const sessionsDirectory = path.join(isolatedDirectory, 'sessions');
    const accountA = resolveSessionIdentity('project-1', 'account-a');
    const accountB = resolveSessionIdentity('project-1', 'account-b');
    const accountACredentials = createCredentials(sessionsDirectory, accountA);
    const accountBCredentials = createCredentials(sessionsDirectory, accountB);
    const accountAKey = sessionMapKey(accountA);
    let releaseClose;
    let reportCloseStarted;
    let closeFinished = false;
    const closeGate = new Promise(resolve => { releaseClose = resolve; });
    const closeStarted = new Promise(resolve => { reportCloseStarted = resolve; });
    context.after(() => releaseClose());
    sessions.set(accountAKey, {
        end: async () => {
            reportCloseStarted();
            await closeGate;
            closeFinished = true;
        }
    });
    statuses.set(accountAKey, 'Connected');
    context.after(() => {
        sessions.delete(accountAKey);
        statuses.delete(accountAKey);
    });

    const disconnecting = disconnectSession(accountA.projectId, accountA.whatsappAccountId);
    await closeStarted;

    assert.equal(isSessionDisabled(sessionsDirectory, accountA), true);
    assert.equal(fs.existsSync(accountACredentials), true);
    assert.equal(fs.existsSync(accountBCredentials), true);
    assert.deepEqual(
        restorableSessionIdentities(sessionsDirectory).map(identity => identity.whatsappAccountId),
        ['account-b']);

    releaseClose();
    await disconnecting;

    assert.equal(closeFinished, true);
    assert.equal(fs.existsSync(accountACredentials), false);
    assert.equal(fs.existsSync(accountBCredentials), true);
    assert.equal(isSessionDisabled(sessionsDirectory, accountA), true);

    createCredentials(sessionsDirectory, accountA);
    sessions.set(accountAKey, { isMock: true });
    statuses.set(accountAKey, 'Connected');
    await startSession(accountA.projectId, accountA.whatsappAccountId);
    assert.equal(isSessionDisabled(sessionsDirectory, accountA), false);
    assert.deepEqual(
        restorableSessionIdentities(sessionsDirectory)
            .map(identity => identity.whatsappAccountId)
            .sort(),
        ['account-a', 'account-b']);
});
