import type { Metadata } from 'next';
import { AuthProvider } from '../context/auth-context';
import { ToastProvider } from '../context/toast-context';
import '@aejkatappaja/phantom-ui/ssr.css';
import './globals.css';

export const metadata: Metadata = {
  title: 'سمارت كاستمر - لوحة التحكم',
  description: 'منصة إدارة العملاء والردود الذكية عبر واتساب',
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ar" dir="rtl">
      <body suppressHydrationWarning>
        <AuthProvider>
          <ToastProvider>
            {children}
          </ToastProvider>
        </AuthProvider>
      </body>
    </html>
  );
}
