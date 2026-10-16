import test from 'node:test';
import assert from 'node:assert/strict';
import { ensureGroupMembers } from '../src/group-admins.js';

const groupJid = '120363123456789@g.us';
const phone = '201010000001';

for (const existing of ['missing', 'member', 'admin', 'superadmin']) {
    test(`includes ${existing} without changing roles, even after a retry`, async () => {
        let participants = existing === 'missing' ? [] : [{
            id: `${phone}@s.whatsapp.net`,
            admin: ['admin', 'superadmin'].includes(existing) ? existing : null
        }];
        const writes = [];
        const socket = {
            groupMetadata: async () => ({ id: groupJid, participants }),
            groupParticipantsUpdate: async (jid, ids, action) => {
                assert.equal(jid, groupJid);
                assert.deepEqual(ids, [`${phone}@s.whatsapp.net`]);
                writes.push(action);
                participants.push({ id: ids[0], admin: null });
            }
        };
        const expected = { complete: true, results: [{ phone, status: 'present' }] };
        assert.deepEqual(await ensureGroupMembers(socket, groupJid, [phone, phone]), expected);
        assert.deepEqual(await ensureGroupMembers(socket, groupJid, [phone]), expected);
        assert.deepEqual(writes, existing === 'missing' ? ['add'] : []);
        assert.equal(participants[0].admin, ['admin', 'superadmin'].includes(existing) ? existing : null);
    });
}

test('recognizes a mapped LID member without adding or promoting them', async () => {
    const member = { id: '123456@lid', admin: null };
    const socket = {
        groupMetadata: async () => ({ id: groupJid, participants: [member] }),
        signalRepository: { lidMapping: { getLIDForPN: async () => member.id } },
        groupParticipantsUpdate: async () => assert.fail('Existing members require no writes')
    };
    assert.deepEqual(await ensureGroupMembers(socket, groupJid, [phone]), {
        complete: true, results: [{ phone, status: 'present' }]
    });
});

test('does not report success when WhatsApp rejects an addition', async () => {
    const socket = {
        groupMetadata: async () => ({ id: groupJid, participants: [] }),
        groupParticipantsUpdate: async () => [{ status: '403' }]
    };
    assert.deepEqual(await ensureGroupMembers(socket, groupJid, [phone]), {
        complete: false, results: [{ phone, status: 'add_failed' }]
    });
});

for (const [reason, socket] of [
    ['offline', null],
    ['mock', { isMock: true }],
    ['wrong group', { groupMetadata: async () => ({ id: 'other@g.us', participants: [] }) }],
    ['provider failure', { groupMetadata: async () => { throw new Error('Offline'); } }]
]) {
    test(`does not claim successful membership when ${reason}`, async () => {
        await assert.rejects(ensureGroupMembers(socket, groupJid, [phone]));
    });
}
