import test from 'node:test';
import assert from 'node:assert/strict';
import { readGroupMembership } from '../src/group-membership.js';

const jid = '120363123456789@g.us';
const phones = ['201011111111', '201022222222'];

test('matches phone and mapped LID members, deduplicates requested phones', async () => {
    const socket = {
        groupMetadata: async () => ({ id: jid, size: 2, participants: [
            { id: '201011111111:4@s.whatsapp.net' }, { id: '987654321@lid' }
        ] }),
        signalRepository: { lidMapping: { getPNForLID: async () => '201022222222@s.whatsapp.net' } }
    };
    const result = await readGroupMembership(socket, jid, [...phones, phones[0], '201033333333']);
    assert.deepEqual(result.members.map(m => m.status), ['joined', 'joined', 'not_joined']);
});

for (const [reason, metadata] of [
    ['empty snapshot', { id: jid, participants: [] }],
    ['unresolved LID', { id: jid, participants: [{ id: '987654321@lid' }] }],
    ['incomplete snapshot', { id: jid, size: 2, participants: [{ id: '201011111111@s.whatsapp.net' }] }],
]) {
    test(`does not label missing numbers outside when ${reason}`, async () => {
        const result = await readGroupMembership({ groupMetadata: async () => metadata }, jid, [phones[1]]);
        assert.equal(result.members[0].status, 'unknown');
    });
}

test('recognizes a known phone through its LID without guessing other hidden numbers', async () => {
    const socket = { groupMetadata: async () => ({ id: jid, participants: [{ id: '123456789@lid' }] }),
        signalRepository: { lidMapping: { getLIDForPN: async phone => phone === `${phones[0]}@s.whatsapp.net` ? '123456789@lid' : null } } };
    const result = await readGroupMembership(socket, jid, phones);
    assert.deepEqual(result.members.map(member => member.status), ['joined', 'unknown']);
});

for (const [size, expected] of [[1, 'not_joined'], [2, 'unknown']]) {
    test(`checks absence by a known LID only with a complete snapshot (size ${size})`, async () => {
        const socket = { groupMetadata: async () => ({ id: jid, size, participants: [{ id: '123456789@lid' }] }),
            signalRepository: { lidMapping: { getLIDForPN: async () => '987654321@lid' } } };
        const result = await readGroupMembership(socket, jid, [phones[0]]);
        assert.equal(result.members[0].status, expected);
    });
}

for (const [reason, socket] of [
    ['offline', null], ['mock', { isMock: true }],
    ['wrong group', { groupMetadata: async () => ({ id: 'other@g.us', participants: [] }) }],
    ['provider error', { groupMetadata: async () => { throw new Error('Offline'); } }],
    ['timeout', { groupMetadata: () => new Promise(() => {}) }],
]) {
    test(`returns no false membership when ${reason}`, async () => {
        await assert.rejects(readGroupMembership(socket, jid, phones, 10));
    });
}
