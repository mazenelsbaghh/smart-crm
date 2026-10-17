export function isCustomerMessageKey({ remoteJid, messageId }) {
    return typeof remoteJid === 'string'
        && /^[0-9]{5,20}@(s\.whatsapp\.net|lid)$/.test(remoteJid)
        && typeof messageId === 'string' && messageId.length > 0 && messageId.length <= 200;
}

export async function sendCustomerReadReceipt(socket, message) {
    if (!isCustomerMessageKey(message)) throw new TypeError('A direct customer message key is required');
    await socket.sendReceipt(message.remoteJid, undefined, [message.messageId], 'read');
}
