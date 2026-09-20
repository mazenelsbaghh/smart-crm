const mediaFormats = [
    ['image', 'image/jpeg', 'jpg', 'Image'],
    ['audio', 'audio/ogg', 'ogg', 'Voice'],
    ['video', 'video/mp4', 'mp4', 'Video'],
    ['document', 'application/octet-stream', 'bin', 'Document']
];

export function inboundMediaAttachment(message, messageId) {
    for (const [type, defaultContentType, extension, messageType] of mediaFormats) {
        const part = message[`${type}Message`];
        if (!part) continue;
        const fileName = part.fileName?.split(/[\\/]/).pop() || `media_${messageId}.${extension}`;
        return { type, messageType, part, fileName, contentType: part.mimetype || defaultContentType };
    }
    return null;
}

export async function uploadInboundMedia(attachment, mediaTransport) {
    const chunks = [];
    const stream = await mediaTransport.download(attachment.part, attachment.type);
    for await (const chunk of stream) chunks.push(chunk);
    const form = new FormData();
    form.append('file', new Blob(chunks, { type: attachment.contentType }), attachment.fileName);
    const response = await mediaTransport.upload(form);
    if (typeof response.data?.id !== 'string' || !response.data.id)
        throw new Error('Media upload returned no asset ID');
    return response.data.id;
}
