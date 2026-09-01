import makeWASocket, { useMultiFileAuthState, DisconnectReason, fetchLatestBaileysVersion, downloadContentFromMessage } from '@whiskeysockets/baileys';
import { randomUUID } from 'node:crypto';
import path from 'path';
import fs from 'fs';
import pino from 'pino';
import { extractAdvertisingReferral } from './ad-referral.js';
import { createBackendClient } from './backend-client.js';
import {
    createGroupResultJournal,
    groupJournalDirectory
} from './group-result-journal.js';
import {
    createInboundMessageOutbox,
    inboundOutboxDirectory
} from './inbound-message-outbox.js';
import {
    resolveSessionIdentity,
    sessionAuthDirectory,
    sessionMapKey
} from './session-identity.js';
import {
    clearSessionDisabled,
    isSessionDisabled,
    markSessionDisabled,
    removeSessionCredentials
} from './session-lifecycle.js';

export const sessions = new Map();
export const qrCodes = new Map();
export const statuses = new Map();
export const sessionErrors = new Map();
export const connectionOpenedAt = new Map();
const reconnectAttempts = new Map();
const reconnectTimers = new Map();
const sessionInitializations = new Map();

const backendClient = createBackendClient();
const MAX_RECONNECT_ATTEMPTS = Number(process.env.MAX_RECONNECT_ATTEMPTS || 3);
const RECONNECT_DELAY_MS = Number(process.env.RECONNECT_DELAY_MS || 5000);
const ALLOW_MOCK_FALLBACK = process.env.ALLOW_MOCK_FALLBACK === 'true';
const inboundMessageOutbox = createInboundMessageOutbox({
    directory: inboundOutboxDirectory(getSessionsDir()),
    forwardMessage: message => backendClient.forwardMessage(message)
});
const groupResultJournal = createGroupResultJournal(
    groupJournalDirectory(getSessionsDir()));

export class WhatsAppSessionUnavailableError extends Error {
    constructor(message) {
        super(message);
        this.name = 'WhatsAppSessionUnavailableError';
        this.code = 'WHATSAPP_SESSION_UNAVAILABLE';
        this.definitelyNotSent = true;
    }
}

export class WhatsAppDeliveryAmbiguousError extends Error {
    constructor(message, cause) {
        super(message, { cause });
        this.name = 'WhatsAppDeliveryAmbiguousError';
        this.code = 'WHATSAPP_DELIVERY_AMBIGUOUS';
    }
}

function getSessionsDir() {
    let sessionsDir = '/app/sessions';
    if (!fs.existsSync(sessionsDir)) {
        sessionsDir = path.resolve('./sessions');
    }
    return sessionsDir;
}

function sessionLabel(identity) {
    return `project ${identity.projectId}, WhatsApp account ${identity.whatsappAccountId}`;
}

function providerMessageId(sentMessage, socket, operation) {
    const confirmedId = sentMessage?.key?.id;
    if (typeof confirmedId === 'string' && confirmedId.trim()) return confirmedId;
    if (socket.isMock) {
        return `msg_${operation}_mock_${randomUUID()}`;
    }
    throw new WhatsAppDeliveryAmbiguousError(
        `WhatsApp accepted the ${operation} request without returning a provider message ID`);
}

async function recordCreatedGroup(identity, idempotencyKey, groupResult) {
    try {
        await groupResultJournal.record(identity, idempotencyKey, groupResult);
    } catch (error) {
        throw new WhatsAppDeliveryAmbiguousError(
            'WhatsApp group was created but its provider result could not be journaled',
            error);
    }
}

export function hasCredentials(projectId, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    const credsFile = path.join(sessionAuthDirectory(getSessionsDir(), identity), 'creds.json');
    if (!fs.existsSync(credsFile)) {
        return false;
    }

    const stats = fs.statSync(credsFile);
    if (stats.size === 0) {
        console.warn(`[baileys-manager] Empty creds.json found for ${sessionLabel(identity)}. Removing corrupted credentials.`);
        fs.rmSync(credsFile, { force: true });
        return false;
    }

    return true;
}

async function downloadAndUploadMedia(projectId, messageKey, mInfo, type) {
    try {
        console.log(`[baileys-manager] Downloading media of type ${type}...`);
        const messagePart = type === 'audio' ? mInfo.audioMessage : mInfo.imageMessage;
        if (!messagePart) return null;

        const stream = await downloadContentFromMessage(messagePart, type);
        let buffer = Buffer.from([]);
        for await (const chunk of stream) {
            buffer = Buffer.concat([buffer, chunk]);
        }

        console.log(`[baileys-manager] Media downloaded. Size: ${buffer.length} bytes. Uploading to backend...`);
        
        const form = new FormData();
        const extension = type === 'audio' ? 'ogg' : 'jpg';
        const contentType = type === 'audio' ? 'audio/ogg' : 'image/jpeg';
        const fileName = `media_${messageKey.id}.${extension}`;
        
        const fileBlob = new Blob([buffer], { type: contentType });
        form.append('file', fileBlob, fileName);

        const response = await backendClient.uploadMedia(projectId, form);

        console.log(`[baileys-manager] Media uploaded successfully. AssetId: ${response.data.id}`);
        return response.data.id;
    } catch (err) {
        console.error(`[baileys-manager] Failed to download or upload media: ${err.message}`);
        return null;
    }
}

function extractPhoneFromJid(jid) {
    if (!jid || !jid.includes('@s.whatsapp.net')) return null;
    return jid.split('@')[0].replace(/\D/g, '') || null;
}

function resolveIncomingSender(key) {
    const rawJid = key.remoteJid || '';
    const senderLid = rawJid.endsWith('@lid') ? rawJid : null;
    const candidatePhone =
        extractPhoneFromJid(key.senderPn) ||
        extractPhoneFromJid(key.remoteJidAlt) ||
        extractPhoneFromJid(key.participantPn) ||
        extractPhoneFromJid(key.participant);

    if (candidatePhone) {
        return {
            sender: candidatePhone,
            senderJid: `${candidatePhone}@s.whatsapp.net`,
            senderLid
        };
    }

    if (rawJid.endsWith('@s.whatsapp.net')) {
        const phone = extractPhoneFromJid(rawJid);
        return {
            sender: phone || rawJid,
            senderJid: rawJid,
            senderLid: null
        };
    }

    return {
        sender: rawJid,
        senderJid: rawJid,
        senderLid
    };
}

export async function startSession(projectId, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    await clearSessionDisabled(getSessionsDir(), identity);
    return startResolvedSession(identity);
}

export async function restoreSession(projectId, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    if (isSessionDisabled(getSessionsDir(), identity)) {
        return {
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            status: 'Disconnected',
            message: 'Session is explicitly disabled'
        };
    }
    return startResolvedSession(identity);
}

async function startResolvedSession(identity) {
    const key = sessionMapKey(identity);
    if (sessions.has(key)) {
        return {
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            status: statuses.get(key) || 'Initializing',
            message: 'Session already active/initializing'
        };
    }

    const activeInitialization = sessionInitializations.get(key);
    if (activeInitialization) return activeInitialization;

    const initialization = initializeSession(identity, key);
    sessionInitializations.set(key, initialization);
    try {
        return await initialization;
    } finally {
        if (sessionInitializations.get(key) === initialization) {
            sessionInitializations.delete(key);
        }
    }
}

async function initializeSession(identity, key) {

    statuses.set(key, 'Initializing');
    connectionOpenedAt.delete(key);
    sessionErrors.delete(key);
    reconnectAttempts.set(key, reconnectAttempts.get(key) || 0);

    const sessionsDir = getSessionsDir();
    fs.mkdirSync(sessionsDir, { recursive: true });

    hasCredentials(identity.projectId, identity.whatsappAccountId);
    const authDir = sessionAuthDirectory(sessionsDir, identity);
    const { state, saveCreds } = await useMultiFileAuthState(authDir);

    let version = [2, 3000, 1017531287];
    try {
        const { version: latestVersion } = await fetchLatestBaileysVersion();
        version = latestVersion;
        console.log(`Using fetched WA Web version: ${version.join('.')}`);
    } catch (err) {
        console.warn('Failed to fetch latest WA Web version, using fallback:', err.message);
    }

    const sock = makeWASocket({
        version,
        auth: state,
        logger: pino({ level: process.env.BAILEYS_LOG_LEVEL || 'warn' }),
        printQRInTerminal: false,
        browser: ['Smart Customer', 'Chrome', '10.0.0'],
        syncFullHistory: false
    });

    sessions.set(key, sock);

    sock.ev.on('connection.update', async (update) => {
        const { connection, lastDisconnect, qr } = update;
        
        // If the session has been replaced by a mock session, don't overwrite its state
        if (sessions.get(key)?.isMock) {
            console.log(`[baileys-manager] Ignoring connection.update for ${sessionLabel(identity)} because it has a mock session.`);
            return;
        }

        if (sessions.get(key) !== sock) {
            console.log(`[baileys-manager] Ignoring stale connection.update for ${sessionLabel(identity)}.`);
            return;
        }
        
        if (qr) {
            console.log(`QR code updated for ${sessionLabel(identity)}`);
            qrCodes.set(key, qr);
            statuses.set(key, 'Initializing');
            connectionOpenedAt.delete(key);
            sessionErrors.delete(key);
            reconnectAttempts.set(key, 0);
        }

        if (connection === 'connecting') {
            statuses.set(key, 'Initializing');
            connectionOpenedAt.delete(key);
        } else if (connection === 'close') {
            const disconnectStatusCode = lastDisconnect?.error?.output?.statusCode;
            const explicitlyDisabled = isSessionDisabled(getSessionsDir(), identity);
            const shouldReconnect = disconnectStatusCode !== DisconnectReason.loggedOut
                && !explicitlyDisabled;
            const errorMessage = lastDisconnect?.error?.message || 'WhatsApp connection closed before pairing completed';
            const attempts = (reconnectAttempts.get(key) || 0) + 1;
            reconnectAttempts.set(key, attempts);
            
            const isPaired = hasCredentials(identity.projectId, identity.whatsappAccountId);
            const maxAttempts = isPaired ? 1000 : MAX_RECONNECT_ATTEMPTS;
            
            // Check if this is a conflict (session replaced by another device/connection)
            const isConflict = errorMessage.toLowerCase().includes('conflict') || 
                               errorMessage.toLowerCase().includes('replaced') ||
                               disconnectStatusCode === 440;
            
            // Use longer delay for conflicts to avoid rapid reconnection loops
            const baseDelay = isConflict ? 30000 : RECONNECT_DELAY_MS;
            const delay = Math.min(baseDelay * Math.pow(2, Math.min(attempts - 1, 3)), 120000);
            
            console.log(`Connection closed for ${sessionLabel(identity)}. Reconnecting: ${shouldReconnect}. Attempt: ${attempts}/${maxAttempts}. Next retry in ${delay}ms. Reason: ${errorMessage}${isConflict ? ' [CONFLICT - using extended delay]' : ''}`);
            
            sessionErrors.set(key, errorMessage);
            connectionOpenedAt.delete(key);

            if (shouldReconnect && attempts < maxAttempts) {
                sessions.delete(key);
                statuses.set(key, isConflict ? 'Reconnecting' : 'Initializing');
                
                // Clear any existing reconnect timer
                const existingTimer = reconnectTimers.get(key);
                if (existingTimer) clearTimeout(existingTimer);
                
                const timer = setTimeout(() => {
                    reconnectTimers.delete(key);
                    restoreSession(identity.projectId, identity.whatsappAccountId).catch((err) => {
                        sessionErrors.set(key, err.message);
                        statuses.set(key, 'Disconnected');
                    });
                }, delay);
                reconnectTimers.set(key, timer);
            } else {
                sessions.delete(key);
                qrCodes.delete(key);
                statuses.set(key, 'Disconnected');
                sessionErrors.set(
                    key,
                    shouldReconnect
                        ? `${errorMessage}. Unable to generate a WhatsApp QR code after ${attempts} attempts.`
                        : errorMessage
                );
                if (disconnectStatusCode === DisconnectReason.loggedOut) {
                    try {
                        await markSessionDisabled(getSessionsDir(), identity);
                        await removeSessionCredentials(getSessionsDir(), identity);
                        console.log(`Cleaned up credentials directory for ${sessionLabel(identity)} because the session was logged out.`);
                    } catch (e) {
                        console.error('Failed to clean auth files', e);
                    }
                } else if (explicitlyDisabled) {
                    console.log(`Session remains disabled for ${sessionLabel(identity)}.`);
                } else {
                    console.log(`Retaining credentials directory for ${sessionLabel(identity)} to allow reconnection later.`);
                }
            }
        } else if (connection === 'open') {
            console.log(`Connection opened successfully for ${sessionLabel(identity)}`);
            const previousStatus = statuses.get(key);
            statuses.set(key, 'Connected');
            if (previousStatus !== 'Connected' || !connectionOpenedAt.has(key)) {
                connectionOpenedAt.set(key, new Date().toISOString());
            }
            qrCodes.delete(key);
            sessionErrors.delete(key);
            reconnectAttempts.set(key, 0);
        }
    });

    sock.ev.on('creds.update', async () => {
        if (sessions.get(key) !== sock) {
            console.log(`[baileys-manager] Ignoring creds.update from a stale session for ${sessionLabel(identity)}.`);
            return;
        }
        await saveCreds();
    });

    sock.ev.on('messages.upsert', async (m) => {
        if (sessions.get(key) !== sock) {
            console.log(`[baileys-manager] Ignoring messages.upsert from a stale session for ${sessionLabel(identity)}.`);
            return;
        }
        const socketConnectionOpenedAt = connectionOpenedAt.get(key) || null;

        if (m.type === 'notify') {
            for (const msg of m.messages) {
                if (!msg.key.fromMe && msg.message) {
                    // Skip WhatsApp Status/Story updates (status@broadcast) and group messages (@g.us)
                    const remoteJid = msg.key.remoteJid || '';
                    if (remoteJid === 'status@broadcast' || remoteJid.endsWith('@broadcast')) {
                        console.log(`[baileys-manager] Skipping status/story message from ${remoteJid}`);
                        continue;
                    }
                    if (remoteJid.endsWith('@g.us')) {
                        console.log(`[baileys-manager] Skipping group message from ${remoteJid}`);
                        continue;
                    }

                    console.log(`[baileys-manager] msg.key: ${JSON.stringify(msg.key)}`);
                    console.log(`[baileys-manager] full msg keys: ${Object.keys(msg).join(', ')}`);
                    if (msg.key.participant) console.log(`[baileys-manager] msg.key.participant: ${msg.key.participant}`);
                    
                    // Mark message as read/seen immediately (DISABLED: "نشيل السين" as requested by user)
                    /*
                    try {
                        await sock.readMessages([msg.key]);
                        console.log(`[baileys-manager] Marked message ${msg.key.id} from ${msg.key.remoteJid} as read/seen.`);
                    } catch (readErr) {
                        console.error(`[baileys-manager] Failed to mark message ${msg.key.id} as read: ${readErr.message}`);
                    }
                    */

                    let mInfo = msg.message;
                    // Unwrap ephemeral or view once wrapper types
                    if (mInfo.ephemeralMessage) mInfo = mInfo.ephemeralMessage.message;
                    if (mInfo.viewOnceMessage) mInfo = mInfo.viewOnceMessage.message;
                    if (mInfo.viewOnceMessageV2) mInfo = mInfo.viewOnceMessageV2.message;

                    if (!mInfo) continue;

                    const { sender, senderJid, senderLid } = resolveIncomingSender(msg.key);
                    let content = '';
                    let messageType = 'Text';

                    if (mInfo.conversation) {
                        content = mInfo.conversation;
                        messageType = 'Text';
                    } else if (mInfo.extendedTextMessage) {
                        content = mInfo.extendedTextMessage.text || '';
                        messageType = 'Text';
                    } else if (mInfo.imageMessage) {
                        content = mInfo.imageMessage.caption || '[Image]';
                        messageType = 'Image';
                    } else if (mInfo.audioMessage) {
                        content = '[Voice Note]';
                        messageType = 'Voice';
                    } else if (mInfo.videoMessage) {
                        content = mInfo.videoMessage.caption || '[Video]';
                        messageType = 'Text';
                    } else if (mInfo.documentMessage) {
                        content = mInfo.documentMessage.title || mInfo.documentMessage.caption || '[Document]';
                        messageType = 'Text';
                    } else if (mInfo.buttonsResponseMessage) {
                        content = mInfo.buttonsResponseMessage.selectedDisplayText || mInfo.buttonsResponseMessage.selectedButtonId || '';
                        messageType = 'Text';
                    } else if (mInfo.templateButtonReplyMessage) {
                        content = mInfo.templateButtonReplyMessage.selectedId || '';
                        messageType = 'Text';
                    } else if (mInfo.listResponseMessage) {
                        content = mInfo.listResponseMessage.title || mInfo.listResponseMessage.selectedRowId || '';
                        messageType = 'Text';
                    } else if (mInfo.reactionMessage) {
                        const emoji = mInfo.reactionMessage.text || '';
                        content = emoji ? `[تفاعل] ${emoji}` : '[تم إزالة التفاعل]';
                        messageType = 'Reaction';
                    } else {
                        // Fallback text extraction
                        content = mInfo.conversation || '';
                    }

                    const timestamp = msg.messageTimestamp;
                    const advertisingContext = extractAdvertisingReferral(msg.message);
                    const inboundMessage = {
                        projectId: identity.projectId,
                        whatsappAccountId: identity.whatsappAccountId,
                        messageId: msg.key.id,
                        sender,
                        senderJid,
                        senderLid,
                        name: msg.pushName || '',
                        content,
                        messageType,
                        timestamp,
                        connectionOpenedAt: socketConnectionOpenedAt,
                        assetId: null,
                        advertisingContext
                    };
                    await inboundMessageOutbox.captureAndForward(inboundMessage, async () => {
                        let assetId = null;
                        if (messageType === 'Image') {
                            assetId = await downloadAndUploadMedia(identity.projectId, msg.key, mInfo, 'image');
                        } else if (messageType === 'Voice') {
                            assetId = await downloadAndUploadMedia(identity.projectId, msg.key, mInfo, 'audio');
                        }

                        console.log(`Forwarding durable message from ${sender} (type=${messageType}) to backend webhook: "${content.substring(0, 50)}..."`);
                        return { ...inboundMessage, assetId };
                    });
                }
            }
        }
    });

    return {
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        status: 'Initializing',
        message: 'Session started. Waiting for a scannable QR code.'
    };
}

export async function restorePendingInboundMessages() {
    return inboundMessageOutbox.restore();
}

export async function getJournaledGroupResult(projectId, whatsappAccountId, idempotencyKey) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    return groupResultJournal.get(identity, idempotencyKey);
}

export function getQR(projectId, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    return qrCodes.get(sessionMapKey(identity)) || null;
}

export function getStatus(projectId, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    const key = sessionMapKey(identity);
    const status = statuses.get(key) || 'Disconnected';
    const sock = sessions.get(key);
    const phoneNumber = sock?.user?.id?.split(':')[0] || null;
    const error = sessionErrors.get(key) || null;
    return {
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        status,
        phoneNumber,
        error,
        connectedAt: connectionOpenedAt.get(key) || null
    };
}

function messageText(text) {
    if (typeof text !== 'string' || !text.trimStart().startsWith('{')) return text;

    try {
        const payload = JSON.parse(text);
        for (const field of ['whatsapp_message', 'message', 'text']) {
            if (typeof payload[field] === 'string' && payload[field].trim()) return payload[field];
        }
    } catch {
    }

    return text;
}

export async function sendMessage(projectId, to, text, buttons, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    const key = sessionMapKey(identity);
    text = messageText(text);
    const sock = sessions.get(key);
    console.log(`[baileys-manager] sendMessage request: ${sessionLabel(identity)}, to=${to}, text="${text}", buttons=${JSON.stringify(buttons || [])}, isMock=${sock ? !!sock.isMock : 'no sock'}`);
    
    if (!sock || statuses.get(key) !== 'Connected') {
        const currentStatus = statuses.get(key) || 'Disconnected';
        console.warn(`[baileys-manager] Session not connected (status: ${currentStatus}).`);
        
        if (sock && sock.isMock) {
            const sent = await sock.sendMessage(to + '@s.whatsapp.net', { text });
            return {
                messageId: providerMessageId(sent, sock, 'send'),
                status: 'Sent'
            };
        }

        if (!ALLOW_MOCK_FALLBACK) {
            throw new WhatsAppSessionUnavailableError(
                `WhatsApp session for ${sessionLabel(identity)} is not connected. Current status: ${currentStatus}. Reconnect by scanning the QR code.`);
        }
        
        const messageId = `msg_mock_${Math.random().toString(36).substring(7)}`;
        console.log(`[baileys-manager] mock sendMessage success. returned messageId=${messageId}`);
        return { messageId, status: 'Sent' };
    }

    // Sanitize recipient to a valid JID (keep as-is if already a full JID, otherwise strip non-digits and append domain)
    let jid;
    if (to.includes('@')) {
        jid = to;
    } else {
        const cleanTo = to.replace(/\D/g, '');
        // If it starts with 7 or 8 and is 14-15 digits long, it is a WhatsApp LID
        if ((cleanTo.startsWith('7') || cleanTo.startsWith('8')) && (cleanTo.length === 14 || cleanTo.length === 15)) {
            jid = `${cleanTo}@lid`;
        } else {
            jid = `${cleanTo}@s.whatsapp.net`;
        }
    }
    console.log(`[baileys-manager] Sanitized JID for sending: raw="${to}", sanitized="${jid}"`);

    console.log(`[baileys-manager] Attempting sock.sendMessage to ${jid}...`);
    let sent;
    try {
        sent = await sock.sendMessage(jid, { text });
    } catch (error) {
        console.error(`[baileys-manager] sock.sendMessage failed to ${jid}. error=${error.message}`, error);
        throw new WhatsAppDeliveryAmbiguousError(
            `WhatsApp message delivery to ${jid} could not be confirmed`,
            error);
    }
    const messageId = providerMessageId(sent, sock, 'send');
    console.log(`[baileys-manager] sock.sendMessage success. returned messageId=${messageId}`);
    return { messageId, status: 'Sent' };
}

export async function sendReaction(projectId, to, reactionText, targetMessageId, targetFromMe, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    const key = sessionMapKey(identity);
    const sock = sessions.get(key);
    console.log(`[baileys-manager] sendReaction request: ${sessionLabel(identity)}, to=${to}, reactionText="${reactionText}", targetMessageId=${targetMessageId}, targetFromMe=${targetFromMe}, isMock=${sock ? !!sock.isMock : 'no sock'}`);
    
    if (!sock || statuses.get(key) !== 'Connected') {
        const currentStatus = statuses.get(key) || 'Disconnected';
        console.warn(`[baileys-manager] Session not connected (status: ${currentStatus}).`);
        
        if (sock && sock.isMock) {
            const reactionPayload = {
                react: {
                    text: reactionText,
                    key: {
                        remoteJid: to + '@s.whatsapp.net',
                        fromMe: targetFromMe,
                        id: targetMessageId
                    }
                }
            };
            const sent = await sock.sendMessage(to + '@s.whatsapp.net', reactionPayload);
            return providerMessageId(sent, sock, 'reaction');
        }

        if (!ALLOW_MOCK_FALLBACK) {
            throw new WhatsAppSessionUnavailableError(
                `WhatsApp session for ${sessionLabel(identity)} is not connected. Current status: ${currentStatus}. Reconnect by scanning the QR code.`);
        }
        
        const messageId = `msg_react_mock_${Math.random().toString(36).substring(7)}`;
        console.log(`[baileys-manager] mock sendReaction success. returned messageId=${messageId}`);
        return messageId;
    }

    let jid;
    if (to.includes('@')) {
        jid = to;
    } else {
        const cleanTo = to.replace(/\D/g, '');
        if ((cleanTo.startsWith('7') || cleanTo.startsWith('8')) && (cleanTo.length === 14 || cleanTo.length === 15)) {
            jid = `${cleanTo}@lid`;
        } else {
            jid = `${cleanTo}@s.whatsapp.net`;
        }
    }
    console.log(`[baileys-manager] Sanitized JID for reaction: raw="${to}", sanitized="${jid}"`);

    console.log(`[baileys-manager] Attempting sock.sendMessage (reaction) to ${jid}...`);
    const reactionPayload = {
        react: {
            text: reactionText,
            key: {
                remoteJid: jid,
                fromMe: targetFromMe,
                id: targetMessageId
            }
        }
    };
    let sent;
    try {
        sent = await sock.sendMessage(jid, reactionPayload);
    } catch (error) {
        console.error(`[baileys-manager] sock.sendMessage (reaction) failed to ${jid}. error=${error.message}`, error);
        throw new WhatsAppDeliveryAmbiguousError(
            `WhatsApp reaction delivery to ${jid} could not be confirmed`,
            error);
    }
    const messageId = providerMessageId(sent, sock, 'reaction');
    console.log(`[baileys-manager] sock.sendMessage (reaction) success. returned messageId=${messageId}`);
    return messageId;
}

export async function disconnectSession(projectId, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    const key = sessionMapKey(identity);
    const sessionsDirectory = getSessionsDir();
    await markSessionDisabled(sessionsDirectory, identity);
    const reconnectTimer = reconnectTimers.get(key);
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimers.delete(key);
    }
    const initialization = sessionInitializations.get(key);
    if (initialization) {
        try {
            await initialization;
        } catch {
            // A failed initialization has no live socket to close.
        }
    }
    const sock = sessions.get(key);
    sessions.delete(key);
    qrCodes.delete(key);
    sessionErrors.delete(key);
    reconnectAttempts.delete(key);
    connectionOpenedAt.delete(key);
    statuses.set(key, 'Disconnected');

    try {
        if (typeof sock?.end === 'function') {
            await sock.end(undefined);
        }
    } finally {
        await removeSessionCredentials(sessionsDirectory, identity);
        await markSessionDisabled(sessionsDirectory, identity);
    }
    console.log(`Cleaned up credentials directory for ${sessionLabel(identity)}`);
    return {
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        status: 'Disconnected',
        message: 'Session disconnected and cleaned up.'
    };
}

export async function createGroup({
    projectId,
    subject,
    participants,
    whatsappAccountId,
    idempotencyKey
}) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    if (typeof idempotencyKey !== 'string' || !idempotencyKey) {
        throw new TypeError('idempotencyKey is required for durable group creation');
    }
    const key = sessionMapKey(identity);
    const sock = sessions.get(key);
    if (!sock || statuses.get(key) !== 'Connected') {
        throw new WhatsAppSessionUnavailableError(
            `WhatsApp session for ${sessionLabel(identity)} is not connected.`);
    }
    if (sock.isMock) {
        console.log(`[MOCK GROUP] Creating mock group "${subject}" with participants: ${participants.join(', ')}`);
        const groupResult = {
            jid: `mock_group_${randomUUID()}@g.us`,
            inviteLink: `https://chat.whatsapp.com/mock_${randomUUID()}`
        };
        await recordCreatedGroup(identity, idempotencyKey, groupResult);
        return groupResult;
    }

    try {
        // Create the group
        // participants must be an array of phone numbers formatted as '201068690092@s.whatsapp.net'
        const formattedParticipants = participants.map(p => {
            let clean = p.replace(/\+/g, '').trim();
            if (!clean.endsWith('@s.whatsapp.net')) {
                clean += '@s.whatsapp.net';
            }
            return clean;
        });

        console.log(`[baileys-manager] Creating group: ${subject} with participants: ${formattedParticipants.join(', ')}`);
        const group = await sock.groupCreate(subject, formattedParticipants);
        const groupJid = group?.id;
        if (typeof groupJid !== 'string' || !groupJid.trim()) {
            throw new WhatsAppDeliveryAmbiguousError(
                'WhatsApp accepted the group creation request without returning a group ID');
        }
        await recordCreatedGroup(identity, idempotencyKey, {
            jid: groupJid,
            inviteLink: null,
            enrichmentError: 'Invite link is pending and can be loaded separately.'
        });

        // Try to get invite code/link
        let inviteCode = null;
        let enrichmentError = null;
        try {
            inviteCode = await sock.groupInviteCode(groupJid);
        } catch (e) {
            console.error(`[baileys-manager] Failed to get invite code: ${e.message}. Retrying...`);
            try {
                await new Promise(r => setTimeout(r, 1000));
                inviteCode = await sock.groupInviteCode(groupJid);
            } catch (retryError) {
                enrichmentError = `Invite link is pending: ${retryError.message}`;
                console.error(`[baileys-manager] ${enrichmentError}`);
            }
        }
        const inviteLink = inviteCode ? `https://chat.whatsapp.com/${inviteCode}` : null;

        // Lock group settings to admin only (announcement and locked)
        try {
            // lock message sending to admins only
            await sock.groupSettingUpdate(groupJid, 'announcement', 'locked');
            // lock settings editing to admins only
            await sock.groupSettingUpdate(groupJid, 'locked', 'locked');
        } catch (e) {
            console.error(`[baileys-manager] Warning: failed to lock group settings: ${e.message}`);
        }

        const groupResult = { jid: groupJid, inviteLink, enrichmentError };
        await recordCreatedGroup(identity, idempotencyKey, groupResult);
        return groupResult;
    } catch (err) {
        console.error(`[baileys-manager] Failed to create group: ${err.message}`, err);
        if (err instanceof WhatsAppDeliveryAmbiguousError) throw err;
        throw new Error(`Failed to create WhatsApp group: ${err.message}`);
    }
}

export async function getGroupInviteLink(projectId, groupJid, whatsappAccountId) {
    const identity = resolveSessionIdentity(projectId, whatsappAccountId);
    const key = sessionMapKey(identity);
    const sock = sessions.get(key);
    if (!sock || statuses.get(key) !== 'Connected') {
        throw new WhatsAppSessionUnavailableError(
            `WhatsApp session for ${sessionLabel(identity)} is not connected.`);
    }
    if (sock.isMock) return { jid: groupJid, inviteLink: `https://chat.whatsapp.com/mock_${groupJid.replace(/\W/g, '')}` };
    const inviteCode = await sock.groupInviteCode(groupJid);
    if (typeof inviteCode !== 'string' || !inviteCode.trim()) {
        throw new WhatsAppSessionUnavailableError(
            `WhatsApp did not return an invite code for group ${groupJid}. Retry after the session reconnects.`);
    }
    return { jid: groupJid, inviteLink: `https://chat.whatsapp.com/${inviteCode}` };
}
