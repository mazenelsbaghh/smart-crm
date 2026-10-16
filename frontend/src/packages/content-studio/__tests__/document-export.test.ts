import { expect, it, vi } from 'vitest';
import type { jsPDF as PdfDocument } from 'jspdf';
import { exportPdf } from '../document-export';
import type { ContentDocumentDetail, ContentDocumentKind } from '../types';

const saved = vi.hoisted(() => ({ contents: '' }));
vi.mock('jspdf', async importOriginal => {
  const actual = await importOriginal<typeof import('jspdf')>();
  return { ...actual, jsPDF: class extends actual.jsPDF {
    constructor(...args: ConstructorParameters<typeof actual.jsPDF>) {
      super(...args);
      function save(this: PdfDocument, filename: string, options: { returnPromise: true }): Promise<void>;
      function save(this: PdfDocument, filename?: string): PdfDocument;
      function save(this: PdfDocument, _filename?: string, options?: { returnPromise: true }) {
        saved.contents = this.output();
        return options?.returnPromise ? Promise.resolve() : this;
      }
      this.save = save;
    }
  } };
});

it.each<ContentDocumentKind>(['A4', 'Paper'])('exports %s with a physical A4 MediaBox', async kind => {
  const detail: ContentDocumentDetail = {
    document: { id: 'print-test', kind, status: 'Ready', title: 'ورقة للطباعة', requestedPageCount: 1,
      createdAt: '2026-10-16T00:00:00Z', updatedAt: '2026-10-16T00:00:00Z' },
    pages: [],
  };

  await exportPdf(detail);

  const mediaBox = saved.contents.match(/\/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]/);
  expect(mediaBox).not.toBeNull();
  expect(Number(mediaBox![1]) * 25.4 / 72).toBeCloseTo(210, 2);
  expect(Number(mediaBox![2]) * 25.4 / 72).toBeCloseTo(297, 2);
});
