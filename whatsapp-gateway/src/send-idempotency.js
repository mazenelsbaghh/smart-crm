import { createClient } from 'redis';
import { resolveSessionIdentity, sessionMapKey } from './session-identity.js';

const DEFAULT_TTL_SECONDS = 30 * 24 * 60 * 60;

export function createSendIdempotency({
    redisUrl = process.env.REDIS_URL,
    createRedisClient = createClient,
    ttlSeconds = DEFAULT_TTL_SECONDS,
} = {}) {
    let redisPromise;

    async function redis() {
        if (!redisUrl) throw new Error('REDIS_URL is required for idempotent sends');
        if (!redisPromise) {
            redisPromise = (async () => {
                const client = createRedisClient({ url: redisUrl });
                client.on('error', (error) => console.error(`[GATEWAY IDEMPOTENCY] Redis error: ${error.message}`));
                await client.connect();
                return client;
            })().catch((error) => {
                redisPromise = undefined;
                throw error;
            });
        }
        return redisPromise;
    }

    async function claim(projectId, whatsappAccountIdOrKey, maybeIdempotencyKey) {
        const usesLegacySignature = maybeIdempotencyKey === undefined;
        const whatsappAccountId = usesLegacySignature ? undefined : whatsappAccountIdOrKey;
        const idempotencyKey = usesLegacySignature ? whatsappAccountIdOrKey : maybeIdempotencyKey;
        const identity = resolveSessionIdentity(projectId, whatsappAccountId);
        const client = await redis();
        const key = `whatsapp:send:${sessionMapKey(identity)}:${idempotencyKey}`;
        const claimed = await client.set(
            key,
            JSON.stringify({ state: 'processing' }),
            { NX: true, EX: ttlSeconds },
        );
        if (claimed === 'OK') return { key, claimed: true };
        const stored = await client.get(key);
        if (stored) {
            const parsed = JSON.parse(stored);
            if (parsed.state === 'sent' && parsed.result) {
                return { key, claimed: false, result: parsed.result };
            }
        }
        return { key, claimed: false };
    }

    async function complete(key, result) {
        const client = await redis();
        await client.set(
            key,
            JSON.stringify({ state: 'sent', result }),
            { EX: ttlSeconds },
        );
    }

    async function release(key) {
        const client = await redis();
        await client.del(key);
    }

    return { claim, complete, release };
}
