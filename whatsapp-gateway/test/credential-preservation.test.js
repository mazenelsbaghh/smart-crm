import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
    resolveSessionIdentity,
    sessionAuthDirectory
} from '../src/session-identity.js';

test('credential check during a truncated-file save preserves the active writer’s credentials', async context => {
    const originalDirectory = process.cwd();
    const originalWebhookSecret = process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET;
    const isolatedDirectory = fs.mkdtempSync(path.join(os.tmpdir(), 'wa-credential-preservation-'));
    process.chdir(isolatedDirectory);
    process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET = 'credential-preservation-test-secret';
    context.after(() => {
        process.chdir(originalDirectory);
        if (originalWebhookSecret === undefined) delete process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET;
        else process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET = originalWebhookSecret;
        fs.rmSync(isolatedDirectory, { recursive: true, force: true });
    });

    const { hasCredentials } = await import('../src/baileys-manager.js');
    const identity = resolveSessionIdentity('project-1', 'account-a');
    const credentialsDirectory = sessionAuthDirectory(path.join(isolatedDirectory, 'sessions'), identity);
    fs.mkdirSync(credentialsDirectory, { recursive: true });
    const credentialsFile = path.join(credentialsDirectory, 'creds.json');
    fs.writeFileSync(credentialsFile, JSON.stringify({ registered: true, nextPreKeyId: 1 }));

    // Reproduce the truncation window found during the 2026-10-16 disconnect investigation.
    const writer = fs.openSync(credentialsFile, 'w');
    try {
        assert.equal(hasCredentials(identity.projectId, identity.whatsappAccountId), false);
        assert.equal(fs.existsSync(credentialsFile), true);

        const savedCredentials = { registered: true, nextPreKeyId: 2 };
        fs.writeFileSync(writer, JSON.stringify(savedCredentials));
        assert.deepEqual(JSON.parse(fs.readFileSync(credentialsFile, 'utf8')), savedCredentials);
        assert.equal(hasCredentials(identity.projectId, identity.whatsappAccountId), true);
    } finally {
        fs.closeSync(writer);
    }
});
