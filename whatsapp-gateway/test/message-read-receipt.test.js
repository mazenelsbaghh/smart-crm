import assert from 'node:assert/strict';
import test from 'node:test';
import { sendCustomerReadReceipt } from '../src/message-read-receipt.js';

for (const remoteJid of ['201012345678@s.whatsapp.net', '123456789012345@lid']) {
    test(`reads only the selected incoming customer message for ${remoteJid}`, async () => {
        const receipts = [];
        const socket = { sendReceipt: async (...receipt) => receipts.push(receipt) };
        await sendCustomerReadReceipt(socket, { remoteJid, messageId: 'last-before-booking' });
        assert.deepEqual(receipts, [[remoteJid, undefined, ['last-before-booking'], 'read']]);
    });
}

for (const remoteJid of ['120000@g.us', 'status@broadcast', 'newsletter@newsletter', 'not-a-jid']) {
    test(`does not send booking read receipts to ${remoteJid}`, async () => {
        const receipts = [];
        const socket = { sendReceipt: async (...receipt) => receipts.push(receipt) };
        await assert.rejects(sendCustomerReadReceipt(socket, { remoteJid, messageId: 'message' }), TypeError);
        assert.deepEqual(receipts, []);
    });
}

test('provider failures remain retryable rather than reporting a successful read', async () => {
    const socket = { sendReceipt: async () => { throw new Error('disconnected'); } };
    await assert.rejects(sendCustomerReadReceipt(socket, {
        remoteJid: '201012345678@s.whatsapp.net', messageId: 'message',
    }), /disconnected/);
});
