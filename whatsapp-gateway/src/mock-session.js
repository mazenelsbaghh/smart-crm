function freshConnectionEpoch(previousEpoch, now) {
    const currentTime = now();
    if (!previousEpoch) return new Date(currentTime).toISOString();
    return new Date(Math.max(currentTime, Date.parse(previousEpoch) + 1)).toISOString();
}

async function closeReplacedSocket(replacedSocket) {
    if (replacedSocket && !replacedSocket.isMock && typeof replacedSocket.end === 'function') {
        await replacedSocket.end(undefined);
    }
}

export async function replaceWithMockSession({
    key,
    status,
    mockSocket,
    sessions,
    statuses,
    connectionOpenedAt,
    now = Date.now
}) {
    const replacedSocket = sessions.get(key);
    const previousEpoch = connectionOpenedAt.get(key);
    sessions.delete(key);
    statuses.set(key, 'Disconnected');
    connectionOpenedAt.delete(key);
    await closeReplacedSocket(replacedSocket);

    statuses.set(key, status);
    if (status !== 'Connected') return null;
    sessions.set(key, mockSocket);
    const connectedAt = freshConnectionEpoch(previousEpoch, now);
    connectionOpenedAt.set(key, connectedAt);
    return connectedAt;
}
