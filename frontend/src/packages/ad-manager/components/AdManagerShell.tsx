import { useRef, type ReactNode } from 'react';
import { RefreshCw } from 'lucide-react';
import { ControlStrip } from './ControlStrip';
import type { StopState } from '../types';
import styles from '../AdManager.module.css';

export interface AdManagerTab { key: string; label: string }
interface Props {
  tabs: readonly AdManagerTab[]; activeTab: string; busy: boolean; refreshing?: boolean; ready: boolean; autopilot: boolean;
  emergencyStop: boolean; continuingSpend?: boolean; updatedAt?: string; notice?: string | null;
  reportingTimezone?: string; canManage?: boolean;
  stopState?: StopState | null;
  error?: string | null; onTabChange: (key: string) => void; onRefresh: () => void; onStop: () => void;
  onResume: () => void; children: ReactNode;
}

export function AdManagerShell(props: Props) {
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const canManage = props.canManage ?? true;
  const tabId = (key: string) => `ad-manager-tab-${key}`;
  const panelId = `ad-manager-panel-${props.activeTab}`;

  return <section className={styles.workspace} dir="rtl">
    <header className={styles.header}><div><p className={styles.eyebrow}>META AI MEDIA BUYER • WHATSAPP ONLY</p>
      <h1>مدير الإعلانات</h1><p>ينشئ ويختبر ويحسّن إعلانات Meta التي تفتح واتساب، ويحاسب نفسه على العميل المؤهل والبيع الحقيقي.</p></div>
      <div className={styles.headerActions}><button className={styles.secondaryButton} onClick={props.onRefresh}
        disabled={props.busy || props.refreshing} aria-busy={props.refreshing}
        aria-label={props.refreshing ? 'جارٍ تحديث بيانات مدير الإعلانات' : 'تحديث بيانات مدير الإعلانات'}>
        <RefreshCw size={17} aria-hidden="true" className={props.refreshing ? styles.spin : undefined} />
        {props.refreshing ? 'جارٍ التحديث' : 'تحديث'}
      </button></div></header>
    <ControlStrip ready={props.ready} autopilot={props.autopilot} emergencyStop={props.emergencyStop}
      continuingSpend={props.continuingSpend} busy={props.busy} updatedAt={props.updatedAt}
      stopState={props.stopState} reportingTimezone={props.reportingTimezone} canManage={canManage}
      onStop={props.onStop} onResume={props.onResume} />
    {props.notice && <div className={styles.notice} role="status" aria-live="polite">{props.notice}</div>}
    {props.error && <div className={styles.error} role="alert">{props.error}</div>}
    <nav className={styles.tabs} aria-label="أقسام مدير الإعلانات" role="tablist">
      {props.tabs.map((tab, index) => <button key={tab.key} ref={(node) => { tabRefs.current[tab.key] = node; }}
        id={tabId(tab.key)} role="tab" aria-selected={props.activeTab === tab.key}
        aria-controls={`ad-manager-panel-${tab.key}`} tabIndex={props.activeTab === tab.key ? 0 : -1}
        className={props.activeTab === tab.key ? styles.activeTab : ''} onClick={() => props.onTabChange(tab.key)}
        onKeyDown={(event) => {
          if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
          event.preventDefault();
          const nextIndex = event.key === 'Home' ? 0 : event.key === 'End' ? props.tabs.length - 1
            : (index + (event.key === 'ArrowRight' ? -1 : 1) + props.tabs.length) % props.tabs.length;
          const nextKey = props.tabs[nextIndex].key;
          props.onTabChange(nextKey);
          window.requestAnimationFrame(() => tabRefs.current[nextKey]?.focus());
        }}>{tab.label}</button>)}
    </nav>
    <div id={panelId} role="tabpanel" aria-labelledby={tabId(props.activeTab)}>{props.children}</div>
  </section>;
}
