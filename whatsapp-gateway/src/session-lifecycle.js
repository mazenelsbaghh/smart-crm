import fs from 'node:fs';
import { rm } from 'node:fs/promises';
import path from 'node:path';
import { writeJsonAtomic } from './durable-json.js';
import {
    sessionAuthDirectory,
    storedSessionIdentities
} from './session-identity.js';

const GATEWAY_STATE_DIRECTORY = '.gateway-state-v1';
const DISABLED_SESSIONS_DIRECTORY = 'disabled-sessions';

function disabledSessionFile(sessionsDirectory, identity) {
    return path.join(
        sessionsDirectory,
        GATEWAY_STATE_DIRECTORY,
        DISABLED_SESSIONS_DIRECTORY,
        identity.projectId,
        `${identity.whatsappAccountId}.json`);
}

export async function markSessionDisabled(sessionsDirectory, identity) {
    await writeJsonAtomic(disabledSessionFile(sessionsDirectory, identity), {
        version: 1,
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        disabledAt: new Date().toISOString()
    });
}

export function isSessionDisabled(sessionsDirectory, identity) {
    return fs.existsSync(disabledSessionFile(sessionsDirectory, identity));
}

export async function clearSessionDisabled(sessionsDirectory, identity) {
    await rm(disabledSessionFile(sessionsDirectory, identity), { force: true });
}

export async function removeSessionCredentials(sessionsDirectory, identity) {
    const credentialsDirectory = sessionAuthDirectory(sessionsDirectory, identity);
    await rm(credentialsDirectory, { recursive: true, force: true });
    if (fs.existsSync(credentialsDirectory)) {
        throw new Error(`Credential cleanup could not be verified for ${identity.projectId}/${identity.whatsappAccountId}`);
    }
}

export function restorableSessionIdentities(sessionsDirectory) {
    return storedSessionIdentities(sessionsDirectory).filter(identity =>
        !isSessionDisabled(sessionsDirectory, identity));
}
