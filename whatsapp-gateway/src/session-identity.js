import fs from 'fs';
import path from 'path';

export const ACCOUNT_SESSIONS_DIRECTORY = '.accounts-v2';

const SAFE_PATH_SEGMENT = /^[A-Za-z0-9_-]{1,128}$/;

export function resolveSessionIdentity(projectId, whatsappAccountId) {
    const normalizedProjectId = normalizedPathSegment(projectId, 'projectId');
    const normalizedAccountId = whatsappAccountId === undefined || whatsappAccountId === null
        ? normalizedProjectId
        : normalizedPathSegment(whatsappAccountId, 'whatsappAccountId');
    const isLegacy = normalizedAccountId.toLowerCase() === normalizedProjectId.toLowerCase();

    return {
        projectId: normalizedProjectId,
        whatsappAccountId: isLegacy ? normalizedProjectId : normalizedAccountId,
        isLegacy
    };
}

export function sessionMapKey(identity) {
    return identity.isLegacy
        ? identity.projectId
        : `account:${identity.projectId}:${identity.whatsappAccountId}`;
}

export function sessionAuthDirectory(sessionsDirectory, identity) {
    return identity.isLegacy
        ? path.join(sessionsDirectory, identity.projectId)
        : path.join(
            sessionsDirectory,
            ACCOUNT_SESSIONS_DIRECTORY,
            identity.projectId,
            identity.whatsappAccountId);
}

export function storedSessionIdentities(sessionsDirectory) {
    if (!fs.existsSync(sessionsDirectory)) return [];
    return [
        ...legacySessionIdentities(sessionsDirectory),
        ...accountSessionIdentities(sessionsDirectory)
    ];
}

function normalizedPathSegment(candidate, fieldName) {
    if (typeof candidate !== 'string' || !SAFE_PATH_SEGMENT.test(candidate.trim())) {
        throw new TypeError(`${fieldName} has an invalid format`);
    }
    return candidate.trim();
}

function legacySessionIdentities(sessionsDirectory) {
    return directoryNames(sessionsDirectory)
        .filter(projectId => !projectId.startsWith('.'))
        .filter(projectId => fs.existsSync(path.join(sessionsDirectory, projectId, 'creds.json')))
        .map(projectId => safeIdentity(projectId, projectId))
        .filter(Boolean);
}

function accountSessionIdentities(sessionsDirectory) {
    const accountRoot = path.join(sessionsDirectory, ACCOUNT_SESSIONS_DIRECTORY);
    return directoryNames(accountRoot).flatMap(projectId =>
        directoryNames(path.join(accountRoot, projectId))
            .filter(accountId => fs.existsSync(path.join(accountRoot, projectId, accountId, 'creds.json')))
            .map(accountId => safeIdentity(projectId, accountId))
            .filter(identity => identity && !identity.isLegacy));
}

function directoryNames(directory) {
    if (!fs.existsSync(directory)) return [];
    return fs.readdirSync(directory, { withFileTypes: true })
        .filter(entry => entry.isDirectory())
        .map(entry => entry.name);
}

function safeIdentity(projectId, whatsappAccountId) {
    try {
        return resolveSessionIdentity(projectId, whatsappAccountId);
    } catch (error) {
        if (error instanceof TypeError) return null;
        throw error;
    }
}
