import makeWASocket, { useMultiFileAuthState, DisconnectReason, fetchLatestBaileysVersion, downloadContentFromMessage } from '@whiskeysockets/baileys';
import path from 'path';
import fs from 'fs';
import axios from 'axios';
import pino from 'pino';

export const sessions = new Map();
export const qrCodes = new Map();
export const statuses = new Map();
export const sessionErrors = new Map();
const reconnectAttempts = new Map();
const reconnectTimers = new Map();

const BACKEND_URL = process.env.BACKEND_URL || 'http://backend:5000';
const MAX_RECONNECT_ATTEMPTS = Number(process.env.MAX_RECONNECT_ATTEMPTS || 3);
const RECONNECT_DELAY_MS = Number(process.env.RECONNECT_DELAY_MS || 5000);

function hasCredentials(projectId) {
    let sessionsDir = '/app/sessions';
    if (!fs.existsSync(sessionsDir)) {
        sessionsDir = path.resolve('./sessions');
    }
    const credsFile = path.join(sessionsDir, projectId, 'creds.json');
    return fs.existsSync(credsFile);
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

        const uploadUrl = `${BACKEND_URL}/api/projects/${projectId}/assets/upload`;
        const response = await axios.post(uploadUrl, form);

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

export async function startSession(projectId) {
    if (sessions.has(projectId)) {
        return { status: statuses.get(projectId) || 'Initializing', message: 'Session already active/initializing' };
    }

    statuses.set(projectId, 'Initializing');
    sessionErrors.delete(projectId);
    reconnectAttempts.set(projectId, reconnectAttempts.get(projectId) || 0);
    
    let sessionsDir = '/app/sessions';
    try {
        if (!fs.existsSync(sessionsDir)) {
            fs.mkdirSync(sessionsDir, { recursive: true });
        }
    } catch (e) {
        sessionsDir = path.resolve('./sessions');
        if (!fs.existsSync(sessionsDir)) {
            fs.mkdirSync(sessionsDir, { recursive: true });
        }
    }
    
    const authDir = path.join(sessionsDir, projectId);
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
        printQRInTerminal: false
    });

    sessions.set(projectId, sock);

    sock.ev.on('connection.update', async (update) => {
        const { connection, lastDisconnect, qr } = update;
        
        // If the session has been replaced by a mock session, don't overwrite its state
        if (sessions.get(projectId)?.isMock) {
            console.log(`[baileys-manager] Ignoring connection.update for project ${projectId} because it has a mock session.`);
            return;
        }
        
        if (qr) {
            console.log(`QR code updated for project ${projectId}`);
            qrCodes.set(projectId, qr);
            statuses.set(projectId, 'Initializing');
            sessionErrors.delete(projectId);
            reconnectAttempts.set(projectId, 0);
        }

        if (connection === 'close') {
            const disconnectStatusCode = lastDisconnect?.error?.output?.statusCode;
            const shouldReconnect = disconnectStatusCode !== DisconnectReason.loggedOut;
            const errorMessage = lastDisconnect?.error?.message || 'WhatsApp connection closed before pairing completed';
            const attempts = (reconnectAttempts.get(projectId) || 0) + 1;
            reconnectAttempts.set(projectId, attempts);
            
            const isPaired = hasCredentials(projectId);
            const maxAttempts = isPaired ? 1000 : MAX_RECONNECT_ATTEMPTS;
            const delay = Math.min(RECONNECT_DELAY_MS * Math.pow(2, attempts - 1), 60000);
            
            console.log(`Connection closed for project ${projectId}. Reconnecting: ${shouldReconnect}. Attempt: ${attempts}/${maxAttempts}. Next retry in ${delay}ms. Reason: ${errorMessage}`);
            
            sessionErrors.set(projectId, errorMessage);
            
            if (shouldReconnect && sessions.has(projectId) && attempts < maxAttempts) {
                sessions.delete(projectId);
                statuses.set(projectId, 'Initializing');
                const timer = setTimeout(() => {
                    reconnectTimers.delete(projectId);
                    startSession(projectId).catch((err) => {
                        sessionErrors.set(projectId, err.message);
                        statuses.set(projectId, 'Disconnected');
                    });
                }, delay);
                reconnectTimers.set(projectId, timer);
            } else {
                sessions.delete(projectId);
                qrCodes.delete(projectId);
                statuses.set(projectId, 'Disconnected');
                sessionErrors.set(
                    projectId,
                    shouldReconnect
                        ? `${errorMessage}. Unable to generate a WhatsApp QR code after ${attempts} attempts.`
                        : errorMessage
                );
                if (!shouldReconnect) {
                    try {
                        fs.rmSync(authDir, { recursive: true, force: true });
                        console.log(`Cleaned up credentials directory for project ${projectId} because the session was logged out.`);
                    } catch (e) {
                        console.error('Failed to clean auth files', e);
                    }
                } else {
                    console.log(`Retaining credentials directory for project ${projectId} to allow reconnection later.`);
                }
            }
        } else if (connection === 'open') {
            console.log(`Connection opened successfully for project ${projectId}`);
            statuses.set(projectId, 'Connected');
            qrCodes.delete(projectId);
            sessionErrors.delete(projectId);
            reconnectAttempts.set(projectId, 0);
        }
    });

    sock.ev.on('creds.update', saveCreds);

    sock.ev.on('messages.upsert', async (m) => {
        if (m.type === 'notify') {
            for (const msg of m.messages) {
                if (!msg.key.fromMe && msg.message) {
                    console.log(`[baileys-manager] msg.key: ${JSON.stringify(msg.key)}`);
                    console.log(`[baileys-manager] full msg keys: ${Object.keys(msg).join(', ')}`);
                    if (msg.key.participant) console.log(`[baileys-manager] msg.key.participant: ${msg.key.participant}`);
                    
                    // Mark message as read/seen immediately
                    try {
                        await sock.readMessages([msg.key]);
                        console.log(`[baileys-manager] Marked message ${msg.key.id} from ${msg.key.remoteJid} as read/seen.`);
                    } catch (readErr) {
                        console.error(`[baileys-manager] Failed to mark message ${msg.key.id} as read: ${readErr.message}`);
                    }

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

                    let assetId = null;
                    if (messageType === 'Image') {
                        assetId = await downloadAndUploadMedia(projectId, msg.key, mInfo, 'image');
                    } else if (messageType === 'Voice') {
                        assetId = await downloadAndUploadMedia(projectId, msg.key, mInfo, 'audio');
                    }

                    const timestamp = msg.messageTimestamp;

                    console.log(`Forwarding message from ${sender} (type=${messageType}) to backend webhook: "${content.substring(0, 50)}..."`);
                    try {
                        await axios.post(`${BACKEND_URL}/api/webhooks/whatsapp/message`, {
                            projectId,
                            messageId: msg.key.id,
                            sender,
                            senderJid,
                            senderLid,
                            name: msg.pushName || '',
                            content,
                            messageType,
                            timestamp,
                            assetId
                        });
                    } catch (err) {
                        console.error(`Failed to forward message from ${sender}: ${err.message}`);
                    }
                }
            }
        }
    });

    return { status: 'Initializing', message: 'Session started. Waiting for a scannable QR code.' };
}

export function getQR(projectId) {
    return qrCodes.get(projectId) || null;
}

export function getStatus(projectId) {
    const status = statuses.get(projectId) || 'Disconnected';
    const sock = sessions.get(projectId);
    const phoneNumber = sock?.user?.id?.split(':')[0] || null;
    const error = sessionErrors.get(projectId) || null;
    return { status, phoneNumber, error };
}

export async function sendMessage(projectId, to, text, buttons) {
    const sock = sessions.get(projectId);
    console.log(`[baileys-manager] sendMessage request: projectId=${projectId}, to=${to}, text="${text}", buttons=${JSON.stringify(buttons || [])}, isMock=${sock ? !!sock.isMock : 'no sock'}`);
    
    if (!sock || statuses.get(projectId) !== 'Connected') {
        const currentStatus = statuses.get(projectId) || 'Disconnected';
        console.warn(`[baileys-manager] Session not connected (status: ${currentStatus}). Falling back to mock send for testing.`);
        
        if (sock && sock.isMock) {
            let messageContent = { text };
            return await sock.sendMessage(to + '@s.whatsapp.net', messageContent);
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

    try {
        console.log(`[baileys-manager] Attempting sock.sendMessage to ${jid}...`);
        
        let messagePayload = { text };

        const sent = await sock.sendMessage(jid, messagePayload);
        const messageId = sent?.key?.id || `msg_${Math.random().toString(36).substring(7)}`;
        console.log(`[baileys-manager] sock.sendMessage success. returned messageId=${messageId}`);
        return { messageId, status: 'Sent' };
    } catch (err) {
        console.error(`[baileys-manager] sock.sendMessage failed to ${jid}. error=${err.message}`, err);
        throw new Error(`Failed to send WhatsApp message to ${jid}: ${err.message}`);
    }
}

export async function sendReaction(projectId, to, reactionText, targetMessageId, targetFromMe) {
    const sock = sessions.get(projectId);
    console.log(`[baileys-manager] sendReaction request: projectId=${projectId}, to=${to}, reactionText="${reactionText}", targetMessageId=${targetMessageId}, targetFromMe=${targetFromMe}, isMock=${sock ? !!sock.isMock : 'no sock'}`);
    
    if (!sock || statuses.get(projectId) !== 'Connected') {
        const currentStatus = statuses.get(projectId) || 'Disconnected';
        console.warn(`[baileys-manager] Session not connected (status: ${currentStatus}). Falling back to mock react for testing.`);
        
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
            return sent?.key?.id || null;
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

    try {
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

        const sent = await sock.sendMessage(jid, reactionPayload);
        const messageId = sent?.key?.id || `msg_${Math.random().toString(36).substring(7)}`;
        console.log(`[baileys-manager] sock.sendMessage (reaction) success. returned messageId=${messageId}`);
        return messageId;
    } catch (err) {
        console.error(`[baileys-manager] sock.sendMessage (reaction) failed to ${jid}. error=${err.message}`, err);
        throw new Error(`Failed to send WhatsApp reaction to ${jid}: ${err.message}`);
    }
}

export async function disconnectSession(projectId) {
    const sock = sessions.get(projectId);
    const reconnectTimer = reconnectTimers.get(projectId);
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimers.delete(projectId);
    }
    sessions.delete(projectId);
    qrCodes.delete(projectId);
    sessionErrors.delete(projectId);
    reconnectAttempts.delete(projectId);
    statuses.set(projectId, 'Disconnected');

    if (sock) {
        try {
            sock.end();
        } catch (err) {
            console.error(`Error ending socket for project ${projectId}:`, err.message);
        }
    }

    let sessionsDir = '/app/sessions';
    try {
        if (!fs.existsSync(sessionsDir)) {
            sessionsDir = path.resolve('./sessions');
        }
    } catch (e) {
        sessionsDir = path.resolve('./sessions');
    }
    const authDir = path.join(sessionsDir, projectId);
    if (fs.existsSync(authDir)) {
        try {
            fs.rmSync(authDir, { recursive: true, force: true });
            console.log(`Cleaned up credentials directory for project ${projectId}`);
        } catch (e) {
            console.error(`Failed to clean auth files for project ${projectId}:`, e.message);
        }
    }
    return { status: 'Disconnected', message: 'Session disconnected and cleaned up.' };
}
