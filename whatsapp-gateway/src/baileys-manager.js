import makeWASocket, { useMultiFileAuthState, DisconnectReason } from '@whiskeysockets/baileys';
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
    
    const sessionsDir = path.resolve('/app/sessions');
    if (!fs.existsSync(sessionsDir)) {
        fs.mkdirSync(sessionsDir, { recursive: true });
    }
    
    const authDir = path.join(sessionsDir, projectId);
    const { state, saveCreds } = await useMultiFileAuthState(authDir);

    const sock = (makeWASocket.default || makeWASocket)({
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
                    const sender = msg.key.remoteJid.split('@')[0];
                    const content = msg.message.conversation || msg.message.extendedTextMessage?.text || '';
                    const messageType = msg.message.imageMessage ? 'Image' : msg.message.audioMessage ? 'Voice' : 'Text';
                    const timestamp = msg.messageTimestamp;

                    console.log(`Forwarding message from ${sender} to backend webhook...`);
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
                        console.error(`Failed to forward message: ${err.message}`);
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
    if (!sock || statuses.get(projectId) !== 'Connected') {
        throw new Error('WhatsApp session is not active or connected');
    }

    const jid = `${to}@s.whatsapp.net`;
    const sent = await sock.sendMessage(jid, { text });
    return { messageId: sent.key.id, status: 'Sent' };
}
