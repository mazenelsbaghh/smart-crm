export async function verifyWhatsAppNumber(socket, phone) {
    if (!/^[1-9]\d{7,14}$/.test(phone)) throw new Error('Invalid canonical phone number');
    if (!socket || socket.isMock || typeof socket.onWhatsApp !== 'function')
        throw new Error('WhatsApp number verification is unavailable');
    let timer;
    try {
        const results = await Promise.race([
            socket.onWhatsApp(phone),
            new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('WhatsApp verification timed out')), 15000); })
        ]);
        if (!Array.isArray(results) || results.some(result => typeof result?.exists !== 'boolean'))
            throw new Error('WhatsApp verification returned no reliable result');
        return results.some(result => result.exists === true);
    } finally {
        clearTimeout(timer);
    }
}
