export function validateExpectedConnectionEpoch(expected, current, isConnected) {
    if (expected === undefined || expected === null) return 'not-required';
    if (typeof expected !== 'string') return 'invalid';

    const expectedTimestamp = Date.parse(expected);
    if (Number.isNaN(expectedTimestamp)) return 'invalid';
    if (!isConnected || typeof current !== 'string') return 'stale';

    const currentTimestamp = Date.parse(current);
    return !Number.isNaN(currentTimestamp) && currentTimestamp === expectedTimestamp
        ? 'current'
        : 'stale';
}
