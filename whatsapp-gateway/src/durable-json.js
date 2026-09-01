import { randomUUID } from 'node:crypto';
import { link, mkdir, open, rename, rm } from 'node:fs/promises';
import path from 'node:path';

async function writeSyncedJson(filePath, payload) {
    const fileHandle = await open(filePath, 'wx', 0o600);
    try {
        await fileHandle.writeFile(JSON.stringify(payload));
        await fileHandle.sync();
    } finally {
        await fileHandle.close();
    }
}

function temporaryPath(filePath) {
    return `${filePath}.${process.pid}.${randomUUID()}.tmp`;
}

export async function writeJsonAtomic(filePath, payload) {
    await mkdir(path.dirname(filePath), { recursive: true });
    const pendingPath = temporaryPath(filePath);
    try {
        await writeSyncedJson(pendingPath, payload);
        await rename(pendingPath, filePath);
    } finally {
        await rm(pendingPath, { force: true });
    }
}

export async function createJsonAtomic(filePath, payload) {
    await mkdir(path.dirname(filePath), { recursive: true });
    const pendingPath = temporaryPath(filePath);
    try {
        await writeSyncedJson(pendingPath, payload);
        await link(pendingPath, filePath);
        return true;
    } catch (error) {
        if (error.code === 'EEXIST') return false;
        throw error;
    } finally {
        await rm(pendingPath, { force: true });
    }
}
