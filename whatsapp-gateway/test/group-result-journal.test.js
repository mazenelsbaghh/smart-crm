import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { resolveSessionIdentity, sessionMapKey } from '../src/session-identity.js';

const originalDirectory = process.cwd();
const isolatedDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-group-journal-'));
process.chdir(isolatedDirectory);
process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET = 'group-journal-test-secret';

const {
    createGroup,
    getJournaledGroupResult,
    sessions,
    statuses
} = await import('../src/baileys-manager.js');

test('provider group ID is journaled before invite enrichment and scoped by account', async context => {
    context.after(() => {
        process.chdir(originalDirectory);
        fs.rmSync(isolatedDirectory, { recursive: true, force: true });
    });
    const identity = resolveSessionIdentity('project-1', 'account-a');
    const key = sessionMapKey(identity);
    let releaseInvite;
    let reportInviteStarted;
    const inviteGate = new Promise(resolve => { releaseInvite = resolve; });
    const inviteStarted = new Promise(resolve => { reportInviteStarted = resolve; });
    context.after(() => releaseInvite());
    sessions.set(key, {
        groupCreate: async () => ({ id: 'provider-group-1@g.us' }),
        groupInviteCode: async () => {
            reportInviteStarted();
            await inviteGate;
            return 'provider-invite-1';
        },
        groupSettingUpdate: async () => {}
    });
    statuses.set(key, 'Connected');
    context.after(() => {
        sessions.delete(key);
        statuses.delete(key);
    });

    const creation = createGroup({
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        subject: 'Support',
        participants: ['201000000001'],
        idempotencyKey: 'group-command-1'
    });
    await inviteStarted;

    const preliminary = await getJournaledGroupResult(
        identity.projectId,
        identity.whatsappAccountId,
        'group-command-1');
    assert.equal(preliminary.jid, 'provider-group-1@g.us');
    assert.equal(preliminary.inviteLink, null);
    assert.equal(await getJournaledGroupResult(
        identity.projectId,
        'account-b',
        'group-command-1'), null);

    releaseInvite();
    const completed = await creation;
    const journaled = await getJournaledGroupResult(
        identity.projectId,
        identity.whatsappAccountId,
        'group-command-1');
    assert.deepEqual(journaled, completed);
    assert.equal(journaled.inviteLink, 'https://chat.whatsapp.com/provider-invite-1');
});
