'use client';

import React, { useCallback, useEffect, useId, useRef, useState } from 'react';
import Image from 'next/image';
import type { AxiosError } from 'axios';
import QRCode from 'qrcode';
import { LogOut, Plus, QrCode, RefreshCw, Smartphone, Star } from 'lucide-react';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { api } from '../../services/api';
import styles from './settings.module.css';

export interface WhatsAppAccount {
  id: string;
  projectId: string;
  name: string;
  isDefault: boolean;
}

type SessionStatus = 'Disconnected' | 'Initializing' | 'Reconnecting' | 'Connected';
type AccountAction = 'start' | 'disconnect' | 'default' | null;

interface SessionStatusResponse {
  projectId: string;
  whatsappAccountId?: string;
  status: SessionStatus;
  phoneNumber: string | null;
  error?: string | null;
}

interface AccountRuntime {
  status: SessionStatus | null;
  phoneNumber: string | null;
  error: string | null;
  qrImageUrl: string | null;
  qrError: string | null;
  statusBusy: boolean;
  qrBusy: boolean;
  actionBusy: AccountAction;
}

interface WhatsAppAccountsPanelProps {
  projectId: string;
}

const initialRuntime = (): AccountRuntime => ({
  status: null,
  phoneNumber: null,
  error: null,
  qrImageUrl: null,
  qrError: null,
  statusBusy: true,
  qrBusy: false,
  actionBusy: null,
});

const getApiErrorMessage = (error: unknown, fallback: string) => {
  const response = (error as AxiosError<{ error?: string; message?: string }>)?.response;
  return response?.data?.error || response?.data?.message || fallback;
};

const statusCopy: Record<SessionStatus, string> = {
  Connected: 'متصل',
  Initializing: 'جاري التجهيز',
  Reconnecting: 'جاري استعادة الاتصال',
  Disconnected: 'غير متصل',
};

const statusClass = (status: SessionStatus | null) => {
  if (status === 'Connected') return styles.dotConnected;
  if (status === 'Initializing' || status === 'Reconnecting') return styles.dotInitializing;
  if (status === 'Disconnected') return styles.dotDisconnected;
  return styles.dotPending;
};

const formatPhoneNumber = (phoneNumber: string) => phoneNumber.startsWith('+') ? phoneNumber : `+${phoneNumber}`;

export default function WhatsAppAccountsPanel({ projectId }: WhatsAppAccountsPanelProps) {
  const panelTitleId = useId();
  const accountTitlePrefix = useId();
  const [accounts, setAccounts] = useState<WhatsAppAccount[]>([]);
  const [runtimeByAccount, setRuntimeByAccount] = useState<Record<string, AccountRuntime>>({});
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [newAccountName, setNewAccountName] = useState('');
  const [addBusy, setAddBusy] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pendingDisconnect, setPendingDisconnect] = useState<WhatsAppAccount | null>(null);
  const projectEpochRef = useRef(0);
  const statusGenerationRef = useRef(new Map<string, number>());
  const qrGenerationRef = useRef(new Map<string, number>());

  const updateRuntime = useCallback((accountId: string, patch: Partial<AccountRuntime>) => {
    setRuntimeByAccount((current) => ({
      ...current,
      [accountId]: { ...(current[accountId] ?? initialRuntime()), ...patch },
    }));
  }, []);

  const nextGeneration = useCallback((generations: React.MutableRefObject<Map<string, number>>, accountId: string) => {
    const next = (generations.current.get(accountId) ?? 0) + 1;
    generations.current.set(accountId, next);
    return next;
  }, []);

  const fetchQr = useCallback(async (accountId: string) => {
    const projectEpoch = projectEpochRef.current;
    const requestGeneration = nextGeneration(qrGenerationRef, accountId);
    updateRuntime(accountId, { qrBusy: true });
    try {
      const qrResponse = await api.get<{ projectId?: string; whatsappAccountId?: string; qr?: string; error?: string }>('/api/whatsapp/session/qr', {
        params: { projectId, whatsappAccountId: accountId },
        validateStatus: (status) => status === 200 || status === 404,
      });
      if (projectEpoch !== projectEpochRef.current || requestGeneration !== qrGenerationRef.current.get(accountId)) return;
      if (
        (qrResponse.data.projectId && qrResponse.data.projectId !== projectId)
        || (qrResponse.data.whatsappAccountId && qrResponse.data.whatsappAccountId !== accountId)
      ) return;

      if (qrResponse.status === 200 && qrResponse.data.qr) {
        const qrImageUrl = await QRCode.toDataURL(qrResponse.data.qr, {
          width: 250,
          margin: 1,
          color: { dark: '#111827', light: '#f8fafc' },
        });
        if (projectEpoch !== projectEpochRef.current || requestGeneration !== qrGenerationRef.current.get(accountId)) return;
        updateRuntime(accountId, { qrImageUrl, qrError: null });
      } else {
        updateRuntime(accountId, {
          qrImageUrl: null,
          qrError: qrResponse.data.error || 'كود الربط غير جاهز بعد. سنحاول مرة أخرى.',
        });
      }
    } catch (error) {
      if (projectEpoch !== projectEpochRef.current || requestGeneration !== qrGenerationRef.current.get(accountId)) return;
      console.error('Failed to fetch WhatsApp QR code', error);
      updateRuntime(accountId, { qrImageUrl: null, qrError: 'تعذر تحميل كود الربط من بوابة واتساب.' });
    } finally {
      if (projectEpoch === projectEpochRef.current && requestGeneration === qrGenerationRef.current.get(accountId)) {
        updateRuntime(accountId, { qrBusy: false });
      }
    }
  }, [nextGeneration, projectId, updateRuntime]);

  const fetchStatus = useCallback(async (accountId: string) => {
    const projectEpoch = projectEpochRef.current;
    const requestGeneration = nextGeneration(statusGenerationRef, accountId);
    updateRuntime(accountId, { statusBusy: true });
    try {
      const statusResponse = await api.get<SessionStatusResponse>('/api/whatsapp/session/status', {
        params: { projectId, whatsappAccountId: accountId },
      });
      if (projectEpoch !== projectEpochRef.current || requestGeneration !== statusGenerationRef.current.get(accountId)) return;
      if (
        statusResponse.data.projectId !== projectId
        || (statusResponse.data.whatsappAccountId && statusResponse.data.whatsappAccountId !== accountId)
      ) return;

      const nextStatus = statusResponse.data.status;
      updateRuntime(accountId, {
        status: nextStatus,
        phoneNumber: statusResponse.data.phoneNumber,
        error: statusResponse.data.error || null,
        ...(
          nextStatus === 'Initializing'
            ? {}
            : { qrImageUrl: null, qrError: null }
        ),
      });

      if (nextStatus === 'Initializing') {
        void fetchQr(accountId);
      } else {
        nextGeneration(qrGenerationRef, accountId);
      }
    } catch (error) {
      if (projectEpoch !== projectEpochRef.current || requestGeneration !== statusGenerationRef.current.get(accountId)) return;
      console.error('Failed to fetch WhatsApp session status', error);
      updateRuntime(accountId, { error: 'تعذر تحديث حالة واتساب. آخر حالة معروضة قد تكون قديمة.' });
    } finally {
      if (projectEpoch === projectEpochRef.current && requestGeneration === statusGenerationRef.current.get(accountId)) {
        updateRuntime(accountId, { statusBusy: false });
      }
    }
  }, [fetchQr, nextGeneration, projectId, updateRuntime]);

  useEffect(() => {
    const projectEpoch = projectEpochRef.current + 1;
    projectEpochRef.current = projectEpoch;
    statusGenerationRef.current.clear();
    qrGenerationRef.current.clear();

    const loadAccounts = async () => {
      setLoading(true);
      setLoadError(null);
      setAccounts([]);
      setRuntimeByAccount({});
      setNotice(null);
      setPendingDisconnect(null);
      try {
        const accountsResponse = await api.get<WhatsAppAccount[]>('/api/whatsapp/accounts', { params: { projectId } });
        if (projectEpoch !== projectEpochRef.current) return;
        const loadedAccounts = accountsResponse.data;
        setAccounts(loadedAccounts);
        setRuntimeByAccount(Object.fromEntries(loadedAccounts.map((account) => [account.id, initialRuntime()])));
        loadedAccounts.forEach((account) => void fetchStatus(account.id));
      } catch (error) {
        if (projectEpoch !== projectEpochRef.current) return;
        console.error('Failed to load WhatsApp accounts', error);
        setLoadError('تعذر تحميل حسابات واتساب. أعد المحاولة.');
      } finally {
        if (projectEpoch === projectEpochRef.current) setLoading(false);
      }
    };

    void loadAccounts();
    return () => {
      if (projectEpochRef.current === projectEpoch) projectEpochRef.current += 1;
    };
  }, [fetchStatus, projectId]);

  const hasInitializingAccount = accounts.some((account) => {
    const status = runtimeByAccount[account.id]?.status;
    return status === 'Initializing' || status === 'Reconnecting';
  });

  useEffect(() => {
    if (accounts.length === 0) return;
    const interval = window.setInterval(() => {
      accounts.forEach((account) => void fetchStatus(account.id));
    }, hasInitializingAccount ? 5_000 : 15_000);
    return () => window.clearInterval(interval);
  }, [accounts, fetchStatus, hasInitializingAccount]);

  const handleAddAccount = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const name = newAccountName.trim();
    if (!name) {
      setAddError('اكتب اسمًا يميّز الحساب.');
      return;
    }

    const projectEpoch = projectEpochRef.current;
    setAddBusy(true);
    setAddError(null);
    setNotice(null);
    try {
      const createdAccountResponse = await api.post<WhatsAppAccount>('/api/whatsapp/accounts', { projectId, name });
      if (projectEpoch !== projectEpochRef.current) return;
      const account = createdAccountResponse.data;
      setAccounts((current) => [...current, account]);
      setRuntimeByAccount((current) => ({ ...current, [account.id]: initialRuntime() }));
      setNewAccountName('');
      setNotice(`تمت إضافة حساب «${account.name}». يمكنك ربط رقمه الآن.`);
      void fetchStatus(account.id);
    } catch (error) {
      if (projectEpoch !== projectEpochRef.current) return;
      setAddError(getApiErrorMessage(error, 'تعذر إضافة حساب واتساب.'));
    } finally {
      if (projectEpoch === projectEpochRef.current) setAddBusy(false);
    }
  };

  const handleStartSession = async (account: WhatsAppAccount) => {
    const projectEpoch = projectEpochRef.current;
    nextGeneration(statusGenerationRef, account.id);
    nextGeneration(qrGenerationRef, account.id);
    updateRuntime(account.id, { actionBusy: 'start', error: null, qrError: null });
    setNotice(null);
    try {
      await api.post('/api/whatsapp/session/start', { projectId, whatsappAccountId: account.id });
      if (projectEpoch !== projectEpochRef.current) return;
      nextGeneration(statusGenerationRef, account.id);
      updateRuntime(account.id, {
        actionBusy: null,
        status: 'Initializing',
        phoneNumber: null,
        qrImageUrl: null,
      });
      void fetchQr(account.id);
    } catch (error) {
      if (projectEpoch !== projectEpochRef.current) return;
      nextGeneration(statusGenerationRef, account.id);
      nextGeneration(qrGenerationRef, account.id);
      updateRuntime(account.id, {
        actionBusy: null,
        error: getApiErrorMessage(error, `تعذر بدء جلسة «${account.name}».`),
      });
    }
  };

  const handleDisconnect = async (account: WhatsAppAccount) => {
    const projectEpoch = projectEpochRef.current;
    setPendingDisconnect(null);
    nextGeneration(statusGenerationRef, account.id);
    nextGeneration(qrGenerationRef, account.id);
    updateRuntime(account.id, { actionBusy: 'disconnect', error: null });
    setNotice(null);
    try {
      await api.post('/api/whatsapp/session/disconnect', { projectId, whatsappAccountId: account.id });
      if (projectEpoch !== projectEpochRef.current) return;
      nextGeneration(statusGenerationRef, account.id);
      nextGeneration(qrGenerationRef, account.id);
      updateRuntime(account.id, {
        actionBusy: null,
        status: 'Disconnected',
        phoneNumber: null,
        qrImageUrl: null,
        qrError: null,
      });
      setNotice(`تم فصل حساب «${account.name}» مع الاحتفاظ بالمحادثات السابقة.`);
    } catch (error) {
      if (projectEpoch !== projectEpochRef.current) return;
      nextGeneration(statusGenerationRef, account.id);
      nextGeneration(qrGenerationRef, account.id);
      updateRuntime(account.id, {
        actionBusy: null,
        error: getApiErrorMessage(error, `تعذر فصل حساب «${account.name}».`),
      });
    }
  };

  const handleSetDefault = async (account: WhatsAppAccount) => {
    if (account.isDefault) return;
    const projectEpoch = projectEpochRef.current;
    updateRuntime(account.id, { actionBusy: 'default', error: null });
    setNotice(null);
    try {
      await api.put(`/api/whatsapp/accounts/${account.id}`, {
        projectId,
        name: account.name,
        isDefault: true,
      });
      if (projectEpoch !== projectEpochRef.current) return;
      setAccounts((current) => current.map((candidateAccount) => ({
        ...candidateAccount,
        isDefault: candidateAccount.id === account.id,
      })));
      updateRuntime(account.id, { actionBusy: null });
      setNotice(`أصبح حساب «${account.name}» هو الحساب الافتراضي.`);
    } catch (error) {
      if (projectEpoch !== projectEpochRef.current) return;
      updateRuntime(account.id, {
        actionBusy: null,
        error: getApiErrorMessage(error, 'تعذر تغيير الحساب الافتراضي.'),
      });
    }
  };

  return (
    <section className={`${styles.card} ${styles.whatsAppPanel}`} aria-labelledby={panelTitleId}>
      <div className={styles.accountsHeader}>
        <div>
          <h2 id={panelTitleId} className={styles.cardTitlePlain}>
            <Smartphone size={20} aria-hidden="true" />
            حسابات واتساب
          </h2>
          <p className={styles.accountsHint}>اربط أكثر من رقم، وتابع حالة وكود كل حساب بشكل مستقل.</p>
        </div>
        {!loading && <span className={styles.accountCount}>{accounts.length} حساب</span>}
      </div>

      {loadError && <p role="alert" className={styles.inlineError}>{loadError}</p>}
      {notice && (
        <p role="status" aria-live="polite" className={styles.inlineSuccess}>
          {notice}
        </p>
      )}

      {loading ? (
        <div className={styles.accountSkeletonList} role="status" aria-label="جاري تحميل حسابات واتساب">
          <span className={styles.accountSkeleton} />
          <span className={styles.accountSkeleton} />
        </div>
      ) : accounts.length > 0 ? (
        <ul className={styles.accountList}>
          {accounts.map((account) => {
            const runtime = runtimeByAccount[account.id] ?? initialRuntime();
            const accountTitleId = `${accountTitlePrefix}-${account.id}`;
            const accountBusy = runtime.actionBusy !== null;
            return (
              <li key={account.id} className={styles.accountRow} aria-labelledby={accountTitleId}>
                <div className={styles.accountSummary}>
                  <div className={styles.accountIdentity}>
                    <h3 id={accountTitleId} className={styles.accountName}>{account.name}</h3>
                    {account.isDefault && <span className={styles.defaultBadge}>افتراضي</span>}
                  </div>
                  <div className={styles.accountStatus} aria-live="polite">
                    <span className={`${styles.dot} ${statusClass(runtime.status)}`} aria-hidden="true" />
                    <span>{runtime.statusBusy && runtime.status === null ? 'جاري تحديث الحالة' : runtime.status ? statusCopy[runtime.status] : 'الحالة غير متاحة'}</span>
                    {runtime.phoneNumber && <bdi className={styles.accountPhone}>{formatPhoneNumber(runtime.phoneNumber)}</bdi>}
                  </div>
                </div>

                {runtime.error && <p role="alert" className={styles.inlineError}>{runtime.error}</p>}

                {runtime.status === 'Initializing' && (
                  <div className={styles.accountQrRegion}>
                    {runtime.qrImageUrl ? (
                      <>
                        <div className={styles.qrWrapper}>
                          <Image
                            src={runtime.qrImageUrl}
                            alt={`كود ربط حساب ${account.name}`}
                            className={styles.qrImage}
                            width={220}
                            height={220}
                            unoptimized
                          />
                        </div>
                        <p className={styles.qrInstructions}>من واتساب على الموبايل، افتح الأجهزة المرتبطة ثم امسح هذا الكود.</p>
                      </>
                    ) : (
                      <p className={styles.qrPending} role="status">
                        {runtime.qrBusy ? 'جاري تجهيز كود الربط…' : runtime.qrError || 'كود الربط لم يجهز بعد.'}
                      </p>
                    )}
                    <button
                      type="button"
                      className={`${styles.btn} ${styles.btnSecondary} ${styles.compactButton}`}
                      onClick={() => void fetchQr(account.id)}
                      disabled={accountBusy || runtime.qrBusy}
                      aria-label={`تحديث كود حساب ${account.name}`}
                    >
                      <RefreshCw size={15} aria-hidden="true" />
                      تحديث الكود
                    </button>
                  </div>
                )}

                <div className={styles.accountActions}>
                  <button
                    type="button"
                    className={`${styles.btn} ${styles.btnSecondary} ${styles.compactButton}`}
                    onClick={() => void fetchStatus(account.id)}
                    disabled={accountBusy}
                    aria-label={`تحديث حالة حساب ${account.name}`}
                  >
                    <RefreshCw size={15} aria-hidden="true" />
                    تحديث الحالة
                  </button>

                  {!account.isDefault && (
                    <button
                      type="button"
                      className={`${styles.btn} ${styles.btnSecondary} ${styles.compactButton}`}
                      onClick={() => void handleSetDefault(account)}
                      disabled={accountBusy}
                      aria-label={`تعيين حساب ${account.name} كافتراضي`}
                    >
                      <Star size={15} aria-hidden="true" />
                      {runtime.actionBusy === 'default' ? 'جاري التعيين…' : 'تعيين كافتراضي'}
                    </button>
                  )}

                  {runtime.status === 'Connected' || runtime.status === 'Initializing' || runtime.status === 'Reconnecting' ? (
                    <button
                      type="button"
                      className={`${styles.btn} ${styles.btnDanger} ${styles.compactButton}`}
                      onClick={() => setPendingDisconnect(account)}
                      disabled={accountBusy}
                      aria-label={`فصل حساب ${account.name}`}
                    >
                      <LogOut size={15} aria-hidden="true" />
                      {runtime.actionBusy === 'disconnect' ? 'جاري الفصل…' : 'فصل'}
                    </button>
                  ) : (
                    <button
                      type="button"
                      className={`${styles.btn} ${styles.btnPrimary} ${styles.compactButton}`}
                      onClick={() => void handleStartSession(account)}
                      disabled={accountBusy}
                      aria-label={`ربط حساب ${account.name}`}
                    >
                      <QrCode size={15} aria-hidden="true" />
                      {runtime.actionBusy === 'start' ? 'جاري التجهيز…' : 'ربط الرقم'}
                    </button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      ) : (
        <div className={styles.accountsEmpty}>
          <Smartphone size={24} aria-hidden="true" />
          <p>لا توجد حسابات بعد. أضف أول حساب ثم اربط رقمه بكود QR.</p>
        </div>
      )}

      <form className={styles.inlineAddAccount} onSubmit={handleAddAccount} noValidate>
        <div className={styles.inlineAddField}>
          <label className={styles.label} htmlFor={`${panelTitleId}-new-account`}>اسم حساب واتساب الجديد</label>
          <input
            id={`${panelTitleId}-new-account`}
            className={styles.input}
            value={newAccountName}
            onChange={(event) => { setNewAccountName(event.target.value); setAddError(null); }}
            placeholder="مثال: فرع الجيزة"
            autoComplete="off"
            maxLength={100}
            aria-describedby={addError ? `${panelTitleId}-add-error` : undefined}
            aria-invalid={Boolean(addError)}
            disabled={addBusy}
          />
          {addError && <p id={`${panelTitleId}-add-error`} role="alert" className={styles.fieldError}>{addError}</p>}
        </div>
        <button type="submit" className={`${styles.btn} ${styles.btnPrimary}`} disabled={addBusy || !newAccountName.trim()}>
          <Plus size={17} aria-hidden="true" />
          {addBusy ? 'جاري الإضافة…' : 'إضافة حساب'}
        </button>
      </form>

      <ConfirmDialog
        isOpen={Boolean(pendingDisconnect)}
        title={`فصل حساب «${pendingDisconnect?.name ?? ''}»؟`}
        message="سيتوقف الإرسال والاستقبال من هذا الرقم، مع الاحتفاظ بالمحادثات السابقة. ستحتاج إلى مسح كود جديد لإعادة الربط."
        confirmLabel="تأكيد الفصل"
        onCancel={() => setPendingDisconnect(null)}
        onConfirm={() => { if (pendingDisconnect) void handleDisconnect(pendingDisconnect); }}
      />
    </section>
  );
}
