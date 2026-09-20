import { normalizeProviderTimestamp } from './inbound-message-outbox.js';

const MISSED_MESSAGE_LOOKBACK_SECONDS = 60 * 60;

export function shouldCaptureInboundUpsert(type, timestamp, connectionOpenedAt) {
    if (type === 'notify') return true;
    if (type !== 'append' || !connectionOpenedAt) return false;

    const openedAtSeconds = Date.parse(connectionOpenedAt) / 1000;
    if (!Number.isFinite(openedAtSeconds)) return false;
    return normalizeProviderTimestamp(timestamp) >= openedAtSeconds - MISSED_MESSAGE_LOOKBACK_SECONDS;
}
