import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'أكمل الآية للقرآن الكريم',
  description: 'استوديو إعداد ونشر تحديات أكمل الآية للقرآن الكريم.',
  icons: {
    icon: [{ url: '/quran-challenge/icon.png', type: 'image/png', sizes: '1024x1024' }],
    shortcut: '/quran-challenge/icon.png',
    apple: '/quran-challenge/icon.png',
  },
};

export default function QuranChallengeLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return children;
}
