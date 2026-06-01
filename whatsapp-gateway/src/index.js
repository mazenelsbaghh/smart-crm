import express from 'express';
import dns from 'dns';
import fs from 'fs';
import path from 'path';
import { startSession, getQR, getStatus, sendMessage, disconnectSession, statuses, sessions, sessionErrors } from './baileys-manager.js';

dns.setDefaultResultOrder('ipv4first');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 3000;

app.post('/api/whatsapp/session/start', async (req, res) => {
    const { projectId } = req.body;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    try {
        const result = await startSession(projectId);
        res.json(result);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

app.post('/api/whatsapp/session/disconnect', async (req, res) => {
    const { projectId } = req.body;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    try {
        const result = await disconnectSession(projectId);
        res.json(result);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

app.get('/api/whatsapp/session/qr', (req, res) => {
    const { projectId } = req.query;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    const qr = getQR(projectId);
    if (!qr) {
        return res.status(404).json({
            error: sessionErrors.get(projectId) || 'QR code not ready or session already connected'
        });
    }
    res.json({ qr });
});

app.get('/api/whatsapp/session/status', (req, res) => {
    const { projectId } = req.query;
    if (!projectId) {
        return res.status(400).json({ error: 'projectId is required' });
    }
    const status = getStatus(projectId);
    res.json({ projectId, ...status });
});

app.post('/api/whatsapp/send', async (req, res) => {
    const { projectId, to, message } = req.body;
    console.log(`[GATEWAY SEND] Request received. projectId: ${projectId}, to: ${to}, message: ${message}`);
    console.log(`[GATEWAY SEND] Available sessions: ${Array.from(sessions.keys()).join(', ')}`);
    console.log(`[GATEWAY SEND] Available statuses: ${Array.from(statuses.entries()).map(([k, v]) => `${k}=${v}`).join(', ')}`);
    if (!projectId || !to || !message) {
        return res.status(400).json({ error: 'projectId, to, and message are required' });
    }
    try {
        const result = await sendMessage(projectId, to, message);
        res.json(result);
    } catch (err) {
        console.error(`[GATEWAY SEND] Error sending message: ${err.message}`);
        res.status(500).json({ error: err.message });
    }
});

const mockSentMessages = [];

// Mock connection endpoint for integration testing
app.post('/api/whatsapp/session/mock', (req, res) => {
    const { projectId, status, phoneNumber } = req.body;
    console.log(`[MOCK SESSION] Request received. projectId: ${projectId}, status: ${status}, phoneNumber: ${phoneNumber}`);
    if (!projectId || !status) {
        return res.status(400).json({ error: 'projectId and status are required' });
    }
    
    statuses.set(projectId, status);
    
    if (status === 'Connected') {
        sessions.set(projectId, {
            isMock: true,
            user: { id: phoneNumber ? `${phoneNumber}:1` : '1234567890:1' },
            sendMessage: async (jid, content) => {
                console.log(`[MOCK SEND] Sending to ${jid}: ${content.text}`);
                const messageId = `msg_${Math.random().toString(36).substring(7)}`;
                mockSentMessages.push({
                    projectId,
                    to: jid.split('@')[0],
                    message: content.text,
                    messageId,
                    timestamp: new Date().toISOString()
                });
                return { key: { id: messageId } };
            }
        });
    } else {
        sessions.delete(projectId);
    }
    
    console.log(`[MOCK SESSION] Current sessions key count: ${sessions.size}`);
    res.json({ message: `Mocked session status of ${projectId} to ${status}` });
});

app.get('/api/whatsapp/mock/sent', (req, res) => {
    res.json(mockSentMessages);
});

app.post('/api/whatsapp/mock/clear', (req, res) => {
    mockSentMessages.length = 0;
    res.json({ message: 'Mock sent messages cleared' });
});

app.listen(PORT, async () => {
    console.log(`WhatsApp Gateway listening on port ${PORT}`);
    
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
        if (fs.existsSync(sessionsDir)) {
            const files = fs.readdirSync(sessionsDir);
            for (const file of files) {
                const fullPath = path.join(sessionsDir, file);
                if (fs.statSync(fullPath).isDirectory()) {
                    // Check if there are credentials in it (e.g. creds.json)
                    const credsFile = path.join(fullPath, 'creds.json');
                    if (fs.existsSync(credsFile)) {
                        console.log(`[GATEWAY STARTUP] Found existing session directory for project: ${file}. Restoring connection...`);
                        startSession(file).catch(err => {
                            console.error(`[GATEWAY STARTUP] Failed to restore session for project ${file}: ${err.message}`);
                        });
                    }
                }
            }
        }
    } catch (err) {
        console.error('[GATEWAY STARTUP] Error scanning sessions directory:', err.message);
    }
});
