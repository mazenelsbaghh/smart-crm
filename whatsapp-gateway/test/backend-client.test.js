import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import test from 'node:test';
import { createBackendClient } from '../src/backend-client.js';

const syntheticSecret = 'gateway-test-secret';

test('gateway authenticates both inbound-message and media-upload requests', async (t) => {
    const requests = [];
    const server = createServer((request, response) => {
        const chunks = [];
        request.on('data', chunk => chunks.push(chunk));
        request.on('end', () => {
            requests.push({
                path: request.url,
                secret: request.headers['x-whatsapp-gateway-secret'],
                body: Buffer.concat(chunks).toString('utf8')
            });
            response.writeHead(200, { 'Content-Type': 'application/json' });
            response.end('{"id":"synthetic-asset"}');
        });
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    t.after(() => server.close());

    const address = server.address();
    const client = createBackendClient({
        backendUrl: `http://127.0.0.1:${address.port}`,
        webhookSecret: syntheticSecret
    });
    await client.forwardMessage({
        projectId: '00000000-0000-0000-0000-000000000001',
        sender: '201000000001',
        content: 'synthetic message'
    });
    const form = new FormData();
    form.append('file', new Blob(['synthetic media'], { type: 'image/jpeg' }), 'media.jpg');
    await client.uploadMedia('00000000-0000-0000-0000-000000000001', form);

    assert.deepEqual(requests.map(request => request.path), [
        '/api/webhooks/whatsapp/message',
        '/api/projects/00000000-0000-0000-0000-000000000001/assets/upload'
    ]);
    assert.deepEqual(requests.map(request => request.secret), [syntheticSecret, syntheticSecret]);
    assert.match(requests[0].body, /synthetic message/);
    assert.match(requests[1].body, /synthetic media/);
});

test('gateway refuses to start a backend client without the shared secret', () => {
    assert.throws(
        () => createBackendClient({ backendUrl: 'http://backend:5000', webhookSecret: '  ' }),
        /WHATSAPP_GATEWAY_WEBHOOK_SECRET is required/);
});
