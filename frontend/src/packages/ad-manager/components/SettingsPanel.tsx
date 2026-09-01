'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import axios from 'axios';
import { LoaderCircle, Megaphone, RefreshCw, ShieldCheck } from 'lucide-react';
import ConfirmDialog from '../../../components/shared/ConfirmDialog';
import { adManagerApi } from '../api/ad-manager-api';
import type {
  AdvertisingConnection,
  AdvertisingEnvelope,
  MetaResourceCatalog,
  WhatsAppGatewayAccount,
  WhatsAppGatewayStatus,
} from '../types';
import styles from '../AdManager.module.css';

type SettingsPanelProps = {
  projectId: string;
  dailyCap: number;
  onSaved: (message: string) => Promise<unknown>;
};

type IntegrationMode = 'CloudApiCoexistence' | 'CloudApi' | 'BaileysObservedExperimental';
type Feedback = { kind: 'error' | 'status'; text: string };
type PendingEnvelope = Pick<AdvertisingEnvelope, 'id' | 'version' | 'state'>;
type ConnectionLoadState = 'loading' | 'disconnected' | 'ready' | 'error';

const experimentalGatewayEnabled = process.env.NEXT_PUBLIC_ENABLE_EXPERIMENTAL_BAILEYS_AD_ATTRIBUTION === 'true';

const storedList = (json?: string) => {
  if (!json) return [];
  try { return JSON.parse(json) as string[]; }
  catch (error) { if (error instanceof SyntaxError) return []; throw error; }
};

const saveErrorMessage = (error: unknown) => {
  if (!axios.isAxiosError<{ code?: string; message?: string }>(error)) return 'تعذّر حفظ الاتصال أو السقف. حاول مرة أخرى.';
  const code = error.response?.data?.code;
  if (code === 'ADS_TIMEZONE_INVALID') return 'المنطقة الزمنية للحساب غير متاحة. اختر حسابًا بتوقيت صالح ثم أعد الحفظ.';
  if (code === 'ADS_CURRENCY_MISMATCH') return 'عملة السقف لا تطابق عملة حساب الإعلانات المختار.';
  if (code === 'ADS_OFFER_REQUIRED') return 'أضف عرضًا مؤهلًا في عقل الشركة قبل تفويض الصرف.';
  if (code === 'ADS_RESOURCES_REQUIRED' || code === 'ADS_RESOURCES_NOT_MUTUALLY_ELIGIBLE') return 'اختر حساب الإعلانات والصفحة وWABA ورقم واتساب وDataset يمكن الوصول إليها بنفس تفويض Meta.';
  if (code === 'ADS_GATEWAY_NOT_CONNECTED') return 'بوابة واتساب التجريبية غير متصلة. استخدم Cloud API للإنتاج أو اربط البوابة في بيئة الاختبار.';
  return error.response?.data?.message ?? 'تعذّر حفظ الاتصال أو السقف. تأكد أن الموارد متوافقة.';
};

const validTimezone = (timezone: string | undefined) => {
  if (!timezone?.trim()) return false;
  try { new Intl.DateTimeFormat('en', { timeZone: timezone }).format(); return true; }
  catch { return false; }
};

export function SettingsPanel({ projectId, dailyCap: savedDailyCap, onSaved }: SettingsPanelProps) {
  const [resources, setResources] = useState<MetaResourceCatalog | null>(null);
  const [account, setAccount] = useState('');
  const [page, setPage] = useState('');
  const [waba, setWaba] = useState('');
  const [phone, setPhone] = useState('');
  const [dataset, setDataset] = useState('');
  const [integrationMode, setIntegrationMode] = useState<IntegrationMode>('CloudApiCoexistence');
  const [gatewayAccounts, setGatewayAccounts] = useState<WhatsAppGatewayAccount[]>([]);
  const [gatewayAccountId, setGatewayAccountId] = useState('');
  const [gateway, setGateway] = useState<WhatsAppGatewayStatus | null>(null);
  const [dailyCap, setDailyCap] = useState(savedDailyCap || 0);
  const [monthlyCap, setMonthlyCap] = useState(0);
  const [allowedCountries, setAllowedCountries] = useState('');
  const [excludedCountries, setExcludedCountries] = useState('');
  const [minimumAge, setMinimumAge] = useState(18);
  const [requiredLanguages, setRequiredLanguages] = useState('');
  const [pendingEnvelope, setPendingEnvelope] = useState<PendingEnvelope | null>(null);
  const [confirmActivation, setConfirmActivation] = useState(false);
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [connectionLoadState, setConnectionLoadState] = useState<ConnectionLoadState>('loading');
  const [loadAttempt, setLoadAttempt] = useState(0);

  const selectedWaba = useMemo(() => resources?.wabas.find((item) => item.id === waba), [resources, waba]);
  const selectedAccount = useMemo(() => resources?.adAccounts.find((item) => item.id === account), [account, resources]);
  const selectedPage = useMemo(() => resources?.pages.find((item) => item.id === page), [page, resources]);
  const selectedDataset = useMemo(() => resources?.datasets.find((item) => item.id === dataset), [dataset, resources]);
  const selectedPhone = useMemo(() => selectedWaba?.phones.find((item) => item.id === phone), [phone, selectedWaba]);
  const productionMode = integrationMode !== 'BaileysObservedExperimental';

  const applyCatalog = useCallback((catalog: MetaResourceCatalog, connection?: AdvertisingConnection | null) => {
    const nextMode = connection?.integrationMode;
    const nextWaba = connection?.wabaExternalId ?? '';
    setResources(catalog);
    setAccount(connection?.adAccountExternalId ?? catalog.adAccounts[0]?.id ?? '');
    setPage(connection?.pageExternalId ?? catalog.pages[0]?.id ?? '');
    setWaba(nextWaba);
    setDataset(connection?.datasetExternalId ?? '');
    const connectedPhone = connection?.phoneNumberExternalId ?? '';
    const phoneBelongsToWaba = catalog.wabas.find((item) => item.id === nextWaba)?.phones.some((item) => item.id === connectedPhone);
    setPhone(phoneBelongsToWaba ? connectedPhone : '');
    setIntegrationMode(nextMode === 'BaileysObservedExperimental' && !experimentalGatewayEnabled
      ? 'CloudApiCoexistence'
      : nextMode ?? 'CloudApiCoexistence');
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const loadConnectedResources = async () => {
      setBusy(true);
      setConnectionLoadState('loading');
      setFeedback(null);
      setResources(null);
      setAccount('');
      setPage('');
      setWaba('');
      setPhone('');
      setDataset('');
      setGatewayAccounts([]);
      setGatewayAccountId('');
      setGateway(null);
      setDailyCap(savedDailyCap || 0);
      setMonthlyCap(0);
      setAllowedCountries('');
      setExcludedCountries('');
      setMinimumAge(18);
      setRequiredLanguages('');
      setPendingEnvelope(null);
      setConfirmActivation(false);
      try {
        const [connection, envelope, loadedGatewayAccounts] = await Promise.all([
          adManagerApi.connection(projectId, controller.signal),
          adManagerApi.envelope(projectId, controller.signal),
          experimentalGatewayEnabled
            ? adManagerApi.whatsAppAccounts(projectId, controller.signal)
            : Promise.resolve([]),
        ]);
        if (controller.signal.aborted) return;
        setGatewayAccounts(loadedGatewayAccounts);
        const selectedGatewayAccountId = connection?.whatsAppAccountId
          ?? loadedGatewayAccounts.find((candidate) => candidate.isDefault)?.id
          ?? loadedGatewayAccounts[0]?.id
          ?? '';
        setGatewayAccountId(selectedGatewayAccountId);
        if (experimentalGatewayEnabled && selectedGatewayAccountId) {
          try {
            const gatewayStatus = await adManagerApi.gatewayStatus(
              projectId,
              selectedGatewayAccountId,
              controller.signal,
            );
            if (!controller.signal.aborted) setGateway(gatewayStatus);
          } catch (error) {
            if (!controller.signal.aborted && !axios.isCancel(error)) {
              setFeedback({ kind: 'status', text: 'تعذّر تحميل حالة Gateway التجريبي؛ حالة ربط Meta موضحة بشكل مستقل.' });
            }
          }
        }
        setDailyCap(envelope?.dailyCap ?? savedDailyCap);
        setMonthlyCap(envelope?.periodCap ?? 0);
        setAllowedCountries(storedList(envelope?.allowedCountriesJson).join(', '));
        setExcludedCountries(storedList(envelope?.hardExcludedGeoJson).join(', '));
        setMinimumAge(envelope?.hardMinimumAge || 18);
        setRequiredLanguages(storedList(envelope?.hardRequiredLanguagesJson).join(', '));
        setPendingEnvelope(envelope ? { id: envelope.id, version: envelope.version, state: envelope.state } : null);
        if (!connection) {
          setConnectionLoadState('disconnected');
          return;
        }
        const catalog = await adManagerApi.resources(
          projectId,
          connection.adAccountExternalId,
          controller.signal,
        );
        if (controller.signal.aborted) return;
        applyCatalog(catalog, connection);
        setConnectionLoadState('ready');
      } catch (error) {
        if (controller.signal.aborted || axios.isCancel(error)) return;
        setConnectionLoadState('error');
        setFeedback({ kind: 'error', text: saveErrorMessage(error) });
        setBusy(false);
        controller.abort();
      } finally {
        if (!controller.signal.aborted) setBusy(false);
      }
    };

    void loadConnectedResources();
    return () => controller.abort();
  }, [applyCatalog, loadAttempt, projectId, savedDailyCap]);

  const connect = async () => {
    setBusy(true);
    setFeedback(null);
    try {
      const result = await adManagerApi.startOAuth(projectId);
      if (result.authorizationUrl.startsWith('http')) {
        window.location.assign(result.authorizationUrl);
        return;
      }
      const catalog = await adManagerApi.resources(projectId);
      applyCatalog(catalog);
      setConnectionLoadState('ready');
      setFeedback({ kind: 'status', text: 'تم ربط Meta. اختر الموارد التي ستُستخدم للإعلانات وقياس نتائج واتساب.' });
    } catch (error) {
      setFeedback({ kind: 'error', text: saveErrorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  const changeAccount = async (nextAccount: string) => {
    setAccount(nextAccount);
    setBusy(true);
    setFeedback(null);
    try {
      const catalog = await adManagerApi.resources(projectId, nextAccount);
      setResources(catalog);
      setPage((current) => catalog.pages.some((item) => item.id === current) ? current : catalog.pages[0]?.id ?? '');
      setWaba('');
      setPhone('');
      setDataset('');
    } catch (error) {
      setFeedback({ kind: 'error', text: saveErrorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  const changeWaba = (nextWaba: string) => {
    setWaba(nextWaba);
    setPhone(resources?.wabas.find((item) => item.id === nextWaba)?.phones[0]?.id ?? '');
  };

  const changeGatewayAccount = async (nextAccountId: string) => {
    setGatewayAccountId(nextAccountId);
    setGateway(null);
    setBusy(true);
    setFeedback(null);
    try {
      setGateway(await adManagerApi.gatewayStatus(projectId, nextAccountId));
    } catch (error) {
      setFeedback({ kind: 'error', text: saveErrorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    const countries = allowedCountries.split(',').map((country) => country.trim().toUpperCase()).filter(Boolean);
    const excluded = excludedCountries.split(',').map((country) => country.trim().toUpperCase()).filter(Boolean);
    const languages = requiredLanguages.split(',').map((language) => language.trim()).filter(Boolean);
    const destinationReady = productionMode
      ? Boolean(waba && phone && dataset)
      : experimentalGatewayEnabled && Boolean(gatewayAccountId)
        && gateway?.status === 'Connected' && Boolean(gateway.phoneNumber);
    const currency = selectedAccount?.currency?.trim() ?? '';
    const reportingTimezone = selectedAccount?.timezone?.trim() ?? '';

    if (!account || !page || !destinationReady || !currency || !validTimezone(reportingTimezone) || dailyCap <= 0 || monthlyCap < dailyCap || countries.length === 0 || minimumAge < 18) {
      setFeedback({
        kind: 'error',
        text: !currency || !validTimezone(reportingTimezone)
          ? 'لم يرجع Meta عملة وتوقيتًا صالحين لحساب الإعلانات. أوقفنا الحفظ حتى لا نعرض أو ننفذ ميزانية بوحدة خاطئة.'
          : productionMode
          ? 'أكمل حساب الإعلانات والصفحة وWABA ورقم واتساب وDataset، ثم أدخل سقفًا شهريًا لا يقل عن اليومي ودولة وعمرًا صالحين.'
          : 'اربط Gateway في بيئة الاختبار وأكمل الحساب والصفحة والسقف والدولة والعمر.',
      });
      return;
    }

    setBusy(true);
    setFeedback(null);
    try {
      const destination = await adManagerApi.selectConnection(projectId, {
        adAccountId: account,
        pageId: page,
        wabaId: productionMode ? waba : undefined,
        phoneNumberId: productionMode ? phone : gateway?.phoneNumber ?? undefined,
        datasetId: productionMode ? dataset : undefined,
        whatsAppAccountId: productionMode ? undefined : gatewayAccountId,
        integrationMode,
      });
      const offers = await adManagerApi.offers(projectId);
      const offer = offers.find((item) => item.state === 'Eligible');
      if (!offer) {
        setFeedback({ kind: 'error', text: 'تم توثيق الموارد، لكن لا يوجد عرض مؤهل. أضف عرضًا موثقًا في عقل الشركة قبل تفويض الصرف.' });
        await onSaved('تم توثيق موارد Meta وواتساب دون تفعيل الصرف.');
        return;
      }
      const envelope = await adManagerApi.saveEnvelope(projectId, {
        offerId: offer.id,
        destinationId: destination.destinationId,
        dailyCap,
        periodCap: monthlyCap,
        periodCapKind: 'Monthly',
        currency,
        safetyReservePercent: 15,
        maximumIncreasePercent: 20,
        cooldownHours: 24,
        allowedCountries: countries,
        excludedCountries: excluded,
        minimumAge,
        requiredLanguages: languages,
        customAudienceExclusions: [],
        reportingTimezoneIana: reportingTimezone,
      });
      setPendingEnvelope({ id: envelope.id, version: envelope.version, state: envelope.state });
      setFeedback({ kind: 'status', text: 'تم حفظ الموارد والسقف كمسودة. راجع الملخص ثم فعّل التفويض بشكل منفصل.' });
      await onSaved('تم حفظ إعدادات Meta وواتساب دون تشغيل الصرف.');
    } catch (error) {
      setFeedback({ kind: 'error', text: saveErrorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  const activateEnvelope = async () => {
    if (!pendingEnvelope) return;
    setConfirmActivation(false);
    setBusy(true);
    setFeedback(null);
    try {
      await adManagerApi.activateEnvelope(projectId, pendingEnvelope.id, pendingEnvelope.version);
      setPendingEnvelope((current) => current ? { ...current, state: 'Active' } : current);
      setFeedback({ kind: 'status', text: 'تم تفعيل تفويض الميزانية. ما زالت أوامر الصرف خاضعة لفحوص الجاهزية والأمان.' });
      await onSaved('تم تفعيل تفويض الميزانية بعد مراجعة السقف والموارد.');
    } catch (error) {
      setFeedback({ kind: 'error', text: saveErrorMessage(error) });
    } finally {
      setBusy(false);
    }
  };

  return <>
    <div className={styles.connectionPanel}>
      <div>
        <Megaphone size={22} />
        <h2>Meta وWhatsApp Business</h2>
        <p>اختر موارد Meta الرسمية للإعلانات والتتبع. الوضع التجريبي لا يظهر إلا عند تفعيله صراحةً في بيئة الاختبار.</p>
      </div>

      {connectionLoadState === 'loading' ? (
        <div className={styles.formNote} role="status"><LoaderCircle className={styles.spin} size={17} /> جارٍ تحميل حالة ربط Meta…</div>
      ) : connectionLoadState === 'error' ? (
        <button type="button" className={styles.secondaryButton} onClick={() => setLoadAttempt((attempt) => attempt + 1)} disabled={busy}>
          <RefreshCw size={17} /> إعادة تحميل حالة الربط
        </button>
      ) : connectionLoadState === 'disconnected' ? (
        <button type="button" className={styles.primaryButton} onClick={() => void connect()} disabled={busy}>
          {busy ? <LoaderCircle className={styles.spin} size={17} /> : <Megaphone size={17} />} ربط حساب Meta
        </button>
      ) : resources ? (
        <div className={styles.formGrid}>
          <label>حساب الإعلانات<select value={account} onChange={(event) => void changeAccount(event.target.value)} disabled={busy}>{resources.adAccounts.map((item) => <option key={item.id} value={item.id}>{item.name} · {item.currency || 'العملة غير متاحة'}</option>)}</select></label>
          <label>صفحة Facebook<select value={page} onChange={(event) => setPage(event.target.value)}>{resources.pages.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
          <label>طريقة استقبال واتساب<select value={integrationMode} onChange={(event) => setIntegrationMode(event.target.value as IntegrationMode)}>
            <option value="CloudApiCoexistence">Cloud API Coexistence (موصى به)</option>
            <option value="CloudApi">WhatsApp Cloud API</option>
            {experimentalGatewayEnabled && <option value="BaileysObservedExperimental">Baileys Gateway (اختبار داخلي فقط)</option>}
          </select></label>

          {productionMode ? <>
            <label>حساب WhatsApp Business<select value={waba} onChange={(event) => changeWaba(event.target.value)}><option value="">اختر WABA</option>{resources.wabas.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
            <label>رقم واتساب<select value={phone} onChange={(event) => setPhone(event.target.value)}><option value="">اختر الرقم</option>{selectedWaba?.phones.map((item) => <option key={item.id} value={item.id}>{item.displayPhoneNumber} · {item.verifiedName}</option>)}</select></label>
            <label>Dataset للتحويلات<select value={dataset} onChange={(event) => setDataset(event.target.value)}><option value="">اختر Dataset</option>{resources.datasets.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
            <div className={styles.formNote}><ShieldCheck size={17} /> WABA والرقم وDataset مطلوبة لإثبات الإسناد وإرسال أحداث Business Messaging بأمان.</div>
          </> : <>
            <label>حساب واتساب Gateway<select value={gatewayAccountId} onChange={(event) => void changeGatewayAccount(event.target.value)} disabled={busy}>
              <option value="">اختر حساب واتساب</option>
              {gatewayAccounts.map((candidate) => <option key={candidate.id} value={candidate.id}>
                {candidate.name}{candidate.isDefault ? ' · افتراضي' : ''}
              </option>)}
            </select></label>
            <div className={styles.formNote}><ShieldCheck size={17} /> هذا وضع اختبار داخلي. Gateway: {gateway?.status === 'Connected' ? <>متصل على <b dir="ltr">+{gateway.phoneNumber}</b></> : 'غير متصل'}. لا يُعامل كإثبات CAPI إنتاجي.</div>
          </>}

          <label>السقف اليومي<input type="number" min="1" step="1" value={dailyCap} onChange={(event) => setDailyCap(Number(event.target.value))} /></label>
          <label>السقف الشهري<input type="number" min={dailyCap || 1} step="1" value={monthlyCap} onChange={(event) => setMonthlyCap(Number(event.target.value))} /></label>
          <label>الدول المسموحة (ISO)<input value={allowedCountries} onChange={(event) => setAllowedCountries(event.target.value)} placeholder="EG, SA" dir="ltr" /></label>
          <label>الدول المستبعدة<input value={excludedCountries} onChange={(event) => setExcludedCountries(event.target.value)} placeholder="اتركها فارغة إن لم توجد" dir="ltr" /></label>
          <label>الحد الأدنى للعمر<input type="number" min="18" max="65" value={minimumAge} onChange={(event) => setMinimumAge(Number(event.target.value))} /></label>
          <label>اللغات المطلوبة (اختياري)<input value={requiredLanguages} onChange={(event) => setRequiredLanguages(event.target.value)} placeholder="ar, en" dir="ltr" /></label>
          <div className={styles.formNote}><ShieldCheck size={17} /> الغياب أو الغموض في referral يظل «غير منسوب» ولا يتم تخمينه. كل تفعيل يخضع للسقف وهامش أمان 15%.</div>
          <button type="button" className={styles.primaryButton} onClick={() => void save()} disabled={busy}>حفظ الموارد والسقف</button>
          {pendingEnvelope && pendingEnvelope.state !== 'Active' && (
            <button type="button" className={styles.secondaryButton} onClick={() => setConfirmActivation(true)} disabled={busy}>مراجعة وتفعيل التفويض</button>
          )}
          {pendingEnvelope?.state === 'Active' && <p className={styles.inlineMessage} role="status">تفويض الميزانية نشط حاليًا.</p>}
        </div>
      ) : null}

      {feedback && <p className={feedback.kind === 'error' ? styles.inlineError : styles.inlineMessage} role={feedback.kind === 'error' ? 'alert' : 'status'} aria-live="polite">{feedback.text}</p>}
    </div>

    <ConfirmDialog
      isOpen={confirmActivation}
      title="تفعيل تفويض الميزانية؟"
      message={`الحساب: ${selectedAccount?.name ?? 'غير محدد'}، الصفحة: ${selectedPage?.name ?? 'غير محددة'}، WABA: ${selectedWaba?.name ?? 'غير محدد'}، الرقم: ${selectedPhone?.displayPhoneNumber ?? 'غير محدد'}، Dataset: ${selectedDataset?.name ?? 'غير محدد'}. السقف اليومي: ${dailyCap} ${selectedAccount?.currency ?? ''}، الشهري: ${monthlyCap} ${selectedAccount?.currency ?? ''}. التفعيل يسمح بالأوامر المؤهلة داخل هذه الحدود فقط.`}
      confirmLabel="فعّل التفويض"
      onCancel={() => setConfirmActivation(false)}
      onConfirm={() => void activateEnvelope()}
    />
  </>;
}
