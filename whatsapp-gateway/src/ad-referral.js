const WRAPPERS = ['ephemeralMessage', 'viewOnceMessage', 'viewOnceMessageV2', 'documentWithCaptionMessage'];

function unwrap(message) {
    let current = message;
    for (let depth = 0; depth < 5 && current; depth += 1) {
        const wrapper = WRAPPERS.find((name) => current[name]?.message);
        if (!wrapper) break;
        current = current[wrapper].message;
    }
    return current || {};
}

function messageParts(message) {
    const value = unwrap(message);
    return Object.values(value).filter((part) => part && typeof part === 'object');
}

export function extractAdvertisingReferral(message) {
    const parts = messageParts(message);
    const contexts = parts.map((part) => part.contextInfo).filter(Boolean);
    const replies = contexts.map((context) => context.externalAdReply).filter(Boolean);
    const documented = replies.find((reply) => typeof reply.ctwaClid === 'string' && reply.ctwaClid.trim());
    if (documented) {
        return {
            identifierState: 'CtwaClid',
            ctwaClid: documented.ctwaClid.trim(),
            providerAdId: typeof documented.sourceId === 'string' ? documented.sourceId : null,
            opaqueMarker: false
        };
    }

    const opaqueMarker = replies.some((reply) => reply.ctwaPayload != null || reply.conversionData != null)
        || contexts.some((context) => context.conversionData != null || context.ctwaPayload != null)
        || parts.some((part) => part.conversionData != null || part.ctwaPayload != null);
    return {
        identifierState: opaqueMarker ? 'OpaquePayloadOnly' : 'Missing',
        ctwaClid: null,
        providerAdId: null,
        opaqueMarker
    };
}
