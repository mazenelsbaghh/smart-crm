'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../../context/auth-context';
import { crmService, Customer, Deal } from '../../../services/crm';
import { useRouter } from 'next/navigation';
import { 
  Users, 
  DollarSign, 
  Target, 
  TrendingUp, 
  Sparkles, 
  MessageSquare, 
  RefreshCw, 
  ArrowRight,
  UserCheck
} from 'lucide-react';

export default function DashboardPage() {
  const { activeProject } = useAuth();
  const router = useRouter();
  
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [deals, setDeals] = useState<Deal[]>([]);
  const [loading, setLoading] = useState(true);
  const [recalculating, setRecalculating] = useState(false);

  const fetchDashboardData = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const custData = await crmService.getCustomers(activeProject.id);
      const dealData = await crmService.getDeals(activeProject.id);
      setCustomers(custData);
      setDeals(dealData);
    } catch (err) {
      console.error('Failed to load dashboard data', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchDashboardData();
  }, [activeProject]);

  const handleRecalculate = async () => {
    if (!activeProject || recalculating) return;
    setRecalculating(true);
    try {
      await crmService.recalculateAnalytics(activeProject.id);
      await fetchDashboardData();
    } catch (err) {
      console.error('Failed to recalculate metrics', err);
    } finally {
      setRecalculating(false);
    }
  };

  if (loading) {
    return (
      <div style={styles.loadingBox}>
        <div style={styles.spinner}></div>
        <p>Loading operation analytics...</p>
      </div>
    );
  }

  // Calculate metrics
  const totalCustomers = customers.length;
  const activeDeals = deals.filter(d => d.status === 0).length;
  const closedWonDeals = deals.filter(d => d.status === 1);
  const revenue = closedWonDeals.reduce((sum, d) => sum + d.amount, 0);
  
  // Average Lead Score
  const avgLeadScore = totalCustomers > 0 
    ? Math.round(customers.reduce((sum, c) => sum + (c.leadScore || 0), 0) / totalCustomers) 
    : 0;

  // Recent 5 customers
  const recentCustomers = [...customers]
    .sort((a, b) => (b.leadScore || 0) - (a.leadScore || 0))
    .slice(0, 5);

  return (
    <div style={styles.container}>
      {/* Title Header */}
      <div style={styles.dashboardHeader}>
        <div>
          <h1 style={styles.pageTitle}>Analytics Overview</h1>
          <p style={styles.pageSubtitle}>Operational efficiency and sales health for {activeProject?.name}</p>
        </div>
        <button 
          onClick={handleRecalculate} 
          disabled={recalculating} 
          style={styles.refreshButton}
        >
          <RefreshCw size={16} className={recalculating ? 'spin-anim' : ''} />
          <span>{recalculating ? 'Syncing...' : 'Sync Metrics'}</span>
        </button>
      </div>

      {/* KPI Stats Cards */}
      <div style={styles.statsGrid}>
        <div className="glass-panel" style={styles.statCard}>
          <div style={styles.statIconContainer}>
            <Users size={24} style={{ color: 'hsl(var(--accent-primary))' }} />
          </div>
          <div>
            <span style={styles.statLabel}>Total Contacts</span>
            <h2 style={styles.statValue}>{totalCustomers}</h2>
          </div>
        </div>

        <div className="glass-panel" style={styles.statCard}>
          <div style={styles.statIconContainer}>
            <Target size={24} style={{ color: 'hsl(var(--accent-secondary))' }} />
          </div>
          <div>
            <span style={styles.statLabel}>Active Pipeline Deals</span>
            <h2 style={styles.statValue}>{activeDeals}</h2>
          </div>
        </div>

        <div className="glass-panel" style={styles.statCard}>
          <div style={styles.statIconContainer}>
            <DollarSign size={24} style={{ color: 'hsl(140, 100%, 65%)' }} />
          </div>
          <div>
            <span style={styles.statLabel}>Closed Revenue</span>
            <h2 style={styles.statValue}>${revenue.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</h2>
          </div>
        </div>

        <div className="glass-panel" style={styles.statCard}>
          <div style={styles.statIconContainer}>
            <TrendingUp size={24} style={{ color: 'hsl(200, 100%, 60%)' }} />
          </div>
          <div>
            <span style={styles.statLabel}>Avg Lead Score</span>
            <h2 style={styles.statValue}>{avgLeadScore}/100</h2>
          </div>
        </div>
      </div>

      {/* Two Column Grid */}
      <div style={styles.contentGrid}>
        {/* Left Column: Recent Hot Leads */}
        <div className="glass-panel" style={styles.leadsPanel}>
          <div style={styles.panelHeader}>
            <h3 style={styles.panelTitle}>Top Priority Leads</h3>
            <span style={styles.badge}>High Score First</span>
          </div>

          <div style={styles.leadsList}>
            {recentCustomers.length === 0 ? (
              <div style={styles.emptyLeads}>No customers found in database</div>
            ) : (
              recentCustomers.map(c => (
                <div key={c.id} style={styles.leadItem} onClick={() => router.push('/crm')}>
                  <div style={styles.leadDetails}>
                    <span style={styles.leadName}>{c.name || 'Anonymous Customer'}</span>
                    <span style={styles.leadPhone}>{c.phoneNumber}</span>
                  </div>
                  <div style={styles.leadRight}>
                    <span style={styles.leadStage}>{c.pipelineStage || 'New'}</span>
                    <div style={styles.scorePill}>
                      <Sparkles size={10} style={{ marginRight: '4px', color: 'hsl(var(--accent-secondary))' }} />
                      Score: {c.leadScore}
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>

        {/* Right Column: Quick actions / workflow shortcuts */}
        <div style={styles.actionsColumn}>
          <div className="glass-panel" style={styles.actionCard}>
            <h3 style={styles.panelTitle}>Quick Actions</h3>
            <div style={styles.actionsGrid}>
              <div 
                style={styles.actionShortcut}
                onClick={() => router.push('/inbox')}
              >
                <div style={styles.shortcutIconBg}>
                  <MessageSquare size={20} />
                </div>
                <div>
                  <h4 style={styles.shortcutTitle}>Inbox Manager</h4>
                  <p style={styles.shortcutDesc}>Chat with customers in real-time</p>
                </div>
                <ArrowRight size={16} style={styles.shortcutArrow} />
              </div>

              <div 
                style={styles.actionShortcut}
                onClick={() => router.push('/crm/pipeline')}
              >
                <div style={styles.shortcutIconBg}>
                  <UserCheck size={20} />
                </div>
                <div>
                  <h4 style={styles.shortcutTitle}>Deals Pipeline</h4>
                  <p style={styles.shortcutDesc}>Manage and drag sales deals</p>
                </div>
                <ArrowRight size={16} style={styles.shortcutArrow} />
              </div>
            </div>
          </div>

          {/* AI suggestion panel info */}
          <div className="glass-panel" style={styles.aiPanel}>
            <div style={styles.aiHeader}>
              <Sparkles size={20} style={{ color: 'hsl(var(--accent-secondary))' }} />
              <h3 style={{...styles.panelTitle, margin: 0}}>Co-Pilot Assistant</h3>
            </div>
            <p style={styles.aiText}>
              Gemini 1.5 Flash Co-Pilot is currently active. Incoming customer messages are automatically analyzed for budget, interests, city locations, and sentiments. High-confidence CRM proposals are auto-saved, while low-confidence fields queue up in your Approvals section.
            </p>
            <div style={styles.aiIndicator}>
              <div style={styles.pulseDot}></div>
              <span>Listening for incoming webhooks...</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-lg)',
  },
  loadingBox: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 'var(--space-xl)',
    color: 'hsl(var(--text-secondary))',
    gap: 'var(--space-md)',
  },
  spinner: {
    width: '30px',
    height: '30px',
    border: '2px solid rgba(255, 255, 255, 0.05)',
    borderTopColor: 'hsl(var(--accent-primary))',
    borderRadius: '50%',
    animation: 'spin 1s linear infinite',
  },
  dashboardHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  pageTitle: {
    margin: 0,
    fontSize: '1.75rem',
    fontWeight: 800,
    color: 'hsl(var(--text-primary))',
  },
  pageSubtitle: {
    margin: 'var(--space-xs) 0 0 0',
    fontSize: '0.9rem',
    color: 'hsl(var(--text-muted))',
  },
  refreshButton: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-sm)',
    backgroundColor: 'rgba(255,255,255,0.03)',
    border: '1px solid rgba(255,255,255,0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-primary))',
    padding: 'var(--space-sm) var(--space-md)',
    fontSize: '0.875rem',
    fontWeight: 600,
    cursor: 'pointer',
    transition: 'var(--transition-normal)',
  },
  statsGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    gap: 'var(--space-md)',
  },
  statCard: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-md)',
    padding: 'var(--space-lg)',
  },
  statIconContainer: {
    width: '48px',
    height: '48px',
    borderRadius: 'var(--radius-md)',
    backgroundColor: 'rgba(255,255,255,0.02)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    border: '1px solid rgba(255, 255, 255, 0.04)',
  },
  statLabel: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
    fontWeight: 600,
    textTransform: 'uppercase',
  },
  statValue: {
    margin: '4px 0 0 0',
    fontSize: '1.5rem',
    fontWeight: 800,
    color: 'hsl(var(--text-primary))',
  },
  contentGrid: {
    display: 'grid',
    gridTemplateColumns: '1.2fr 1fr',
    gap: 'var(--space-lg)',
    '@media (max-width: 992px)': {
      gridTemplateColumns: '1fr',
    },
  } as React.CSSProperties,
  leadsPanel: {
    padding: 'var(--space-lg)',
    display: 'flex',
    flexDirection: 'column',
  },
  panelHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 'var(--space-md)',
  },
  panelTitle: {
    margin: 0,
    fontSize: '1.05rem',
    fontWeight: 700,
    color: 'hsl(var(--text-primary))',
  },
  badge: {
    fontSize: '0.7rem',
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    color: 'hsl(var(--accent-primary))',
    padding: '3px 8px',
    borderRadius: 'var(--radius-full)',
    fontWeight: 600,
  },
  leadsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
    flexGrow: 1,
  },
  emptyLeads: {
    padding: 'var(--space-xl)',
    textAlign: 'center',
    color: 'hsl(var(--text-muted))',
    fontSize: '0.9rem',
    border: '1px dashed rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-md)',
  },
  leadItem: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: 'var(--space-sm) var(--space-md)',
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.04)',
    borderRadius: 'var(--radius-md)',
    cursor: 'pointer',
    transition: 'var(--transition-fast)',
  },
  leadDetails: {
    display: 'flex',
    flexDirection: 'column',
  },
  leadName: {
    fontSize: '0.9rem',
    fontWeight: 600,
    color: 'hsl(var(--text-primary))',
  },
  leadPhone: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
  },
  leadRight: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-md)',
  },
  leadStage: {
    fontSize: '0.75rem',
    backgroundColor: 'rgba(255,255,255,0.04)',
    color: 'hsl(var(--text-secondary))',
    padding: '2px 8px',
    borderRadius: 'var(--radius-sm)',
    fontWeight: 500,
  },
  scorePill: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-primary))',
    fontWeight: 700,
    display: 'flex',
    alignItems: 'center',
  },
  actionsColumn: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-lg)',
  },
  actionCard: {
    padding: 'var(--space-lg)',
  },
  actionsGrid: {
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
    marginTop: 'var(--space-md)',
  },
  actionShortcut: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-md)',
    padding: 'var(--space-md)',
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.04)',
    borderRadius: 'var(--radius-md)',
    cursor: 'pointer',
    transition: 'var(--transition-normal)',
    position: 'relative',
  },
  shortcutIconBg: {
    width: '40px',
    height: '40px',
    borderRadius: 'var(--radius-md)',
    backgroundColor: 'rgba(99, 102, 241, 0.1)',
    color: 'hsl(var(--accent-primary))',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  shortcutTitle: {
    margin: 0,
    fontSize: '0.9rem',
    fontWeight: 700,
    color: 'hsl(var(--text-primary))',
  },
  shortcutDesc: {
    margin: '2px 0 0 0',
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
  },
  shortcutArrow: {
    position: 'absolute',
    right: 'var(--space-md)',
    color: 'hsl(var(--text-muted))',
    transition: 'var(--transition-fast)',
  },
  aiPanel: {
    padding: 'var(--space-lg)',
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
  },
  aiHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-sm)',
  },
  aiText: {
    margin: 0,
    fontSize: '0.85rem',
    lineHeight: 1.5,
    color: 'hsl(var(--text-secondary))',
  },
  aiIndicator: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-sm)',
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
    marginTop: 'var(--space-xs)',
  },
  pulseDot: {
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    backgroundColor: 'hsl(140, 100%, 60%)',
    boxShadow: '0 0 8px hsl(140, 100%, 60%)',
    animation: 'pulse 1.5s infinite',
  },
};
