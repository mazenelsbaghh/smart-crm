import test from 'node:test';
import assert from 'node:assert/strict';
import { verifyWhatsAppNumber } from '../src/number-verification.js';

test('queries WhatsApp registration without sending the person a message', async () => {
    const queries = [];
    const socket = { onWhatsApp: async phone => { queries.push(phone); return [{ jid: phone + '@s.whatsapp.net', exists: true }]; } };
    assert.equal(await verifyWhatsAppNumber(socket, '201033334444'), true);
    assert.deepEqual(queries, ['201033334444']);
});

test('confirmed absence of WhatsApp registration is distinct from an unavailable lookup', async () => {
    assert.equal(await verifyWhatsAppNumber({ onWhatsApp: async () => [{ exists: false }] }, '201033334444'), false);
    assert.equal(await verifyWhatsAppNumber({ onWhatsApp: async () => [] }, '201033334444'), false);
    for (const results of [undefined, null, [{}]]) {
        await assert.rejects(verifyWhatsAppNumber({ onWhatsApp: async () => results }, '201033334444'), /no reliable result/);
    }
});

test('mock/disconnected sockets and lookup errors never validate a friend', async () => {
    await assert.rejects(verifyWhatsAppNumber(null, '201033334444'), /unavailable/);
    await assert.rejects(verifyWhatsAppNumber({ isMock: true, onWhatsApp: async () => [{ exists: true }] }, '201033334444'), /unavailable/);
    await assert.rejects(verifyWhatsAppNumber({ onWhatsApp: async () => { throw new Error('offline'); } }, '201033334444'), /offline/);
    await assert.rejects(verifyWhatsAppNumber({}, 'invalid'), /Invalid canonical/);
});
