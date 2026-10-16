import type { Metadata, Viewport } from 'next';
import localFont from 'next/font/local';
import { ThemeProvider } from '../context/theme-context';
import { AuthProvider } from '../context/auth-context';
import { ToastProvider } from '../context/toast-context';
import '@aejkatappaja/phantom-ui/ssr.css';
import './globals.css';

const arabicFont = localFont({
  src: [
    { path: './fonts/IBMPlexSansArabic-Regular.ttf', weight: '400' },
    { path: './fonts/IBMPlexSansArabic-Medium.ttf', weight: '500' },
    { path: './fonts/IBMPlexSansArabic-SemiBold.ttf', weight: '600' },
    { path: './fonts/IBMPlexSansArabic-Bold.ttf', weight: '700' },
  ],
  variable: '--font-arabic',
  display: 'swap',
});

export const viewport: Viewport = { width: 'device-width', initialScale: 1, viewportFit: 'cover', interactiveWidget: 'resizes-content' };

export const metadata: Metadata = {
  title: 'سمارت كاستمر - لوحة التحكم',
  description: 'منصة إدارة المبيعات والعملاء والردود الذكية',
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ar" dir="rtl" className={arabicFont.variable} suppressHydrationWarning>
      <body suppressHydrationWarning>
        <ThemeProvider>
          <AuthProvider>
            <ToastProvider>
              {children}
            </ToastProvider>
          </AuthProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
