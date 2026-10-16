import { Document, ImageRun, Packer, PageBreak, Paragraph } from 'docx';
import { jsPDF } from 'jspdf';
import PptxGenJS from 'pptxgenjs';
import type { ContentDocumentDetail } from './types';
import { contentApi } from './content-api';

type ExportPage = ContentDocumentDetail['pages'][number] & { imageData?: string };

export async function exportPresentationPptx(detail: ContentDocumentDetail) {
  const pages = await hydrateImages(detail.pages);
  const pptx = new PptxGenJS();
  pptx.layout = 'LAYOUT_WIDE';
  pptx.author = 'Smart Customer Core';
  pptx.subject = detail.document.title;
  pptx.title = detail.document.title;
  for (const page of pages) {
    const slide = pptx.addSlide();
    if (page.imageData) slide.addImage({ data: page.imageData, x: 0, y: 0, w: 13.333, h: 7.5 });
  }
  await pptx.writeFile({ fileName: `${safeName(detail.document.title)}.pptx` });
}

export async function exportDocumentDocx(detail: ContentDocumentDetail) {
  const pages = await hydrateImages(detail.pages);
  const children: Paragraph[] = [];
  for (const [index, page] of pages.entries()) {
    const dimensions = page.imageData && detail.document.kind === 'Paper'
      ? containDimensions(await loadImage(page.imageData), { width: 560, height: 792 })
      : { width: 560, height: 792 };
    if (page.imageData) children.push(new Paragraph({ alignment: 'center', children: [new ImageRun({ data: dataUrlBytes(page.imageData), transformation: dimensions, type: imageType(page.imageData) })] }));
    if (index < pages.length - 1) children.push(new Paragraph({ children: [new PageBreak()] }));
  }
  const document = new Document({ sections: [{ properties: { page: { margin: { top: 120, right: 120, bottom: 120, left: 120 } } }, children }] });
  downloadBlob(await Packer.toBlob(document), `${safeName(detail.document.title)}.docx`);
}

export async function exportPdf(detail: ContentDocumentDetail) {
  const pages = await hydrateImages(detail.pages);
  const presentation = detail.document.kind === 'Presentation';
  const dimensions: [number, number] = presentation ? [1600, 900] : [1240, 1754];
  const pageSize: [number, number] = presentation ? dimensions : [210, 297];
  const orientation = presentation ? 'landscape' : 'portrait';
  const pdf = new jsPDF({ orientation, unit: presentation ? 'px' : 'mm', format: pageSize, hotfixes: ['px_scaling'] });
  for (let index = 0; index < pages.length; index += 1) {
    if (index > 0) pdf.addPage(pageSize, orientation);
    const canvas = await renderPage(pages[index], dimensions, detail.document.kind === 'Paper' ? 'contain' : 'cover');
    pdf.addImage(canvas.toDataURL('image/jpeg', 0.94), 'JPEG', 0, 0, pageSize[0], pageSize[1], undefined, 'FAST');
  }
  pdf.save(`${safeName(detail.document.title)}.pdf`);
}

async function hydrateImages(pages: ContentDocumentDetail['pages']): Promise<ExportPage[]> {
  return Promise.all(pages.map(async page => {
    if (!page.imageUrl) return page;
    const blob = await contentApi.downloadAsset(page.imageUrl, new AbortController().signal);
    return { ...page, imageData: await blobToDataUrl(blob) };
  }));
}

async function renderPage(page: ExportPage, dimensions: [number, number], fit: 'contain' | 'cover') {
  const canvas = document.createElement('canvas');
  [canvas.width, canvas.height] = dimensions;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('تعذر تجهيز ملف PDF.');
  context.fillStyle = fit === 'contain' ? '#faf9f5' : '#0a0e17';
  context.fillRect(0, 0, canvas.width, canvas.height);
  if (page.imageData) {
    const image = await loadImage(page.imageData);
    const scale = (fit === 'contain' ? Math.min : Math.max)(canvas.width / image.width, canvas.height / image.height);
    context.drawImage(image, (canvas.width - image.width * scale) / 2, (canvas.height - image.height * scale) / 2, image.width * scale, image.height * scale);
  }
  return canvas;
}

function containDimensions(image: HTMLImageElement, bounds: { width: number; height: number }) {
  const scale = Math.min(bounds.width / image.width, bounds.height / image.height);
  return { width: image.width * scale, height: image.height * scale };
}

function blobToDataUrl(blob: Blob) { return new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsDataURL(blob); }); }
function loadImage(source: string) { return new Promise<HTMLImageElement>((resolve, reject) => { const image = new Image(); image.onload = () => resolve(image); image.onerror = reject; image.src = source; }); }
function dataUrlBytes(value: string) { return Uint8Array.from(atob(value.split(',')[1]), character => character.charCodeAt(0)); }
function imageType(value: string): 'png' | 'jpg' | 'gif' | 'bmp' { return value.startsWith('data:image/png') ? 'png' : 'jpg'; }
function safeName(value: string) { return (value || 'ملف جديد').replace(/[\\/:*?"<>|]/g, '-').slice(0, 90); }
function downloadBlob(blob: Blob, name: string) { const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; anchor.click(); URL.revokeObjectURL(url); }
