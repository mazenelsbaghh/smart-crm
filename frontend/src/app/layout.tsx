import type { Metadata } from 'next';
import { AuthProvider } from '../context/auth-context';
import './globals.css';

export const metadata: Metadata = {
  title: 'Smart WhatsApp - Dashboard',
  description: 'Smart WhatsApp Customer Core Web Portal',
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body>
        <AuthProvider>
          {children}
        </AuthProvider>
      </body>
    </html>
  );
}
