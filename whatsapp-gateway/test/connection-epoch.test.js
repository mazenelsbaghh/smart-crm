import assert from 'node:assert/strict';
import test from 'node:test';
import { validateExpectedConnectionEpoch } from '../src/connection-epoch.js';

test('accepts only the connection epoch that is still active', () => {
    const current = '2026-09-01T20:35:43.123Z';

    assert.equal(validateExpectedConnectionEpoch(undefined, current, true), 'not-required');
    assert.equal(validateExpectedConnectionEpoch('not-a-date', current, true), 'invalid');
    assert.equal(validateExpectedConnectionEpoch('2026-09-01T23:35:43.123+03:00', current, true), 'current');
    assert.equal(validateExpectedConnectionEpoch('2026-09-01T20:30:00.000Z', current, true), 'stale');
    assert.equal(validateExpectedConnectionEpoch(current, current, false), 'stale');
});
