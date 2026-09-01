export function authErrorMessage(error: unknown, fallback: string) {
  if (typeof error !== 'object' || error === null) return fallback;
  if (error instanceof Error) {
    if (error.message === 'NO_AUTHORIZED_PROJECT') {
      return 'الحساب صحيح، لكن لا توجد مساحة عمل مرتبطة به. تواصل مع المدير.';
    }
    if (error.message === 'INVALID_LOGIN_RESPONSE') {
      return 'تعذر تأكيد جلسة الدخول من الخادم. أعد المحاولة، ثم تواصل مع الدعم إذا استمرت المشكلة.';
    }
  }

  const authenticationError = error as { response?: { status?: number; data?: { error?: unknown; message?: unknown } } };
  if (authenticationError.response?.status === 401) return fallback;
  const serverMessage = authenticationError.response?.data?.error ?? authenticationError.response?.data?.message;
  return typeof serverMessage === 'string' && serverMessage.trim() ? serverMessage : fallback;
}
