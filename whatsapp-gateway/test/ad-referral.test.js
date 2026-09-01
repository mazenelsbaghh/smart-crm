import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { extractAdvertisingReferral } from '../src/ad-referral.js';

const fixtureRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'advertising');
async function fixture(name) {
    return JSON.parse(await readFile(path.join(fixtureRoot, name), 'utf8'));
}

test('captures only the documented optional ctwaClid and provider source id', async () => {
    assert.deepEqual(extractAdvertisingReferral(await fixture('present-referral.json')), {
        identifierState: 'CtwaClid', ctwaClid: 'ARAb-test-click-id',
        providerAdId: '238500000000001', opaqueMarker: false
    });
});

test('unwraps supported message wrappers without guessing referral values', async () => {
    assert.equal(extractAdvertisingReferral(await fixture('wrapped-referral.json')).ctwaClid, 'wrapped-click-id');
});

test('marks opaque payloads without decoding or forwarding their value', async () => {
    const result = extractAdvertisingReferral(await fixture('opaque-marker.json'));
    assert.equal(result.identifierState, 'OpaquePayloadOnly');
    assert.equal(result.ctwaClid, null);
    assert.equal(JSON.stringify(result).includes('opaque-undocumented-value'), false);
    assert.equal(JSON.stringify(result).includes('opaque-conversion-value'), false);
});

test('keeps ordinary inbound messages in the missing denominator', async () => {
    assert.deepEqual(extractAdvertisingReferral(await fixture('missing-referral.json')), {
        identifierState: 'Missing', ctwaClid: null, providerAdId: null, opaqueMarker: false
    });
});
