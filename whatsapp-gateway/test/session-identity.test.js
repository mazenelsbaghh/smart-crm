import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
    ACCOUNT_SESSIONS_DIRECTORY,
    resolveSessionIdentity,
    sessionAuthDirectory,
    sessionMapKey,
    storedSessionIdentities
} from '../src/session-identity.js';

test('missing, null, and project-matching account IDs keep the legacy session slot', () => {
    const sessionsDirectory = path.join('var', 'sessions');
    const missing = resolveSessionIdentity('project-1');
    const nullAccount = resolveSessionIdentity('project-1', null);
    const matching = resolveSessionIdentity('project-1', 'PROJECT-1');

    for (const identity of [missing, nullAccount, matching]) {
        assert.equal(identity.whatsappAccountId, 'project-1');
        assert.equal(identity.isLegacy, true);
        assert.equal(sessionMapKey(identity), 'project-1');
        assert.equal(
            sessionAuthDirectory(sessionsDirectory, identity),
            path.join(sessionsDirectory, 'project-1'));
    }
});

test('additional accounts have isolated map keys and v2 credential directories', () => {
    const sessionsDirectory = path.join('var', 'sessions');
    const first = resolveSessionIdentity('project-1', 'account-a');
    const second = resolveSessionIdentity('project-1', 'account-b');

    assert.notEqual(sessionMapKey(first), sessionMapKey(second));
    assert.equal(
        sessionAuthDirectory(sessionsDirectory, first),
        path.join(sessionsDirectory, ACCOUNT_SESSIONS_DIRECTORY, 'project-1', 'account-a'));
    assert.equal(
        sessionAuthDirectory(sessionsDirectory, second),
        path.join(sessionsDirectory, ACCOUNT_SESSIONS_DIRECTORY, 'project-1', 'account-b'));
});

test('session IDs cannot escape their credential directory', () => {
    assert.throws(
        () => resolveSessionIdentity('project-1', '../account-c'),
        /whatsappAccountId has an invalid format/);
});

test('startup discovery restores credentials from legacy and accounts-v2 layouts', (t) => {
    const sessionsDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-session-identity-'));
    t.after(() => fs.rmSync(sessionsDirectory, { recursive: true, force: true }));

    const credentialDirectories = [
        path.join(sessionsDirectory, 'legacy-project'),
        path.join(sessionsDirectory, ACCOUNT_SESSIONS_DIRECTORY, 'project-1', 'account-a'),
        path.join(sessionsDirectory, ACCOUNT_SESSIONS_DIRECTORY, 'project-1', 'account-b')
    ];
    for (const directory of credentialDirectories) {
        fs.mkdirSync(directory, { recursive: true });
        fs.writeFileSync(path.join(directory, 'creds.json'), '{}');
    }

    const identities = storedSessionIdentities(sessionsDirectory)
        .map(identity => [identity.projectId, identity.whatsappAccountId])
        .sort();

    assert.deepEqual(identities, [
        ['legacy-project', 'legacy-project'],
        ['project-1', 'account-a'],
        ['project-1', 'account-b']
    ]);
});
