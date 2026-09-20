import assert from 'node:assert/strict';
import test from 'node:test';
import { shouldCaptureInboundUpsert } from '../src/inbound-upsert-policy.js';

const connectedAt = '2026-09-01T19:37:10.000Z';

test('live notifications are always captured', () => {
    assert.equal(shouldCaptureInboundUpsert('notify', 0, null), true);
});

test('a recent append from a reconnect gap is captured', () => {
    assert.equal(shouldCaptureInboundUpsert(
        'append',
        { low: 1_788_291_360, high: 0, unsigned: true },
        connectedAt), true);
});

test('old history and unsupported upsert types are ignored', () => {
    assert.equal(shouldCaptureInboundUpsert('append', 1_788_287_000, connectedAt), false);
    assert.equal(shouldCaptureInboundUpsert('replace', 1_788_291_360, connectedAt), false);
});
