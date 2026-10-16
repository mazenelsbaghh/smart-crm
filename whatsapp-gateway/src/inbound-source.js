export function isNonCustomerChat(jid) {
    const normalized = jid.toLowerCase();
    return ['@newsletter', '@broadcast', '@g.us'].some(suffix => normalized.endsWith(suffix));
}
