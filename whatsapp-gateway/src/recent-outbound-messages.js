import { sessionMapKey } from './session-identity.js';

const DEFAULT_TTL_MS = 10 * 60 * 1000;

function normalizedJid(jid) {
    const [address, domain = ''] = jid.trim().toLowerCase().split('@');
    return `${address.split(':')[0]}@${domain}`;
}

function messageKey(identity, recipientJid, providerMessageId) {
    return `${sessionMapKey(identity)}:${normalizedJid(recipientJid)}:${providerMessageId}`;
}

export function createRecentOutboundMessages({
    ttlMs = DEFAULT_TTL_MS,
    now = () => Date.now()
} = {}) {
    const expirations = new Map();
    let recordsSincePrune = 0;

    function pruneExpired() {
        const currentTime = now();
        for (const [key, active] of expirations) {
            const unexpired = active.filter(expiry => expiry > currentTime);
            if (unexpired.length === 0) expirations.delete(key);
            else expirations.set(key, unexpired);
        }
    }

    function record(identity, recipientJid, providerMessageId) {
        recordsSincePrune++;
        if (recordsSincePrune >= 100) {
            pruneExpired();
            recordsSincePrune = 0;
        }
        const key = messageKey(identity, recipientJid, providerMessageId);
        const active = (expirations.get(key) || []).filter(expiry => expiry > now());
        active.push(now() + ttlMs);
        expirations.set(key, active);
    }

    function consumeEcho(identity, senderJid, providerMessageId) {
        const key = messageKey(identity, senderJid, providerMessageId);
        const active = (expirations.get(key) || []).filter(expiry => expiry > now());
        if (active.length === 0) {
            expirations.delete(key);
            return false;
        }

        active.shift();
        if (active.length === 0) expirations.delete(key);
        else expirations.set(key, active);
        return true;
    }

    return { record, consumeEcho };
}

export const recentOutboundMessages = createRecentOutboundMessages();
