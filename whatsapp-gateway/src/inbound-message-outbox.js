import { createHash } from 'node:crypto';
import { readFile, readdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { createJsonAtomic, writeJsonAtomic } from './durable-json.js';
import { resolveSessionIdentity } from './session-identity.js';

const RETRY_DELAYS_MS = [1_000, 5_000, 15_000, 60_000, 300_000];
const OUTBOX_DIRECTORY = '.gateway-state-v1/inbound-outbox';

function envelopeDigest(providerMessageId) {
    return createHash('sha256').update(providerMessageId).digest('hex');
}

function envelopePath(outboxDirectory, identity, providerMessageId) {
    return path.join(
        outboxDirectory,
        identity.projectId,
        identity.whatsappAccountId,
        `${envelopeDigest(providerMessageId)}.json`);
}

function retryDelay(retryDelaysMs, attempts) {
    return retryDelaysMs[Math.min(attempts - 1, retryDelaysMs.length - 1)];
}

async function envelopeFiles(directory) {
    let entries;
    try {
        entries = await readdir(directory, { withFileTypes: true });
    } catch (error) {
        if (error.code === 'ENOENT') return [];
        throw error;
    }

    const nested = await Promise.all(entries.map(entry => {
        const entryPath = path.join(directory, entry.name);
        if (entry.isDirectory()) return envelopeFiles(entryPath);
        return entry.isFile() && entry.name.endsWith('.json') ? [entryPath] : [];
    }));
    return nested.flat();
}

function assertProviderMessageId(message) {
    if (typeof message.messageId !== 'string' || !message.messageId.trim()) {
        throw new TypeError('Inbound provider messageId is required');
    }
}

export function inboundOutboxDirectory(sessionsDirectory) {
    return path.join(sessionsDirectory, OUTBOX_DIRECTORY);
}

class InboundMessageOutbox {
    #directory;
    #forwardMessage;
    #retryDelaysMs;
    #now;
    #logger;
    #inFlight = new Map();
    #captures = new Map();
    #retryTimers = new Map();

    constructor({ directory, forwardMessage, retryDelaysMs, now, logger }) {
        this.#directory = directory;
        this.#forwardMessage = forwardMessage;
        this.#retryDelaysMs = retryDelaysMs;
        this.#now = now;
        this.#logger = logger;
    }

    #cancelRetry(filePath) {
        const timer = this.#retryTimers.get(filePath);
        if (timer) clearTimeout(timer);
        this.#retryTimers.delete(filePath);
    }

    #scheduleRetry(filePath, delayMs) {
        this.#cancelRetry(filePath);
        const timer = setTimeout(() => {
            this.#retryTimers.delete(filePath);
            this.#dispatch(filePath).catch(error => {
                if (error.code === 'ENOENT') return;
                this.#logger.error(`[GATEWAY INBOUND OUTBOX] Retry failed before delivery: ${error.message}`);
                this.#scheduleRetry(filePath, this.#retryDelaysMs.at(-1));
            });
        }, Math.max(0, delayMs));
        timer.unref();
        this.#retryTimers.set(filePath, timer);
    }

    async #deferEnvelope(filePath, envelope, error) {
        const attempts = envelope.attempts + 1;
        const delayMs = retryDelay(this.#retryDelaysMs, attempts);
        await writeJsonAtomic(filePath, {
            ...envelope,
            attempts,
            nextAttemptAt: this.#now() + delayMs
        });
        this.#logger.error(
            `[GATEWAY INBOUND OUTBOX] Backend delivery failed for ${envelope.projectId}/${envelope.whatsappAccountId}; retained for retry: ${error.message}`);
        this.#scheduleRetry(filePath, delayMs);
    }

    async #dispatchFile(filePath) {
        const envelope = JSON.parse(await readFile(filePath, 'utf8'));
        const waitMs = envelope.nextAttemptAt === null
            ? 0
            : envelope.nextAttemptAt - this.#now();
        if (waitMs > 0) {
            this.#scheduleRetry(filePath, waitMs);
            return;
        }

        try {
            await this.#forwardMessage(envelope.message);
        } catch (error) {
            await this.#deferEnvelope(filePath, envelope, error);
            return;
        }

        this.#cancelRetry(filePath);
        await rm(filePath, { force: true });
    }

    #dispatch(filePath) {
        const activeDelivery = this.#inFlight.get(filePath);
        if (activeDelivery) return activeDelivery;
        const delivery = this.#dispatchFile(filePath).finally(() => {
            if (this.#inFlight.get(filePath) === delivery) this.#inFlight.delete(filePath);
        });
        this.#inFlight.set(filePath, delivery);
        return delivery;
    }

    async #stage(message) {
        const { identity, filePath } = this.#messageLocation(message);
        const created = await createJsonAtomic(filePath, {
            version: 1,
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            providerMessageId: message.messageId,
            message,
            attempts: 0,
            nextAttemptAt: null
        });
        return { filePath, created };
    }

    #messageLocation(message) {
        assertProviderMessageId(message);
        const identity = resolveSessionIdentity(message.projectId, message.whatsappAccountId);
        return {
            identity,
            filePath: envelopePath(this.#directory, identity, message.messageId)
        };
    }

    async #forward(stagedEnvelope, enrichedMessage) {
        if (stagedEnvelope.created) {
            const envelope = JSON.parse(await readFile(stagedEnvelope.filePath, 'utf8'));
            await writeJsonAtomic(stagedEnvelope.filePath, {
                ...envelope,
                message: enrichedMessage
            });
        }
        return this.#dispatch(stagedEnvelope.filePath);
    }

    async #capture(message, enrichMessage) {
        const stagedEnvelope = await this.#stage(message);
        if (!stagedEnvelope.created) return this.#forward(stagedEnvelope, message);
        const enrichedMessage = await enrichMessage(message);
        return this.#forward(stagedEnvelope, enrichedMessage);
    }

    captureAndForward(message, enrichMessage) {
        const { filePath } = this.#messageLocation(message);
        const activeCapture = this.#captures.get(filePath);
        if (activeCapture) return activeCapture;
        const capture = this.#capture(message, enrichMessage).finally(() => {
            if (this.#captures.get(filePath) === capture) this.#captures.delete(filePath);
        });
        this.#captures.set(filePath, capture);
        return capture;
    }

    async restore() {
        const files = await envelopeFiles(this.#directory);
        return Promise.all(files.map(filePath => this.#dispatch(filePath)));
    }

    close() {
        for (const timer of this.#retryTimers.values()) clearTimeout(timer);
        this.#retryTimers.clear();
    }
}

export function createInboundMessageOutbox({
    directory,
    forwardMessage,
    retryDelaysMs = RETRY_DELAYS_MS,
    now = Date.now,
    logger = console
}) {
    return new InboundMessageOutbox({
        directory,
        forwardMessage,
        retryDelaysMs,
        now,
        logger
    });
}
