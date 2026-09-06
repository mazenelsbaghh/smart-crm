'use client';

import { useEffect, useRef, useState } from 'react';
import { api } from '../../../services/api';
import type { Message } from '../../../types/chat';
import styles from '../inbox.module.css';

const attachmentNames = { Image: 'الصورة', Voice: 'التسجيل الصوتي', Document: 'المستند', Video: 'الفيديو' };

function attachmentFileName(header: string | undefined) {
  const encoded = header?.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  if (encoded) return decodeURIComponent(encoded);
  return header?.match(/filename="?([^";]+)"?/i)?.[1] ?? 'attachment';
}

export default function MessageAttachment({ assetId, mediaType }: Pick<Message, 'assetId' | 'mediaType'>) {
  const [download, setDownload] = useState<{ url: string; name: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const requestRef = useRef<AbortController | null>(null);
  const urlRef = useRef<string | null>(null);

  useEffect(() => () => {
    requestRef.current?.abort();
    if (urlRef.current) URL.revokeObjectURL(urlRef.current);
  }, []);

  const loadAttachment = async () => {
    if (!assetId || loading) return;
    const controller = new AbortController();
    requestRef.current = controller;
    setLoading(true);
    setFailed(false);
    try {
      const response = await api.get<Blob>(`/api/assets/${assetId}/content`, { responseType: 'blob', signal: controller.signal });
      if (controller.signal.aborted) return;
      const name = attachmentFileName(response.headers?.['content-disposition']);
      const url = URL.createObjectURL(response.data);
      urlRef.current = url;
      setDownload({ url, name });
      const link = document.createElement('a');
      link.href = url;
      link.download = name;
      link.click();
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  };

  const name = mediaType ? attachmentNames[mediaType] : 'المرفق';
  if (!assetId) return <span role="status">تعذر حفظ {name}. اطلب إعادة إرساله.</span>;
  if (download) return <a className={styles.attachmentLink} href={download.url} download={download.name}>تنزيل {name} مرة أخرى</a>;
  return (
    <div>
      {failed && <span role="status">تعذر تنزيل {name}. أعد المحاولة.</span>}
      <button type="button" className={styles.retryConversationsBtn} disabled={loading} onClick={loadAttachment}>
        {loading ? `جاري تنزيل ${name}...` : failed ? 'إعادة المحاولة' : `تنزيل ${name}`}
      </button>
    </div>
  );
}
