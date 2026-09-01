import express from 'express';
import dns from 'dns';
import fs from 'fs';
import path from 'path';
import {
    startSession, restoreSession, getQR, getStatus, sendMessage, sendReaction, disconnectSession,
    statuses, sessions, sessionErrors, connectionOpenedAt, hasCredentials, createGroup,
    WhatsAppSessionUnavailableError, getGroupInviteLink, restorePendingInboundMessages,
    getJournaledGroupResult
} from './baileys-manager.js';
import { validateExpectedConnectionEpoch } from './connection-epoch.js';
import { createSendIdempotency } from './send-idempotency.js';
import { executeIdempotentSessionCommand } from './idempotent-session-command.js';
import {
    resolveSessionIdentity,
    sessionMapKey
} from './session-identity.js';
import { restorableSessionIdentities } from './session-lifecycle.js';
import { replaceWithMockSession } from './mock-session.js';

dns.setDefaultResultOrder('ipv4first');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 3000;
const sendIdempotency = createSendIdempotency();
const OUTBOX_RESTORE_RETRY_MS = 30_000;

function parseSessionIdentity(projectId, whatsappAccountId) {
    try {
        return { identity: resolveSessionIdentity(projectId, whatsappAccountId) };
    } catch (error) {
        return { error: error.message };
    }
}

function validateSessionConnection(identity, expectedConnectedAt) {
    const key = sessionMapKey(identity);
    return validateExpectedConnectionEpoch(
        expectedConnectedAt,
        connectionOpenedAt.get(key),
        statuses.get(key) === 'Connected' && sessions.has(key));
}

function invalidIdempotencyKey(idempotencyKey) {
    return idempotencyKey && (typeof idempotencyKey !== 'string'
        || !/^[A-Za-z0-9:_-]{1,128}$/.test(idempotencyKey));
}

async function restoreInboundOutbox() {
    try {
        await restorePendingInboundMessages();
    } catch (error) {
        console.error(`[GATEWAY STARTUP] Failed to restore inbound outbox: ${error.message}`);
        const retry = setTimeout(restoreInboundOutbox, OUTBOX_RESTORE_RETRY_MS);
        retry.unref();
    }
}

app.post('/api/whatsapp/session/start', async (req, res) => {
    const { projectId, whatsappAccountId } = req.body;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });

    try {
        const result = await startSession(
            parsed.identity.projectId,
            parsed.identity.whatsappAccountId);
        res.json(result);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

app.post('/api/whatsapp/session/disconnect', async (req, res) => {
    const { projectId, whatsappAccountId } = req.body;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });

    try {
        const result = await disconnectSession(
            parsed.identity.projectId,
            parsed.identity.whatsappAccountId);
        res.json(result);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

app.get('/api/whatsapp/session/qr', (req, res) => {
    const { projectId, whatsappAccountId } = req.query;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });

    const identity = parsed.identity;
    const qr = getQR(identity.projectId, identity.whatsappAccountId);
    if (!qr) {
        return res.status(404).json({
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            error: sessionErrors.get(sessionMapKey(identity)) || 'QR code not ready or session already connected'
        });
    }
    res.json({
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        qr
    });
});

app.get('/api/whatsapp/session/status', (req, res) => {
    const { projectId, whatsappAccountId } = req.query;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });

    const status = getStatus(
        parsed.identity.projectId,
        parsed.identity.whatsappAccountId);
    res.json(status);
});

app.post('/api/whatsapp/send', async (req, res) => {
    const {
        projectId, whatsappAccountId, to, message, buttons,
        idempotencyKey, expectedConnectedAt
    } = req.body;
    console.log(`[GATEWAY SEND] Request received. projectId: ${projectId}, whatsappAccountId: ${whatsappAccountId}, to: ${to}, message: ${message}, buttons: ${JSON.stringify(buttons || [])}`);
    console.log(`[GATEWAY SEND] Available sessions: ${Array.from(sessions.keys()).join(', ')}`);
    console.log(`[GATEWAY SEND] Available statuses: ${Array.from(statuses.entries()).map(([k, v]) => `${k}=${v}`).join(', ')}`);
    if (!projectId || !to || !message) {
        return res.status(400).json({ error: 'projectId, to, and message are required' });
    }
    if (invalidIdempotencyKey(idempotencyKey)) {
        return res.status(400).json({ error: 'idempotencyKey has an invalid format' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });
    const identity = parsed.identity;

    const commandOutcome = await executeIdempotentSessionCommand({
        idempotencyStore: sendIdempotency,
        identity,
        idempotencyKey,
        validateConnection: () => validateSessionConnection(identity, expectedConnectedAt),
        dispatch: async () => ({
            ...await sendMessage(
                identity.projectId,
                to,
                message,
                buttons,
                identity.whatsappAccountId),
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId
        })
    });
    if (commandOutcome.state === 'invalid-epoch') {
        return res.status(400).json({ error: 'expectedConnectedAt must be an ISO-8601 timestamp' });
    }
    if (commandOutcome.state === 'storage-unavailable') {
        console.error(`[GATEWAY IDEMPOTENCY] Unable to use ${idempotencyKey}: ${commandOutcome.error.message}`);
        return res.status(503).json({ error: 'Idempotent send storage is unavailable' });
    }
    if (commandOutcome.state === 'replayed') {
        return res.json({
            ...commandOutcome.providerResponse,
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            idempotentReplay: true
        });
    }
    if (commandOutcome.state === 'processing') {
        return res.status(409).json({ error: 'Send with this idempotency key is still processing' });
    }
    if (commandOutcome.state === 'stale-epoch') {
        return res.status(412).json({
            code: 'STALE_CONNECTION_EPOCH',
            error: 'WhatsApp connection changed before this message could be sent'
        });
    }
    if (commandOutcome.state === 'failed') {
        const dispatchError = commandOutcome.error;
        console.error(`[GATEWAY SEND] Error sending message: ${dispatchError.message}`);
        if (commandOutcome.releaseError) {
            console.error(`[GATEWAY IDEMPOTENCY] Unable to release definitely-unsent claim: ${commandOutcome.releaseError.message}`);
        }
        if (dispatchError instanceof WhatsAppSessionUnavailableError || dispatchError?.definitelyNotSent === true) {
            return res.status(503).json({ code: dispatchError.code, error: dispatchError.message });
        }
        return res.status(500).json({ code: dispatchError.code, error: dispatchError.message });
    }
    return res.json(commandOutcome.providerResponse);
});

app.post('/api/whatsapp/react', async (req, res) => {
    const {
        projectId, whatsappAccountId, to, reactionText, targetMessageId, targetFromMe,
        idempotencyKey, expectedConnectedAt
    } = req.body;
    console.log(`[GATEWAY REACT] Request received. projectId: ${projectId}, whatsappAccountId: ${whatsappAccountId}, to: ${to}, reactionText: ${reactionText}, targetMessageId: ${targetMessageId}, targetFromMe: ${targetFromMe}`);
    if (!projectId || !to || !reactionText || !targetMessageId) {
        return res.status(400).json({ error: 'projectId, to, reactionText, and targetMessageId are required' });
    }
    if (invalidIdempotencyKey(idempotencyKey)) {
        return res.status(400).json({ error: 'idempotencyKey has an invalid format' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });
    const identity = parsed.identity;

    const commandOutcome = await executeIdempotentSessionCommand({
        idempotencyStore: sendIdempotency,
        identity,
        idempotencyKey,
        validateConnection: () => validateSessionConnection(identity, expectedConnectedAt),
        dispatch: async () => ({
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            status: 'Reacted',
            messageId: await sendReaction(
                identity.projectId,
                to,
                reactionText,
                targetMessageId,
                targetFromMe === true,
                identity.whatsappAccountId)
        })
    });
    if (commandOutcome.state === 'invalid-epoch') {
        return res.status(400).json({ error: 'expectedConnectedAt must be an ISO-8601 timestamp' });
    }
    if (commandOutcome.state === 'storage-unavailable') {
        console.error(`[GATEWAY IDEMPOTENCY] Unable to use ${idempotencyKey}: ${commandOutcome.error.message}`);
        return res.status(503).json({ error: 'Idempotent reaction storage is unavailable' });
    }
    if (commandOutcome.state === 'replayed') {
        return res.json({
            ...commandOutcome.providerResponse,
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId,
            idempotentReplay: true
        });
    }
    if (commandOutcome.state === 'processing') {
        return res.status(409).json({ error: 'Reaction with this idempotency key is still processing' });
    }
    if (commandOutcome.state === 'stale-epoch') {
        return res.status(412).json({
            code: 'STALE_CONNECTION_EPOCH',
            error: 'WhatsApp connection changed before this reaction could be sent'
        });
    }
    if (commandOutcome.state === 'failed') {
        const dispatchError = commandOutcome.error;
        console.error(`[GATEWAY REACT] Error sending reaction: ${dispatchError.message}`);
        if (commandOutcome.releaseError) {
            console.error(`[GATEWAY IDEMPOTENCY] Unable to release definitely-unsent reaction claim: ${commandOutcome.releaseError.message}`);
        }
        if (dispatchError instanceof WhatsAppSessionUnavailableError || dispatchError?.definitelyNotSent === true) {
            return res.status(503).json({ code: dispatchError.code, error: dispatchError.message });
        }
        return res.status(500).json({ code: dispatchError.code, error: dispatchError.message });
    }
    return res.json(commandOutcome.providerResponse);
});

app.post('/api/whatsapp/group/create', async (req, res) => {
    const {
        projectId, whatsappAccountId, subject, participants,
        idempotencyKey, expectedConnectedAt
    } = req.body;
    console.log(`[GATEWAY GROUP CREATE] Request received. projectId: ${projectId}, whatsappAccountId: ${whatsappAccountId}, subject: ${subject}, participants: ${JSON.stringify(participants || [])}`);
    if (!projectId || !subject || !participants || !Array.isArray(participants)) {
        return res.status(400).json({ error: 'projectId, subject, and participants (array) are required' });
    }
    if (!idempotencyKey) {
        return res.status(400).json({ error: 'idempotencyKey is required for durable group creation' });
    }
    if (invalidIdempotencyKey(idempotencyKey)) {
        return res.status(400).json({ error: 'idempotencyKey has an invalid format' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });
    const identity = parsed.identity;
    if (idempotencyKey) {
        try {
            const journaledGroup = await getJournaledGroupResult(
                identity.projectId,
                identity.whatsappAccountId,
                idempotencyKey);
            if (journaledGroup) {
                return res.json({
                    ...journaledGroup,
                    projectId: identity.projectId,
                    whatsappAccountId: identity.whatsappAccountId,
                    idempotentReplay: true,
                    reconciledFromJournal: true
                });
            }
        } catch (error) {
            console.error(`[GATEWAY GROUP JOURNAL] Unable to read group ${idempotencyKey}: ${error.message}`);
            return res.status(503).json({ error: 'Durable group result storage is unavailable' });
        }
    }
    const validateConnection = () => validateSessionConnection(identity, expectedConnectedAt);
    const epochValidation = validateConnection();
    if (epochValidation === 'invalid') {
        return res.status(400).json({ error: 'expectedConnectedAt must be an ISO-8601 timestamp' });
    }
    if (epochValidation === 'stale') {
        return res.status(412).json({
            code: 'STALE_CONNECTION_EPOCH',
            error: 'WhatsApp connection changed before this group could be created'
        });
    }

    let idempotency;
    try {
        if (idempotencyKey) {
            try {
                idempotency = await sendIdempotency.claim(
                    identity.projectId,
                    identity.whatsappAccountId,
                    idempotencyKey);
            } catch (error) {
                console.error(`[GATEWAY IDEMPOTENCY] Unable to claim group ${idempotencyKey}: ${error.message}`);
                return res.status(503).json({ error: 'Idempotent group storage is unavailable' });
            }
            if (idempotency.result) {
                return res.json({
                    ...idempotency.result,
                    projectId: identity.projectId,
                    whatsappAccountId: identity.whatsappAccountId,
                    idempotentReplay: true
                });
            }
            if (!idempotency.claimed) {
                return res.status(409).json({ error: 'Group creation with this idempotency key is still processing' });
            }
        }

        if (validateConnection() === 'stale') {
            if (idempotency?.claimed) await sendIdempotency.release(idempotency.key);
            return res.status(412).json({
                code: 'STALE_CONNECTION_EPOCH',
                error: 'WhatsApp connection changed before this group could be created'
            });
        }

        const result = await createGroup({
            projectId: identity.projectId,
            subject,
            participants,
            whatsappAccountId: identity.whatsappAccountId,
            idempotencyKey
        });
        const response = {
            ...result,
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId
        };
        if (idempotency) await sendIdempotency.complete(idempotency.key, response);
        res.json(response);
    } catch (err) {
        console.error(`[GATEWAY GROUP CREATE] Error creating group: ${err.message}`);
        if (err instanceof WhatsAppSessionUnavailableError || err?.definitelyNotSent === true) {
            if (idempotency?.claimed) {
                try {
                    await sendIdempotency.release(idempotency.key);
                } catch (releaseError) {
                    console.error(`[GATEWAY IDEMPOTENCY] Unable to release definitely-unsent group claim ${idempotency.key}: ${releaseError.message}`);
                }
            }
            return res.status(503).json({ code: err.code, error: err.message });
        }
        res.status(500).json({ code: err.code, error: err.message });
    }
});

app.post('/api/whatsapp/group/invite', async (req, res) => {
    const { projectId, whatsappAccountId, groupJid, expectedConnectedAt } = req.body;
    if (!projectId || !groupJid) {
        return res.status(400).json({ error: 'projectId and groupJid are required' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });
    const identity = parsed.identity;
    const epochValidation = validateSessionConnection(identity, expectedConnectedAt);
    if (epochValidation === 'invalid') {
        return res.status(400).json({ error: 'expectedConnectedAt must be an ISO-8601 timestamp' });
    }
    if (epochValidation === 'stale') {
        return res.status(412).json({
            code: 'STALE_CONNECTION_EPOCH',
            error: 'WhatsApp connection changed before the group invite could be loaded'
        });
    }
    try {
        const result = await getGroupInviteLink(
            identity.projectId,
            groupJid,
            identity.whatsappAccountId);
        res.json({
            ...result,
            projectId: identity.projectId,
            whatsappAccountId: identity.whatsappAccountId
        });
    } catch (err) {
        if (err instanceof WhatsAppSessionUnavailableError || err?.definitelyNotSent === true) {
            return res.status(503).json({ code: err.code, error: err.message });
        }
        res.status(500).json({ error: err.message });
    }
});

const mockSentMessages = [];

// Mock connection endpoint for integration testing
app.post('/api/whatsapp/session/mock', async (req, res) => {
    const { projectId, whatsappAccountId, status, phoneNumber } = req.body;
    console.log(`[MOCK SESSION] Request received. projectId: ${projectId}, whatsappAccountId: ${whatsappAccountId}, status: ${status}, phoneNumber: ${phoneNumber}`);
    if (!projectId || !status) {
        return res.status(400).json({ error: 'projectId and status are required' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });
    const identity = parsed.identity;
    const key = sessionMapKey(identity);
    
    const mockSocket = status === 'Connected'
        ? {
            isMock: true,
            user: { id: phoneNumber ? `${phoneNumber}:1` : '1234567890:1' },
            sendMessage: async (jid, content) => {
                if (content.react) {
                    console.log(`[MOCK REACT] Reacting with ${content.react.text} to message ${content.react.key.id}`);
                    const messageId = `msg_react_${Math.random().toString(36).substring(7)}`;
                    mockSentMessages.push({
                        projectId: identity.projectId,
                        whatsappAccountId: identity.whatsappAccountId,
                        to: jid.split('@')[0],
                        reaction: content.react.text,
                        targetMessageId: content.react.key.id,
                        targetFromMe: content.react.key.fromMe,
                        messageId,
                        timestamp: new Date().toISOString()
                    });
                    return { key: { id: messageId } };
                }
                console.log(`[MOCK SEND] Sending to ${jid}: ${content.text}, buttons: ${JSON.stringify(content.buttons || [])}`);
                const messageId = `msg_${Math.random().toString(36).substring(7)}`;
                mockSentMessages.push({
                    projectId: identity.projectId,
                    whatsappAccountId: identity.whatsappAccountId,
                    to: jid.split('@')[0],
                    message: content.text,
                    buttons: content.buttons ? content.buttons.map(b => b.buttonText.displayText) : [],
                    messageId,
                    timestamp: new Date().toISOString()
                });
                return { key: { id: messageId } };
            }
        }
        : null;
    try {
        await replaceWithMockSession({
            key,
            status,
            mockSocket,
            sessions,
            statuses,
            connectionOpenedAt
        });
    } catch (error) {
        sessionErrors.set(key, error.message);
        return res.status(500).json({ error: `Unable to replace WhatsApp session: ${error.message}` });
    }
    sessionErrors.delete(key);
    
    console.log(`[MOCK SESSION] Current sessions key count: ${sessions.size}`);
    res.json({
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        status,
        message: `Mocked session status of ${identity.projectId}/${identity.whatsappAccountId} to ${status}`
    });
});

app.get('/api/whatsapp/mock/sent', (req, res) => {
    const { projectId, whatsappAccountId } = req.query;
    if (!projectId) return res.json(mockSentMessages);
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });
    const identity = parsed.identity;
    res.json(mockSentMessages.filter(message =>
        message.projectId === identity.projectId
        && message.whatsappAccountId === identity.whatsappAccountId));
});

app.post('/api/whatsapp/mock/clear', (req, res) => {
    const { projectId, whatsappAccountId } = req.body ?? {};
    if (!projectId) {
        mockSentMessages.length = 0;
        return res.json({ message: 'Mock sent messages cleared' });
    }
    const parsed = parseSessionIdentity(projectId, whatsappAccountId);
    if (parsed.error) return res.status(400).json({ error: parsed.error });
    const identity = parsed.identity;
    for (let index = mockSentMessages.length - 1; index >= 0; index -= 1) {
        const message = mockSentMessages[index];
        if (message.projectId === identity.projectId
            && message.whatsappAccountId === identity.whatsappAccountId) {
            mockSentMessages.splice(index, 1);
        }
    }
    res.json({
        projectId: identity.projectId,
        whatsappAccountId: identity.whatsappAccountId,
        message: 'Mock sent messages cleared'
    });
});

app.listen(PORT, async () => {
    console.log(`WhatsApp Gateway listening on port ${PORT}`);
    await restoreInboundOutbox();
    
    if (process.env.AUTO_RESTORE_SESSIONS !== 'true') {
        console.log('[GATEWAY STARTUP] Auto-restore sessions disabled. Sessions will start on demand.');
        return;
    }

    // Auto-restore sessions from /app/sessions or local sessions directory
    try {
        let sessionsDir = '/app/sessions';
        if (!fs.existsSync(sessionsDir)) {
            sessionsDir = path.resolve('./sessions');
        }
        for (const identity of restorableSessionIdentities(sessionsDir)) {
            if (hasCredentials(identity.projectId, identity.whatsappAccountId)) {
                console.log(`[GATEWAY STARTUP] Found existing session for project ${identity.projectId}, WhatsApp account ${identity.whatsappAccountId}. Will restore connection after delay...`);
                // Delay session restore to let WhatsApp servers release the old connection
                setTimeout(() => {
                    console.log(`[GATEWAY STARTUP] Now restoring project ${identity.projectId}, WhatsApp account ${identity.whatsappAccountId}`);
                    restoreSession(identity.projectId, identity.whatsappAccountId).catch(err => {
                        console.error(`[GATEWAY STARTUP] Failed to restore project ${identity.projectId}, WhatsApp account ${identity.whatsappAccountId}: ${err.message}`);
                    });
                }, 10000);
            }
        }
    } catch (err) {
        console.error('[GATEWAY STARTUP] Error scanning sessions directory:', err.message);
    }
});
