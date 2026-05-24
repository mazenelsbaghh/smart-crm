'use client';

import React, { useEffect, useState } from 'react';
import { useAuth } from '../../../context/auth-context';
import { crmService, Customer } from '../../../services/crm';
import CustomerDetail from '../../../components/CustomerDetail';
import { 
  Search, 
  Filter, 
  MapPin, 
  DollarSign, 
  Tag as TagIcon, 
  UserCheck, 
  Sparkles,
  Edit2
} from 'lucide-react';

export default function CRMPage() {
  const { activeProject } = useAuth();
  
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  
  // Search & Filter
  const [search, setSearch] = useState('');
  const [stageFilter, setStageFilter] = useState('All');
  const [cityFilter, setCityFilter] = useState('All');
  
  // Modal state
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);

  const fetchCustomers = async () => {
    if (!activeProject) return;
    try {
      setLoading(true);
      const data = await crmService.getCustomers(activeProject.id);
      setCustomers(data);
    } catch (e) {
      console.error('Failed to load CRM customers', e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchCustomers();
  }, [activeProject]);

  if (loading) {
    return (
      <div style={styles.loadingBox}>
        <div style={styles.spinner}></div>
        <p>Retrieving customer registry...</p>
      </div>
    );
  }

  // Extract unique stages and cities for filters
  const stages = ['All', ...Array.from(new Set(customers.map(c => c.pipelineStage).filter(Boolean)))];
  const cities = ['All', ...Array.from(new Set(customers.map(c => c.city).filter(Boolean)))];

  // Filtered List
  const filteredCustomers = customers.filter(c => {
    const matchesSearch = 
      (c.name || '').toLowerCase().includes(search.toLowerCase()) || 
      (c.phoneNumber || '').includes(search);
    
    const matchesStage = stageFilter === 'All' || c.pipelineStage === stageFilter;
    const matchesCity = cityFilter === 'All' || c.city === cityFilter;

    return matchesSearch && matchesStage && matchesCity;
  });

  return (
    <div style={styles.container}>
      {/* Title */}
      <div style={styles.header}>
        <div>
          <h1 style={styles.pageTitle}>Customers CRM</h1>
          <p style={styles.pageSubtitle}>Review and manage qualified customer contexts, scores, and tags</p>
        </div>
      </div>

      {/* Search and Filters panel */}
      <div className="glass-panel" style={styles.filterBar}>
        <div style={styles.searchWrapper}>
          <Search size={18} style={styles.searchIcon} />
          <input 
            type="text" 
            placeholder="Search by name or phone..." 
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={styles.searchInput}
          />
        </div>

        <div style={styles.filtersGroup}>
          <div style={styles.filterSelectWrapper}>
            <UserCheck size={16} style={styles.filterIcon} />
            <select 
              value={stageFilter} 
              onChange={(e) => setStageFilter(e.target.value)}
              style={styles.filterSelect}
            >
              {stages.map(st => (
                <option key={st} value={st}>{st === 'All' ? 'All Stages' : st}</option>
              ))}
            </select>
          </div>

          <div style={styles.filterSelectWrapper}>
            <MapPin size={16} style={styles.filterIcon} />
            <select 
              value={cityFilter} 
              onChange={(e) => setCityFilter(e.target.value)}
              style={styles.filterSelect}
            >
              {cities.map(ct => (
                <option key={ct} value={ct}>{ct === 'All' ? 'All Cities' : ct}</option>
              ))}
            </select>
          </div>
        </div>
      </div>

      {/* Customers Table / Grid */}
      <div className="glass-panel" style={styles.tablePanel}>
        {filteredCustomers.length === 0 ? (
          <div style={styles.emptyTable}>No customers match current criteria.</div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={styles.table}>
              <thead>
                <tr>
                  <th style={styles.th}>Customer</th>
                  <th style={styles.th}>Location</th>
                  <th style={styles.th}>Lead Score</th>
                  <th style={styles.th}>Stage</th>
                  <th style={styles.th}>Budget</th>
                  <th style={styles.th}>Tags</th>
                  <th style={{ ...styles.th, textAlign: 'center' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredCustomers.map(c => (
                  <tr key={c.id} style={styles.tr}>
                    <td style={styles.td}>
                      <div style={styles.customerCell}>
                        <div style={styles.avatar}>
                          {(c.name || 'C').charAt(0).toUpperCase()}
                        </div>
                        <div style={styles.customerNameBox}>
                          <span style={styles.customerName}>{c.name || 'Anonymous Customer'}</span>
                          <span style={styles.customerPhone}>{c.phoneNumber}</span>
                        </div>
                      </div>
                    </td>
                    <td style={styles.td}>
                      <span style={styles.locationText}>
                        {c.city ? (
                          <>
                            <MapPin size={12} style={{ marginRight: '4px', verticalAlign: 'middle' }} />
                            {c.city}
                          </>
                        ) : (
                          <span style={{ color: 'hsl(var(--text-muted))' }}>Not set</span>
                        )}
                      </span>
                    </td>
                    <td style={styles.td}>
                      <div style={styles.scoreBox}>
                        <Sparkles size={12} style={{ color: 'hsl(var(--accent-secondary))', marginRight: '4px' }} />
                        <span style={styles.scoreVal}>{c.leadScore || 0}</span>
                      </div>
                    </td>
                    <td style={styles.td}>
                      <span style={{
                        ...styles.stageBadge,
                        backgroundColor: c.pipelineStage === 'Won' ? 'rgba(16, 185, 129, 0.15)' : 
                                         c.pipelineStage === 'Lost' ? 'rgba(239, 68, 68, 0.15)' : 'rgba(255,255,255,0.04)',
                        color: c.pipelineStage === 'Won' ? 'hsl(140, 100%, 65%)' : 
                               c.pipelineStage === 'Lost' ? 'hsl(0, 100%, 65%)' : 'hsl(var(--text-secondary))'
                      }}>
                        {c.pipelineStage || 'New'}
                      </span>
                    </td>
                    <td style={styles.td}>
                      <span style={styles.budgetText}>
                        {c.budget ? `$${c.budget.toLocaleString()}` : <span style={{ color: 'hsl(var(--text-muted))' }}>-</span>}
                      </span>
                    </td>
                    <td style={styles.td}>
                      <div style={styles.tagsContainer}>
                        {c.tags && c.tags.slice(0, 2).map(tag => (
                          <span key={tag} style={styles.tagBadge}>{tag}</span>
                        ))}
                        {c.tags && c.tags.length > 2 && (
                          <span style={styles.tagMore}>+{c.tags.length - 2}</span>
                        )}
                      </div>
                    </td>
                    <td style={{ ...styles.td, textAlign: 'center' }}>
                      <button 
                        onClick={() => setSelectedCustomerId(c.id)} 
                        style={styles.editButton}
                        title="Edit profile"
                      >
                        <Edit2 size={14} />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Customer Detail Drawer Modal */}
      {selectedCustomerId && activeProject && (
        <CustomerDetail 
          customerId={selectedCustomerId}
          projectId={activeProject.id}
          onClose={() => setSelectedCustomerId(null)}
          onUpdate={fetchCustomers}
        />
      )}
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
  header: {
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
  filterBar: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: 'var(--space-md) var(--space-lg)',
    gap: 'var(--space-md)',
    flexWrap: 'wrap',
  },
  searchWrapper: {
    position: 'relative',
    flexGrow: 1,
    minWidth: '240px',
  },
  searchIcon: {
    position: 'absolute',
    left: '12px',
    top: '50%',
    transform: 'translateY(-50%)',
    color: 'hsl(var(--text-muted))',
  },
  searchInput: {
    width: '100%',
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-primary))',
    padding: 'var(--space-sm) var(--space-md) var(--space-sm) 40px',
    fontSize: '0.9rem',
    outline: 'none',
    transition: 'var(--transition-normal)',
  },
  filtersGroup: {
    display: 'flex',
    gap: 'var(--space-sm)',
    flexWrap: 'wrap',
  },
  filterSelectWrapper: {
    position: 'relative',
    display: 'flex',
    alignItems: 'center',
  },
  filterIcon: {
    position: 'absolute',
    left: '12px',
    color: 'hsl(var(--text-muted))',
    pointerEvents: 'none',
  },
  filterSelect: {
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.08)',
    borderRadius: 'var(--radius-md)',
    color: 'hsl(var(--text-primary))',
    padding: 'var(--space-sm) var(--space-lg) var(--space-sm) 36px',
    fontSize: '0.875rem',
    cursor: 'pointer',
    outline: 'none',
  },
  tablePanel: {
    padding: 'var(--space-xs)',
  },
  emptyTable: {
    padding: 'var(--space-xl)',
    textAlign: 'center',
    color: 'hsl(var(--text-muted))',
    fontSize: '0.95rem',
  },
  table: {
    width: '100%',
    borderCollapse: 'collapse',
    textAlign: 'left',
  },
  th: {
    padding: 'var(--space-md) var(--space-lg)',
    fontSize: '0.8rem',
    fontWeight: 700,
    color: 'hsl(var(--text-muted))',
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
  },
  tr: {
    borderBottom: '1px solid rgba(255, 255, 255, 0.03)',
    transition: 'var(--transition-fast)',
    cursor: 'pointer',
  },
  td: {
    padding: 'var(--space-md) var(--space-lg)',
    fontSize: '0.9rem',
    verticalAlign: 'middle',
  },
  customerCell: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-md)',
  },
  avatar: {
    width: '36px',
    height: '36px',
    borderRadius: 'var(--radius-full)',
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    color: 'hsl(var(--accent-primary))',
    border: '1px solid rgba(99, 102, 241, 0.3)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: 700,
  },
  customerNameBox: {
    display: 'flex',
    flexDirection: 'column',
  },
  customerName: {
    fontWeight: 600,
    color: 'hsl(var(--text-primary))',
  },
  customerPhone: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
  },
  locationText: {
    color: 'hsl(var(--text-secondary))',
    fontSize: '0.85rem',
  },
  scoreBox: {
    display: 'flex',
    alignItems: 'center',
  },
  scoreVal: {
    fontWeight: 700,
    color: 'hsl(var(--text-primary))',
  },
  stageBadge: {
    fontSize: '0.75rem',
    padding: '2px 8px',
    borderRadius: 'var(--radius-full)',
    fontWeight: 600,
  },
  budgetText: {
    fontWeight: 600,
    color: 'hsl(var(--text-primary))',
  },
  tagsContainer: {
    display: 'flex',
    gap: '4px',
    alignItems: 'center',
  },
  tagBadge: {
    fontSize: '0.7rem',
    backgroundColor: 'rgba(255,255,255,0.03)',
    border: '1px solid rgba(255,255,255,0.06)',
    borderRadius: 'var(--radius-sm)',
    padding: '1px 6px',
    color: 'hsl(var(--text-secondary))',
  },
  tagMore: {
    fontSize: '0.7rem',
    color: 'hsl(var(--text-muted))',
    fontWeight: 600,
  },
  editButton: {
    backgroundColor: 'rgba(255,255,255,0.02)',
    border: '1px solid rgba(255,255,255,0.08)',
    borderRadius: 'var(--radius-sm)',
    color: 'hsl(var(--text-secondary))',
    padding: '6px',
    cursor: 'pointer',
    transition: 'var(--transition-fast)',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
};
