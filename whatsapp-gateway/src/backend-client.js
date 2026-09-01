import axios from 'axios';

const AUTH_HEADER = 'X-WhatsApp-Gateway-Secret';

export function createBackendClient({
    backendUrl = process.env.BACKEND_URL || 'http://backend:5000',
    webhookSecret = process.env.WHATSAPP_GATEWAY_WEBHOOK_SECRET || ''
} = {}) {
    const normalizedSecret = webhookSecret.trim();
    if (!normalizedSecret) {
        throw new Error('WHATSAPP_GATEWAY_WEBHOOK_SECRET is required');
    }

    const requestConfig = {
        headers: { [AUTH_HEADER]: normalizedSecret }
    };

    return Object.freeze({
        uploadMedia(projectId, form) {
            return axios.post(
                `${backendUrl}/api/projects/${projectId}/assets/upload`,
                form,
                requestConfig);
        },
        forwardMessage(message) {
            return axios.post(
                `${backendUrl}/api/webhooks/whatsapp/message`,
                message,
                requestConfig);
        }
    });
}
