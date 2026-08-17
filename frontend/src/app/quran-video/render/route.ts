import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import sharp from 'sharp';

export const runtime = 'nodejs';
export const maxDuration = 300;

const WIDTH = 1080;
const HEIGHT = 1920;
const PAGE_SIZE = 7;

type QuranApiResponse = {
  data?: {
    text?: string;
    surah?: { name?: string };
  };
};

type VersePage = {
  words: string[];
  startIndex: number;
};

function escapeXml(value: string) {
  return value.replace(/[&<>"']/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;',
  })[character] ?? character);
}

function splitIntoPages(words: string[]): VersePage[] {
  const pages: VersePage[] = [];
  for (let startIndex = 0; startIndex < words.length; startIndex += PAGE_SIZE) {
    pages.push({ words: words.slice(startIndex, startIndex + PAGE_SIZE), startIndex });
  }
  return pages;
}

function wrapWords(words: string[], maxWordsPerLine = 4) {
  const lines: string[] = [];
  for (let index = 0; index < words.length; index += maxWordsPerLine) {
    lines.push(words.slice(index, index + maxWordsPerLine).join(' '));
  }
  return lines;
}

function textLines(lines: string[], startY: number, lineHeight: number, fontSize: number, color = '#f4ecd7') {
  return lines.map((line, index) => (
    `<text x="540" y="${startY + (index * lineHeight)}" text-anchor="middle" direction="rtl" unicode-bidi="bidi-override" fill="${color}" font-size="${fontSize}" font-weight="700">${escapeXml(line)}</text>`
  )).join('');
}

function frame(content: string, footer = '') {
  return `
    <svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}">
      <defs>
        <radialGradient id="bg" cx="50%" cy="38%" r="78%">
          <stop offset="0" stop-color="#315b3d"/><stop offset="0.58" stop-color="#173f31"/><stop offset="1" stop-color="#0d291f"/>
        </radialGradient>
      </defs>
      <rect width="1080" height="1920" fill="url(#bg)"/>
      <circle cx="1020" cy="570" r="400" fill="none" stroke="#d4ad54" stroke-opacity=".28" stroke-width="2"/>
      <circle cx="40" cy="1690" r="340" fill="none" stroke="#d4ad54" stroke-opacity=".2" stroke-width="2"/>
      <g font-family="Noto Sans Arabic, sans-serif">${content}</g>
      <text x="540" y="1825" text-anchor="middle" direction="rtl" fill="#f4ecd7" fill-opacity=".74" font-family="Noto Sans Arabic, sans-serif" font-size="30">${escapeXml(footer)}</text>
    </svg>`;
}

function introSvg() {
  return frame(`
    <text x="540" y="370" text-anchor="middle" direction="rtl" fill="#d8b75e" font-size="48" font-weight="700">مُسَابَقَةٌ قُرْآنِيَّةٌ</text>
    ${textLines(['اخْتَبِرْ نَفْسَكَ', 'ابْتِغَاءَ مَرْضَاةِ اللَّهِ'], 610, 115, 74)}
    <rect x="185" y="920" width="710" height="3" fill="#d8b75e" fill-opacity=".62"/>
    ${textLines(['صَلِّ عَلَى النَّبِيِّ ﷺ'], 1100, 100, 62, '#d8b75e')}
  `, 'أَكْمِلِ الْآيَةَ');
}

function outroSvg() {
  return frame(`
    <text x="540" y="500" text-anchor="middle" direction="rtl" fill="#d8b75e" font-size="62" font-weight="700">أَحْسَنْتَ</text>
    ${textLines(['صَلِّ عَلَى النَّبِيِّ ﷺ', 'وَلَا تَنْسَ الإِعْجَابَ وَالِاشْتِرَاكَ'], 760, 130, 58)}
  `, 'نَلْتَقِي فِي تَحَدٍّ جَدِيدٍ');
}

function questionSvg(page: VersePage, pageIndex: number, pageCount: number, hiddenIndex: number, options: string[], surah: string, ayah: number) {
  const displayedWords = page.words.map((word, localIndex) => (
    page.startIndex + localIndex === hiddenIndex ? 'ــــــــــــ' : word
  ));
  const lines = wrapWords(displayedWords);
  const optionRows = options.map((option, index) => {
    const y = 1220 + (index * 135);
    return `<rect x="120" y="${y}" width="840" height="100" rx="28" fill="#0a2119" fill-opacity=".34" stroke="#f4ecd7" stroke-opacity=".34" stroke-width="2"/>
      <circle cx="880" cy="${y + 50}" r="25" fill="#f4ecd7" fill-opacity=".18"/>
      <text x="880" y="${y + 61}" text-anchor="middle" direction="rtl" fill="#f4ecd7" font-size="27">${['أ', 'ب', 'ج'][index]}</text>
      <text x="540" y="${y + 66}" text-anchor="middle" direction="rtl" fill="#f4ecd7" font-size="39">${escapeXml(option)}</text>`;
  }).join('');

  return frame(`
    <text x="220" y="135" text-anchor="middle" direction="rtl" fill="#d8b75e" font-size="34" font-weight="700">أَكْمِلِ الْآيَةَ</text>
    <text x="850" y="135" text-anchor="middle" direction="rtl" fill="#f4ecd7" fill-opacity=".68" font-size="28">الجزء ${pageIndex + 1} من ${pageCount}</text>
    <text x="540" y="330" text-anchor="middle" direction="rtl" fill="#d8b75e" font-size="40">مَا الْكَلِمَةُ النَّاقِصَةُ؟</text>
    ${textLines(lines, 560, 120, lines.length > 2 ? 58 : 68)}
    ${optionRows}
  `, `${surah}، الْآيَةُ ${ayah}`);
}

function answerSvg(page: VersePage, hiddenIndex: number, answer: string, surah: string, ayah: number) {
  const lines = wrapWords(page.words);
  return frame(`
    <text x="540" y="350" text-anchor="middle" direction="rtl" fill="#d8b75e" font-size="46" font-weight="700">الإِجَابَةُ الصَّحِيحَةُ</text>
    <rect x="230" y="480" width="620" height="120" rx="36" fill="#d8b75e"/>
    <text x="540" y="560" text-anchor="middle" direction="rtl" fill="#173f31" font-size="54" font-weight="800">${escapeXml(answer)}</text>
    ${textLines(lines, 850, 120, lines.length > 2 ? 55 : 66)}
  `, `${surah}، الْآيَةُ ${ayah}، الكلمة ${hiddenIndex + 1}`);
}

async function run(command: string, args: string[]) {
  await new Promise<void>((resolve, reject) => {
    const process = spawn(command, args, { stdio: ['ignore', 'ignore', 'pipe'] });
    let errorOutput = '';
    process.stderr.on('data', (chunk) => { errorOutput += chunk.toString(); });
    process.on('error', reject);
    process.on('close', (code) => code === 0 ? resolve() : reject(new Error(`${command} failed (${code}): ${errorOutput.slice(-1200)}`)));
  });
}

async function getAudioDuration(path: string) {
  let output = '';
  await new Promise<void>((resolve, reject) => {
    const process = spawn('ffprobe', ['-v', 'error', '-show_entries', 'format=duration', '-of', 'default=noprint_wrappers=1:nokey=1', path]);
    process.stdout.on('data', (chunk) => { output += chunk.toString(); });
    process.on('error', reject);
    process.on('close', (code) => code === 0 ? resolve() : reject(new Error('تعذّر قراءة مدة التلاوة.')));
  });
  const duration = Number.parseFloat(output.trim());
  if (!Number.isFinite(duration) || duration <= 0 || duration > 900) throw new Error('مدة التلاوة غير صالحة.');
  return duration;
}

function buildOptions(words: string[], hiddenIndex: number) {
  const answer = words[hiddenIndex];
  const candidates = [...words.slice(hiddenIndex + 1), ...words.slice(0, hiddenIndex), 'الْحَقِّ', 'الْآخِرَةِ', 'الْعَالَمِينَ'];
  const unique = candidates.filter((word, index) => word !== answer && candidates.indexOf(word) === index);
  return [answer, ...unique.slice(0, 2)].sort((left, right) => left.localeCompare(right, 'ar'));
}

export async function POST(request: Request) {
  let workDirectory = '';
  try {
    const body = await request.json() as { surahNumber?: unknown; ayahNumber?: unknown; hiddenWordIndex?: unknown };
    const surahNumber = Number(body.surahNumber);
    const ayahNumber = Number(body.ayahNumber);
    const hiddenWordIndex = Number(body.hiddenWordIndex);
    if (!Number.isInteger(surahNumber) || surahNumber < 1 || surahNumber > 114 || !Number.isInteger(ayahNumber) || ayahNumber < 1) {
      return Response.json({ error: 'رقم السورة أو الآية غير صحيح.' }, { status: 400 });
    }

    const verseResponse = await fetch(`https://api.alquran.cloud/v1/ayah/${surahNumber}:${ayahNumber}/quran-simple`, { cache: 'no-store' });
    if (!verseResponse.ok) return Response.json({ error: 'تعذّر تحميل الآية المطلوبة.' }, { status: 400 });
    const versePayload = await verseResponse.json() as QuranApiResponse;
    const text = versePayload.data?.text?.trim();
    const surah = versePayload.data?.surah?.name?.trim() ?? `السورة ${surahNumber}`;
    if (!text) throw new Error('لم يصل نص الآية من المصدر.');
    const words = text.split(/\s+/).filter(Boolean);
    if (words.length < 3 || words.length > 250 || !Number.isInteger(hiddenWordIndex) || hiddenWordIndex <= 0 || hiddenWordIndex >= words.length - 1) {
      return Response.json({ error: 'الكلمة المختارة غير صالحة لهذه الآية.' }, { status: 400 });
    }

    workDirectory = await mkdtemp(join(tmpdir(), 'quran-video-'));
    const audioUrl = `https://everyayah.com/data/Yasser_Ad-Dussary_128kbps/${String(surahNumber).padStart(3, '0')}${String(ayahNumber).padStart(3, '0')}.mp3`;
    const audioResponse = await fetch(audioUrl, { cache: 'no-store' });
    if (!audioResponse.ok) throw new Error('تلاوة ياسر الدوسري غير متاحة لهذه الآية.');
    const audioPath = join(workDirectory, 'recitation.mp3');
    await writeFile(audioPath, Buffer.from(await audioResponse.arrayBuffer()));
    const audioDuration = await getAudioDuration(audioPath);

    const pages = splitIntoPages(words);
    const options = buildOptions(words, hiddenWordIndex);
    const answerPage = pages.find((page) => hiddenWordIndex >= page.startIndex && hiddenWordIndex < page.startIndex + page.words.length) ?? pages[0];
    const slides = [
      { svg: introSvg(), duration: 2 },
      ...pages.map((page, index) => ({
        svg: questionSvg(page, index, pages.length, hiddenWordIndex, options, surah, ayahNumber),
        duration: audioDuration * (page.words.length / words.length),
      })),
      { svg: answerSvg(answerPage, hiddenWordIndex, words[hiddenWordIndex], surah, ayahNumber), duration: 2 },
      { svg: outroSvg(), duration: 2 },
    ];

    const manifestLines: string[] = [];
    for (const [index, slide] of slides.entries()) {
      const imagePath = join(workDirectory, `slide-${String(index).padStart(2, '0')}.png`);
      await sharp(Buffer.from(slide.svg)).png().toFile(imagePath);
      manifestLines.push(`file '${imagePath}'`, `duration ${slide.duration.toFixed(3)}`);
    }
    manifestLines.push(`file '${join(workDirectory, `slide-${String(slides.length - 1).padStart(2, '0')}.png`)}'`);
    const manifestPath = join(workDirectory, 'slides.txt');
    await writeFile(manifestPath, manifestLines.join('\n'));

    const videoPath = join(workDirectory, 'quran-challenge.mp4');
    const totalDuration = 2 + audioDuration + 4;
    await run('ffmpeg', [
      '-y', '-f', 'concat', '-safe', '0', '-i', manifestPath,
      '-i', audioPath,
      '-filter_complex', '[1:a]adelay=2000:all=1,apad=pad_dur=4[a]',
      '-map', '0:v:0', '-map', '[a]', '-t', totalDuration.toFixed(3),
      '-vf', 'fps=30,format=yuv420p', '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '21',
      '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', videoPath,
    ]);

    const video = await readFile(videoPath);
    return new Response(new Uint8Array(video), {
      headers: {
        'Content-Type': 'video/mp4',
        'Content-Disposition': `attachment; filename="akmel-alaya-${surahNumber}-${ayahNumber}.mp4"`,
        'Cache-Control': 'no-store',
        'X-Quran-Video-Duration': totalDuration.toFixed(3),
      },
    });
  } catch (error) {
    console.error('Quran video render failed', error);
    return Response.json({ error: error instanceof Error ? error.message : 'تعذّر إنشاء الفيديو.' }, { status: 500 });
  } finally {
    if (workDirectory) await rm(workDirectory, { recursive: true, force: true });
  }
}
