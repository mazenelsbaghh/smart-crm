const bareJid = value => typeof value === 'string' ? value.replace(/:\d+@/, '@') : null;

async function findMember(socket, participants, phone) {
    const pn = `${phone}@s.whatsapp.net`;
    const direct = participants.find(p => [p.id, p.phoneNumber].some(id => bareJid(id) === pn));
    if (direct) return direct;
    const lid = await socket.signalRepository?.lidMapping?.getLIDForPN?.(pn);
    return lid ? participants.find(p => [p.id, p.lid].some(id => bareJid(id) === bareJid(lid))) : undefined;
}

async function metadata(socket, groupJid) {
    const result = await socket.groupMetadata(groupJid);
    if (result?.id !== groupJid || !Array.isArray(result.participants))
        throw new Error('Invalid group metadata');
    return result.participants;
}

export async function ensureGroupMembers(socket, groupJid, phones) {
    if (!socket || socket.isMock) throw new Error('Live WhatsApp connection required');
    let participants = await metadata(socket, groupJid);
    const results = [];
    for (const phone of new Set(phones)) {
        let member = await findMember(socket, participants, phone);
        if (!member) {
            await socket.groupParticipantsUpdate(groupJid, [`${phone}@s.whatsapp.net`], 'add');
            participants = await metadata(socket, groupJid);
            member = await findMember(socket, participants, phone);
        }
        if (!member) {
            results.push({ phone, status: 'add_failed' });
            continue;
        }
        results.push({ phone, status: 'present' });
    }
    return { complete: results.every(result => result.status === 'present'), results };
}
