import assert from 'node:assert/strict';
import test from 'node:test';
import { inboundMediaAttachment, uploadInboundMedia } from '../src/inbound-media.js';

for (const [type, messageType, mimetype, fileName] of [
    ['document', 'Document', 'application/pdf', 'booking.pdf'],
    ['video', 'Video', 'video/mp4', 'session.mp4']
]) {
    test(`${type} persists original file bytes and retains its attachment classification`, async () => {
        const part = { mimetype, fileName, mediaKey: 'synthetic-key', directPath: '/synthetic-file' };
        const attachment = inboundMediaAttachment({ [`${type}Message`]: part }, 'provider-id');
        let uploadedFile;
        const assetId = await uploadInboundMedia(attachment, {
            download: async function* () { yield Buffer.from('original bytes'); },
            upload: async form => { uploadedFile = form.get('file'); return { data: { id: 'saved-asset' } }; }
        });
        assert.equal(assetId, 'saved-asset');
        assert.equal(attachment.messageType, messageType);
        assert.equal(uploadedFile.name, fileName);
        assert.equal(uploadedFile.type, mimetype);
        assert.equal(await uploadedFile.text(), 'original bytes');
    });
}
