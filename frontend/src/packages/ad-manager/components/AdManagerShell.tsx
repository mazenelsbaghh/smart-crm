import type { ReactNode } from 'react';
import Tabs from '../../../components/ui/Tabs';
import PageHeader from '../../../components/ui/PageHeader';
import Button from '../../../components/ui/Button';
import { Bot, Film, FlaskConical, LayoutDashboard, Megaphone, MessageCircleMore, RefreshCw, Settings2, Target, Users } from 'lucide-react';
import { ControlStrip } from './ControlStrip';
import type { StopState } from '../types';
import styles from '../AdManager.module.css';

export interface AdManagerTab { key: string; label: string }
const tabIcons: Record<string, ReactNode> = {
  overview: <LayoutDashboard size={17} aria-hidden="true" />,
  strategy: <Target size={17} aria-hidden="true" />,
  campaigns: <Megaphone size={17} aria-hidden="true" />,
  audiences: <Users size={17} aria-hidden="true" />,
  creatives: <Film size={17} aria-hidden="true" />,
  experiments: <FlaskConical size={17} aria-hidden="true" />,
  conversions: <MessageCircleMore size={17} aria-hidden="true" />,
  decisions: <Bot size={17} aria-hidden="true" />,
  settings: <Settings2 size={17} aria-hidden="true" />,
};
interface Props {
  tabs: readonly AdManagerTab[]; activeTab: string; busy: boolean; refreshing?: boolean; ready: boolean; autopilot: boolean;
  emergencyStop: boolean; continuingSpend?: boolean; updatedAt?: string; notice?: string | null;
  reportingTimezone?: string; canManage?: boolean;
  stopState?: StopState | null;
  error?: string | null; onTabChange: (key: string) => void; onRefresh: () => void; onStop: () => void;
  onResume: () => void; children: ReactNode;
}

export function AdManagerShell(props: Props) {
  const canManage = props.canManage ?? true;
  const tabId = (key: string) => `ad-manager-tab-${key}`;
  const panelId = `ad-manager-panel-${props.activeTab}`;

  return <section className={styles.workspace} dir="rtl">
    <PageHeader title="مدير الإعلانات" icon={<Megaphone size={24} aria-hidden="true" />} description="الحملات التي تفتح واتساب، نتائجها، وقرارات تحسينها"
      actions={<Button onClick={props.onRefresh} disabled={props.busy} loading={props.refreshing}
        aria-label={props.refreshing ? 'جارٍ تحديث بيانات مدير الإعلانات' : 'تحديث بيانات مدير الإعلانات'}>
        <RefreshCw size={17} aria-hidden="true" />{props.refreshing ? 'جارٍ التحديث' : 'تحديث'}
      </Button>} />
    <ControlStrip ready={props.ready} autopilot={props.autopilot} emergencyStop={props.emergencyStop}
      continuingSpend={props.continuingSpend} busy={props.busy} updatedAt={props.updatedAt}
      stopState={props.stopState} reportingTimezone={props.reportingTimezone} canManage={canManage}
      onStop={props.onStop} onResume={props.onResume} />
    {props.notice && <div className={styles.notice} role="status" aria-live="polite">{props.notice}</div>}
    {props.error && <div className={styles.error} role="alert">{props.error}</div>}
    <Tabs idPrefix="ad-manager" label="أقسام مدير الإعلانات" compactOnMobile tabs={props.tabs.map(tab => ({ ...tab, icon: tabIcons[tab.key] }))} selected={props.activeTab} onChange={props.onTabChange} />
    <div id={panelId} role="tabpanel" aria-labelledby={tabId(props.activeTab)}>{props.children}</div>
  </section>;
}
