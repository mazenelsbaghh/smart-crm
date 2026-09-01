import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { writeJsonAtomic } from './durable-json.js';

const JOURNAL_DIRECTORY = '.gateway-state-v1/group-results';

function commandDigest(idempotencyKey) {
    return createHash('sha256').update(idempotencyKey).digest('hex');
}

function journalFile(directory, identity, idempotencyKey) {
    return path.join(
        directory,
        identity.projectId,
        identity.whatsappAccountId,
        `${commandDigest(idempotencyKey)}.json`);
}

export function groupJournalDirectory(sessionsDirectory) {
    return path.join(sessionsDirectory, JOURNAL_DIRECTORY);
}

export function createGroupResultJournal(directory) {
    async function record(identity, idempotencyKey, groupResult) {
        if (!idempotencyKey) return;
        await writeJsonAtomic(journalFile(directory, identity, idempotencyKey), {
            version: 1,
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            idempotencyKey,
            groupResult
        });
    }

    async function get(identity, idempotencyKey) {
        if (!idempotencyKey) return null;
        try {
            const entry = JSON.parse(await readFile(
                journalFile(directory, identity, idempotencyKey),
                'utf8'));
            return entry.groupResult;
        } catch (error) {
            if (error.code === 'ENOENT') return null;
            throw error;
        }
    }

    return Object.freeze({ record, get });
}
