function phoneFromJid(jid) {
    return typeof jid === 'string'
        ? jid.match(/^([1-9]\d{6,14})(?::\d+)?@s\.whatsapp\.net$/)?.[1] ?? null
        : null;
}

export async function readGroupMembership(socket, groupJid, phones, timeoutMs = 8000) {
    if (!socket || socket.isMock || typeof socket.groupMetadata !== 'function')
        throw new Error('Group membership unavailable');
    let timer;
    try {
        return await Promise.race([
            readMembers(socket, groupJid, phones),
            new Promise((_, reject) => {
                timer = setTimeout(() => reject(new Error('Group membership timed out')), timeoutMs);
            })
        ]);
    } finally {
        clearTimeout(timer);
    }
}

async function readMembers(socket, groupJid, phones) {
    const metadata = await socket.groupMetadata(groupJid);
    if (metadata?.id !== groupJid || !Array.isArray(metadata.participants))
        throw new Error('Invalid group membership response');
    const knownPhones = new Set();
    const knownLids = new Set();
    let complete = metadata.participants.length > 0
        && (metadata.size == null || metadata.size === metadata.participants.length);
    let identitiesComplete = complete;
    for (const member of metadata.participants) {
        let phone = phoneFromJid(member?.phoneNumber) ?? phoneFromJid(member?.id);
        const lid = [member?.id, member?.lid].find(value => typeof value === 'string' && /^\d+@lid$/.test(value));
        if (lid) knownLids.add(lid);
        if (!phone && lid && socket.signalRepository?.lidMapping?.getPNForLID) {
            phone = phoneFromJid(await socket.signalRepository.lidMapping.getPNForLID(lid));
        }
        if (phone) knownPhones.add(phone);
        else complete = false;
        if (!phone && !lid) identitiesComplete = false;
    }
    const members = [];
    for (const phone of new Set(phones)) {
        let joined = knownPhones.has(phone);
        let mappedLid = null;
        if (!joined && knownLids.size && socket.signalRepository?.lidMapping?.getLIDForPN) {
            mappedLid = await socket.signalRepository.lidMapping.getLIDForPN(`${phone}@s.whatsapp.net`);
            joined = knownLids.has(mappedLid);
        }
        const absenceConfirmed = complete || (identitiesComplete && typeof mappedLid === 'string' && /^\d+@lid$/.test(mappedLid));
        members.push({ phone, status: joined ? 'joined' : absenceConfirmed ? 'not_joined' : 'unknown' });
    }
    return { checkedAtUtc: new Date().toISOString(), members };
}
