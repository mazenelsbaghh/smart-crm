async function claimCommand(idempotencyStore, identity, idempotencyKey) {
    if (!idempotencyKey) return { state: 'ready', claim: null };

    try {
        const claim = await idempotencyStore.claim(
            identity.projectId,
            identity.whatsappAccountId,
            idempotencyKey);
        if (claim.result) return { state: 'replayed', providerResponse: claim.result };
        if (!claim.claimed) return { state: 'processing' };
        return { state: 'ready', claim };
    } catch (error) {
        return { state: 'storage-unavailable', error };
    }
}

async function releaseClaim(idempotencyStore, claim) {
    if (!claim?.claimed) return null;
    try {
        await idempotencyStore.release(claim.key);
        return null;
    } catch (error) {
        return error;
    }
}

async function dispatchClaimedCommand(idempotencyStore, preparation, dispatch) {
    try {
        const providerResponse = await dispatch();
        if (preparation.claim) {
            await idempotencyStore.complete(preparation.claim.key, providerResponse);
        }
        return { state: 'completed', providerResponse };
    } catch (error) {
        const releaseError = error?.definitelyNotSent === true
            ? await releaseClaim(idempotencyStore, preparation.claim)
            : null;
        return { state: 'failed', error, releaseError };
    }
}

export async function executeIdempotentSessionCommand({
    idempotencyStore,
    identity,
    idempotencyKey,
    validateConnection,
    dispatch
}) {
    if (validateConnection() === 'invalid') return { state: 'invalid-epoch' };

    const preparation = await claimCommand(idempotencyStore, identity, idempotencyKey);
    if (preparation.state !== 'ready') return preparation;

    if (validateConnection() === 'stale') {
        const releaseError = await releaseClaim(idempotencyStore, preparation.claim);
        return releaseError
            ? { state: 'storage-unavailable', error: releaseError }
            : { state: 'stale-epoch' };
    }
    return dispatchClaimedCommand(idempotencyStore, preparation, dispatch);
}
