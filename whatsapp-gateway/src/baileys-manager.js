import makeWASocket, { useMultiFileAuthState, DisconnectReason, fetchLatestWaWebVersion } from '@itsukichan/baileys';
import path from 'path';
import fs from 'fs';
import axios from 'axios';

export const sessions = new Map();
export const qrCodes = new Map();
export const statuses = new Map();

const BACKEND_URL = process.env.BACKEND_URL || 'http://backend:5000';

export async function startSession(projectId) {
    if (sessions.has(projectId)) {
        return { status: statuses.get(projectId) || 'Initializing', message: 'Session already active/initializing' };
    }

    statuses.set(projectId, 'Initializing');
    
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
        const { version: latestVersion } = await fetchLatestWaWebVersion();
        version = latestVersion;
        console.log(`Using fetched WA Web version: ${version.join('.')}`);
    } catch (err) {
        console.warn('Failed to fetch latest WA Web version, using fallback:', err.message);
    }

    const sock = (makeWASocket.default || makeWASocket)({
        version,
        auth: state,
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
            qrCodes.set(projectId, qr);
            statuses.set(projectId, 'Initializing');
        }

        if (connection === 'close') {
            const shouldReconnect = lastDisconnect?.error?.output?.statusCode !== DisconnectReason.loggedOut;
            console.log(`Connection closed for project ${projectId}. Reconnecting: ${shouldReconnect}`);
            
            qrCodes.delete(projectId);
            statuses.set(projectId, 'Disconnected');
            
            if (shouldReconnect) {
                sessions.delete(projectId);
                setTimeout(() => startSession(projectId), 5000);
            } else {
                sessions.delete(projectId);
                try {
                    fs.rmSync(authDir, { recursive: true, force: true });
                } catch (e) {
                    console.error('Failed to clean auth files', e);
                }
            }
        } else if (connection === 'open') {
            console.log(`Connection opened successfully for project ${projectId}`);
            statuses.set(projectId, 'Connected');
            qrCodes.delete(projectId);
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
                    
                    let mInfo = msg.message;
                    // Unwrap ephemeral or view once wrapper types
                    if (mInfo.ephemeralMessage) mInfo = mInfo.ephemeralMessage.message;
                    if (mInfo.viewOnceMessage) mInfo = mInfo.viewOnceMessage.message;
                    if (mInfo.viewOnceMessageV2) mInfo = mInfo.viewOnceMessageV2.message;

                    if (!mInfo) continue;

                    const sender = msg.key.remoteJid.endsWith('@s.whatsapp.net')
                        ? msg.key.remoteJid.split('@')[0]
                        : msg.key.remoteJid;
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
                    } else {
                        // Fallback text extraction
                        content = mInfo.conversation || '';
                    }

                    const timestamp = msg.messageTimestamp;

                    console.log(`Forwarding message from ${sender} (type=${messageType}) to backend webhook: "${content.substring(0, 50)}..."`);
                    try {
                        await axios.post(`${BACKEND_URL}/api/webhooks/whatsapp/message`, {
                            projectId,
                            messageId: msg.key.id,
                            sender,
                            content,
                            messageType,
                            timestamp
                        });
                    } catch (err) {
                        console.error(`Failed to forward message from ${sender}: ${err.message}`);
                    }
                }
            }
        }
    });

    return { status: 'Initializing', message: 'Session started. Scannable QR code generated.' };
}

export function getQR(projectId) {
    return qrCodes.get(projectId) || null;
}

export function getStatus(projectId) {
    const status = statuses.get(projectId) || 'Disconnected';
    const sock = sessions.get(projectId);
    const phoneNumber = sock?.user?.id?.split(':')[0] || null;
    return { status, phoneNumber };
}

export async function sendMessage(projectId, to, text) {
    const sock = sessions.get(projectId);
    console.log(`[baileys-manager] sendMessage request: projectId=${projectId}, to=${to}, text="${text}", isMock=${sock ? !!sock.isMock : 'no sock'}`);
    
    if (!sock || statuses.get(projectId) !== 'Connected') {
        const currentStatus = statuses.get(projectId) || 'Disconnected';
        console.error(`[baileys-manager] Cannot send: sock exists=${!!sock}, status=${currentStatus}`);
        throw new Error(`WhatsApp session is not active or connected (current status: ${currentStatus})`);
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
        const sent = await sock.sendMessage(jid, { text });
        const messageId = sent?.key?.id || `msg_${Math.random().toString(36).substring(7)}`;
        console.log(`[baileys-manager] sock.sendMessage success. returned messageId=${messageId}`);
        return { messageId, status: 'Sent' };
    } catch (err) {
        console.error(`[baileys-manager] sock.sendMessage failed to ${jid}. error=${err.message}`, err);
        throw new Error(`Failed to send WhatsApp message to ${jid}: ${err.message}`);
    }
}
