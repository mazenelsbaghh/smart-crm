'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useAuth } from '../../../context/auth-context';
import { api } from '../../../services/api';
import { SignalRService } from '../../../services/signalr';
import { Conversation, Message, AISuggestion, ConversationStatus } from '../../../types/chat';
import { 
  Search, 
  Send, 
  Paperclip, 
  Sparkles, 
  User, 
  Phone, 
  MapPin, 
  DollarSign, 
  Award, 
  Tag, 
  CheckSquare, 
  Inbox
} from 'lucide-react';

export default function InboxPage() {
  const { activeProject } = useAuth();
  
  // State
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeConv, setActiveConv] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [aiSuggestion, setAiSuggestion] = useState<AISuggestion | null>(null);
  
  // Panel 3 Customer Info editing
  const [customerName, setCustomerName] = useState('');
  const [customerCity, setCustomerCity] = useState('');
  const [customerBudget, setCustomerBudget] = useState('');
  const [customerLeadScore, setCustomerLeadScore] = useState(0);
  const [customerStage, setCustomerStage] = useState('');
  const [customerNotes, setCustomerNotes] = useState('');
  const [customerTags, setCustomerTags] = useState<string[]>([]);
  const [newTag, setNewTag] = useState('');
  const [savingCustomer, setSavingCustomer] = useState(false);

  // Message composer
  const [inputMessage, setInputMessage] = useState('');
  const [sending, setSending] = useState(false);
  const [filterStatus, setFilterStatus] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState('');

  // Refs
  const messageEndRef = useRef<HTMLDivElement>(null);
  const signalRServiceRef = useRef<SignalRService | null>(null);

  // Load Conversations
  const fetchConversations = async () => {
    if (!activeProject) return;
    try {
      const response = await api.get<Conversation[]>(`/api/projects/${activeProject.id}/conversations`);
      setConversations(response.data);
    } catch (e) {
      console.error('Error loading conversations', e);
    }
  };

  useEffect(() => {
    fetchConversations();
  }, [activeProject]);

  // Load Messages & Customer Details for Active Conversation
  useEffect(() => {
    const fetchActiveDetails = async () => {
      if (!activeConv) {
        setMessages([]);
        setAiSuggestion(null);
        return;
      }

      try {
        // Load messages
        const msgResp = await api.get<Message[]>(`/api/conversations/${activeConv.id}/messages`);
        setMessages(msgResp.data);

        // Load complete CRM customer profile
        const custResp = await api.get(`/api/customers/${activeConv.customer.id}`);
        const c = custResp.data;
        setCustomerName(c.name || '');
        setCustomerCity(c.city || '');
        setCustomerBudget(c.budget ? c.budget.toString() : '');
        setCustomerLeadScore(c.leadScore || 0);
        setCustomerStage(c.pipelineStage || 'New');
        setCustomerNotes(c.notes || '');
        setCustomerTags(c.tags || []);
        
        // Reset AI suggestion
        setAiSuggestion(null);
      } catch (e) {
        console.error('Error loading conversation details', e);
      }
    };

    fetchActiveDetails();
  }, [activeConv]);

  // Scroll to bottom of message list on updates
  useEffect(() => {
    messageEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // Initialize SignalR Connection
  useEffect(() => {
    if (!activeProject) return;
    const token = localStorage.getItem('accessToken');
    if (!token) return;

    const service = new SignalRService(activeProject.id, token);
    signalRServiceRef.current = service;

    const initSignalR = async () => {
      service.registerOnMessage((message: Message) => {
        // Check if message belongs to current active conversation
        if (activeConv && message.conversationId === activeConv.id) {
          setMessages((prev: Message[]) => {
            if (prev.find(m => m.id === message.id)) return prev;
            return [...prev, message];
          });
        }
        
        // Update conversation list preview
        setConversations((prev: Conversation[]) => {
          return prev.map((c) => {
            if (c.id === message.conversationId) {
              return {
                ...c,
                lastMessageAt: message.createdAt,
                unreadCount: activeConv?.id === c.id ? 0 : c.unreadCount + 1
              };
            }
            return c;
          });
        });
      });

      service.registerOnStatusChange((convId: string, status: ConversationStatus) => {
        setConversations((prev: Conversation[]) => 
          prev.map((c) => (c.id === convId ? { ...c, status } : c))
        );
        if (activeConv && activeConv.id === convId) {
          setActiveConv((prev: Conversation | null) => prev ? { ...prev, status } : null);
        }
      });

      service.registerOnAISuggestion((suggestion: AISuggestion) => {
        if (activeConv && suggestion.conversationId === activeConv.id) {
          setAiSuggestion(suggestion);
        }
      });

      try {
        await service.start();
        await service.updatePresence('Online');
      } catch (e) {
        console.error('Failed to connect to SignalR realtime gateway', e);
      }
    };

    initSignalR();

    return () => {
      service.updatePresence('Offline');
      service.stop();
      signalRServiceRef.current = null;
    };
  }, [activeProject, activeConv?.id]);

  // Handlers
  const handleSendMessage = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!inputMessage.trim() || !activeConv || sending) return;

    setSending(true);
    try {
      const response = await api.post(`/api/conversations/${activeConv.id}/messages`, {
        content: inputMessage,
      });
      // Append manually for instant feedback before SignalR round-trip (if SignalR isn't connected)
      setMessages((prev: Message[]) => {
        if (prev.find(m => m.id === response.data.id)) return prev;
        return [...prev, response.data];
      });
      setInputMessage('');
      setAiSuggestion(null); // Clear suggestion after sending
    } catch (error) {
      console.error('Failed to send reply message', error);
    } finally {
      setSending(false);
    }
  };

  const handleUpdateCustomer = async () => {
    if (!activeConv || savingCustomer) return;
    setSavingCustomer(true);
    try {
      await api.put(`/api/customers/${activeConv.customer.id}`, {
        name: customerName,
        city: customerCity,
        budget: customerBudget ? parseFloat(customerBudget) : null,
        leadScore: customerLeadScore,
        pipelineStage: customerStage,
        notes: customerNotes,
        tags: customerTags,
      });
      // Refresh list to update contact names if name changed
      fetchConversations();
    } catch (e) {
      console.error('Failed to update CRM customer profile', e);
    } finally {
      setSavingCustomer(false);
    }
  };

  const addTag = () => {
    if (!newTag.trim() || customerTags.includes(newTag.trim())) return;
    setCustomerTags([...customerTags, newTag.trim()]);
    setNewTag('');
  };

  const removeTag = (tagToRemove: string) => {
    setCustomerTags(customerTags.filter((t) => t !== tagToRemove));
  };

  const applyAISuggestion = (text: string) => {
    setInputMessage(text);
  };

  // Filters & Searches
  const filteredConversations = conversations.filter((c) => {
    const statusMatch = filterStatus === 'All' || c.status === filterStatus;
    const nameMatch = c.customer.name.toLowerCase().includes(searchQuery.toLowerCase()) || 
                      c.customer.phone.includes(searchQuery);
    return statusMatch && nameMatch;
  });

  return (
    <div style={styles.inboxContainer}>
      {/* Panel 1: Conversation List */}
      <div className="glass-panel" style={styles.convPanel}>
        <div style={styles.panelHeader}>
          <h3 style={styles.panelTitle}>Conversations</h3>
          <div style={styles.searchWrapper}>
            <Search size={16} style={styles.searchIcon} />
            <input
              type="text"
              placeholder="Search agent inbox..."
              className="neon-input"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              style={styles.searchInput}
            />
          </div>
          <div style={styles.filterBar}>
            {['All', 'Open', 'Pending', 'Resolved'].map((st) => (
              <button
                key={st}
                onClick={() => setFilterStatus(st)}
                style={{
                  ...styles.filterBtn,
                  ...(filterStatus === st ? styles.filterBtnActive : {}),
                }}
              >
                {st}
              </button>
            ))}
          </div>
        </div>

        <div style={styles.convList}>
          {filteredConversations.length === 0 ? (
            <div style={styles.emptyState}>No conversations found</div>
          ) : (
            filteredConversations.map((c) => (
              <div
                key={c.id}
                onClick={() => setActiveConv(c)}
                style={{
                  ...styles.convCard,
                  ...(activeConv?.id === c.id ? styles.convCardActive : {}),
                }}
              >
                <div style={styles.avatar}>
                  {c.customer.name.charAt(0).toUpperCase()}
                </div>
                <div style={styles.convMeta}>
                  <div style={styles.convNameRow}>
                    <span style={styles.convName}>{c.customer.name}</span>
                    <span style={styles.convTime}>
                      {new Date(c.lastMessageAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </span>
                  </div>
                  <div style={styles.convPreviewRow}>
                    <span style={styles.convPhone}>{c.customer.phone}</span>
                    {c.unreadCount > 0 && (
                      <span style={styles.unreadBadge}>{c.unreadCount}</span>
                    )}
                  </div>
                </div>
              </div>
            ))
          )}
        </div>
      </div>

      {/* Panel 2: Chat Log */}
      <div className="glass-panel" style={styles.chatPanel}>
        {activeConv ? (
          <>
            <div style={styles.chatHeader}>
              <div>
                <h4 style={styles.chatHeaderName}>{activeConv.customer.name}</h4>
                <p style={styles.chatHeaderPhone}>{activeConv.customer.phone}</p>
              </div>
              <div style={styles.chatHeaderStatus}>
                <span style={styles.statusLabel}>Status:</span>
                <select
                  value={activeConv.status}
                  onChange={async (e) => {
                    const nextStatus = e.target.value as ConversationStatus;
                    try {
                      await api.put(`/api/conversations/${activeConv.id}/status`, { status: nextStatus });
                    } catch (err) {
                      console.error('Failed to change status', err);
                    }
                  }}
                  style={styles.statusSelect}
                >
                  <option value="Open">Open</option>
                  <option value="Pending">Pending</option>
                  <option value="Resolved">Resolved</option>
                  <option value="Closed">Closed</option>
                </select>
              </div>
            </div>

            {/* Message Thread */}
            <div style={styles.messageThread}>
              {messages.length === 0 ? (
                <div style={styles.emptyState}>No messages in this chat</div>
              ) : (
                messages.map((m) => {
                  const isAgent = m.senderType === 'Agent';
                  const isAI = m.senderType === 'AI';
                  
                  let bubbleStyle = styles.msgBubbleCustomer;
                  let alignStyle = styles.msgRowCustomer;
                  
                  if (isAgent) {
                    bubbleStyle = styles.msgBubbleAgent;
                    alignStyle = styles.msgRowAgent;
                  } else if (isAI) {
                    bubbleStyle = styles.msgBubbleAI;
                    alignStyle = styles.msgRowAgent;
                  } else if (m.senderType === 'System') {
                    bubbleStyle = styles.msgBubbleSystem;
                    alignStyle = styles.msgRowSystem;
                  }

                  return (
                    <div key={m.id} style={alignStyle}>
                      <div style={bubbleStyle}>
                        {isAI && (
                          <div style={styles.aiBadgeRow}>
                            <Sparkles size={12} style={{ color: 'hsl(var(--accent-secondary))' }} />
                            <span>AI Co-Pilot</span>
                          </div>
                        )}
                        <p style={{ whiteSpace: 'pre-wrap' }}>{m.content}</p>
                        <span style={styles.msgTime}>
                          {new Date(m.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                        </span>
                      </div>
                    </div>
                  );
                })
              )}
              <div ref={messageEndRef} />
            </div>

            {/* AI Suggestion Box */}
            {aiSuggestion && (
              <div className="glass-panel" style={styles.aiSuggestionBox}>
                <div style={styles.aiSuggestionHeader}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-sm)' }}>
                    <Sparkles size={16} className="text-pink" />
                    <span style={{ fontWeight: 700, color: 'hsl(var(--text-primary))' }}>Gemini AI Suggested Reply</span>
                  </div>
                  <span style={styles.aiConfidence}>
                    {(aiSuggestion.confidenceScore * 100).toFixed(0)}% Match
                  </span>
                </div>
                <p style={styles.aiSuggestionText}>{aiSuggestion.suggestionText}</p>
                <div style={styles.aiSuggestionFooter}>
                  <p style={styles.aiReasoning}>{aiSuggestion.reasoning}</p>
                  <button 
                    onClick={() => applyAISuggestion(aiSuggestion.suggestionText)}
                    className="neon-btn-secondary"
                    style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                  >
                    Use Suggestion
                  </button>
                </div>
              </div>
            )}

            {/* Composer */}
            <form onSubmit={handleSendMessage} style={styles.composerForm}>
              <button type="button" style={styles.attachmentBtn}>
                <Paperclip size={20} />
              </button>
              <input
                type="text"
                placeholder="Type a message..."
                className="neon-input"
                value={inputMessage}
                onChange={(e) => setInputMessage(e.target.value)}
                style={styles.composerInput}
              />
              <button 
                type="submit" 
                className="neon-btn" 
                style={styles.sendBtn}
                disabled={!inputMessage.trim() || sending}
              >
                <Send size={18} />
              </button>
            </form>
          </>
        ) : (
          <div style={styles.noActiveChat}>
            <Inbox size={48} style={{ color: 'hsl(var(--text-muted))', marginBottom: 'var(--space-md)' }} />
            <h3>Select a Conversation</h3>
            <p style={{ color: 'hsl(var(--text-muted))' }}>Choose a contact from the list to start messaging.</p>
          </div>
        )}
      </div>

      {/* Panel 3: Customer Details Panel */}
      <div className="glass-panel" style={styles.detailsPanel}>
        {activeConv ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
            <h3 style={styles.detailsTitle}>Customer Details</h3>
            
            {/* Field: Name */}
            <div style={styles.detailField}>
              <label style={styles.detailLabel}><User size={14} /> Name</label>
              <input
                type="text"
                className="neon-input"
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
                style={styles.detailInput}
              />
            </div>

            {/* Field: Phone (Read Only) */}
            <div style={styles.detailField}>
              <label style={styles.detailLabel}><Phone size={14} /> WhatsApp Phone</label>
              <input
                type="text"
                className="neon-input"
                value={activeConv.customer.phone}
                disabled
                style={{ ...styles.detailInput, opacity: 0.6, cursor: 'not-allowed' }}
              />
            </div>

            {/* Field: City */}
            <div style={styles.detailField}>
              <label style={styles.detailLabel}><MapPin size={14} /> City</label>
              <input
                type="text"
                className="neon-input"
                placeholder="Not specified"
                value={customerCity}
                onChange={(e) => setCustomerCity(e.target.value)}
                style={styles.detailInput}
              />
            </div>

            {/* Field: Budget */}
            <div style={styles.detailField}>
              <label style={styles.detailLabel}><DollarSign size={14} /> Budget</label>
              <input
                type="number"
                className="neon-input"
                placeholder="Not specified"
                value={customerBudget}
                onChange={(e) => setCustomerBudget(e.target.value)}
                style={styles.detailInput}
              />
            </div>

            {/* Field: Lead Score & Pipeline Stage */}
            <div style={{ display: 'flex', gap: 'var(--space-sm)' }}>
              <div style={{ ...styles.detailField, flex: 1 }}>
                <label style={styles.detailLabel}><Award size={14} /> Lead Score</label>
                <input
                  type="number"
                  className="neon-input"
                  value={customerLeadScore}
                  onChange={(e) => setCustomerLeadScore(parseInt(e.target.value) || 0)}
                  style={styles.detailInput}
                />
              </div>

              <div style={{ ...styles.detailField, flex: 1.2 }}>
                <label style={styles.detailLabel}><CheckSquare size={14} /> CRM Stage</label>
                <select
                  className="neon-input"
                  value={customerStage}
                  onChange={(e) => setCustomerStage(e.target.value)}
                  style={styles.detailSelect}
                >
                  <option value="New">New</option>
                  <option value="Contacted">Contacted</option>
                  <option value="Qualified">Qualified</option>
                  <option value="Proposal">Proposal</option>
                  <option value="Negotiation">Negotiation</option>
                  <option value="Won">Won</option>
                  <option value="Lost">Lost</option>
                </select>
              </div>
            </div>

            {/* Field: Tags */}
            <div style={styles.detailField}>
              <label style={styles.detailLabel}><Tag size={14} /> Tags</label>
              <div style={styles.tagsContainer}>
                {customerTags.map((tag) => (
                  <span key={tag} style={styles.tagBadge}>
                    {tag}
                    <button onClick={() => removeTag(tag)} style={styles.tagRemoveBtn}>×</button>
                  </span>
                ))}
              </div>
              <div style={{ display: 'flex', gap: 'var(--space-xs)', marginTop: 'var(--space-xs)' }}>
                <input
                  type="text"
                  placeholder="Add tag..."
                  className="neon-input"
                  value={newTag}
                  onChange={(e) => setNewTag(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && addTag()}
                  style={{ ...styles.detailInput, flex: 1, padding: '4px 8px', fontSize: '0.8rem' }}
                />
                <button onClick={addTag} className="neon-btn-secondary" style={{ padding: '4px 8px', fontSize: '0.8rem' }}>Add</button>
              </div>
            </div>

            {/* Field: Notes */}
            <div style={styles.detailField}>
              <label style={styles.detailLabel}>Notes</label>
              <textarea
                className="neon-input"
                placeholder="Add conversation notes here..."
                value={customerNotes}
                onChange={(e) => setCustomerNotes(e.target.value)}
                style={styles.detailTextarea}
              />
            </div>

            {/* Save Profile Button */}
            <button
              onClick={handleUpdateCustomer}
              className="neon-btn"
              disabled={savingCustomer}
              style={{ marginTop: 'var(--space-xs)' }}
            >
              {savingCustomer ? 'Saving Profile...' : 'Save Customer Data'}
            </button>
          </div>
        ) : (
          <div style={styles.noActiveChat}>
            <p style={{ color: 'hsl(var(--text-muted))' }}>No customer selected</p>
          </div>
        )}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  inboxContainer: {
    display: 'flex',
    height: 'calc(100vh - 110px)',
    gap: 'var(--space-md)',
  },
  convPanel: {
    width: '320px',
    height: '100%',
    display: 'flex',
    flexDirection: 'column',
    borderRadius: 'var(--radius-md)',
    border: '1px solid rgba(255, 255, 255, 0.05)',
  },
  panelHeader: {
    padding: 'var(--space-md)',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
  },
  panelTitle: {
    fontSize: '1.1rem',
    fontWeight: 700,
  },
  searchWrapper: {
    position: 'relative',
    display: 'flex',
    alignItems: 'center',
  },
  searchIcon: {
    position: 'absolute',
    left: '10px',
    color: 'hsl(var(--text-muted))',
  },
  searchInput: {
    width: '100%',
    paddingLeft: '32px',
    fontSize: '0.85rem',
  },
  filterBar: {
    display: 'flex',
    gap: '4px',
    overflowX: 'auto',
  },
  filterBtn: {
    backgroundColor: 'transparent',
    color: 'hsl(var(--text-muted))',
    border: 'none',
    padding: '4px 8px',
    fontSize: '0.75rem',
    borderRadius: 'var(--radius-sm)',
    cursor: 'pointer',
    transition: 'var(--transition-fast)',
    fontWeight: 500,
  },
  filterBtnActive: {
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    color: 'hsl(var(--accent-primary))',
    fontWeight: 600,
  },
  convList: {
    flexGrow: 1,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    padding: 'var(--space-xs)',
  },
  emptyState: {
    padding: 'var(--space-lg)',
    textAlign: 'center',
    color: 'hsl(var(--text-muted))',
    fontSize: '0.875rem',
  },
  convCard: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-sm)',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-md)',
    cursor: 'pointer',
    transition: 'var(--transition-normal)',
    border: '1px solid transparent',
  },
  convCardActive: {
    backgroundColor: 'rgba(255, 255, 255, 0.03)',
    borderColor: 'rgba(99, 102, 241, 0.2)',
  },
  avatar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '40px',
    height: '40px',
    borderRadius: 'var(--radius-full)',
    backgroundColor: 'rgba(99, 102, 241, 0.12)',
    color: 'hsl(var(--accent-primary))',
    fontWeight: 700,
    fontSize: '0.95rem',
    flexShrink: 0,
  },
  convMeta: {
    flexGrow: 1,
    overflow: 'hidden',
  },
  convNameRow: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'baseline',
    marginBottom: '2px',
  },
  convName: {
    fontSize: '0.9rem',
    fontWeight: 600,
    color: 'hsl(var(--text-primary))',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  convTime: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
  },
  convPreviewRow: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  convPhone: {
    fontSize: '0.8rem',
    color: 'hsl(var(--text-muted))',
  },
  unreadBadge: {
    backgroundColor: 'hsl(var(--accent-secondary))',
    color: 'white',
    fontSize: '0.7rem',
    fontWeight: 700,
    borderRadius: 'var(--radius-full)',
    padding: '1px 6px',
  },
  chatPanel: {
    flexGrow: 1,
    height: '100%',
    display: 'flex',
    flexDirection: 'column',
    borderRadius: 'var(--radius-md)',
    border: '1px solid rgba(255, 255, 255, 0.05)',
  },
  chatHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: 'var(--space-md)',
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
  },
  chatHeaderName: {
    fontSize: '1rem',
    fontWeight: 700,
  },
  chatHeaderPhone: {
    fontSize: '0.8rem',
    color: 'hsl(var(--text-muted))',
  },
  chatHeaderStatus: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-xs)',
  },
  statusLabel: {
    fontSize: '0.8rem',
    color: 'hsl(var(--text-secondary))',
    fontWeight: 500,
  },
  statusSelect: {
    backgroundColor: 'rgba(255, 255, 255, 0.03)',
    border: '1px solid rgba(255, 255, 255, 0.1)',
    color: 'hsl(var(--text-primary))',
    borderRadius: 'var(--radius-sm)',
    padding: '2px 8px',
    fontSize: '0.8rem',
    outline: 'none',
  },
  messageThread: {
    flexGrow: 1,
    padding: 'var(--space-md)',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-md)',
    backgroundColor: 'rgba(15, 23, 42, 0.15)',
  },
  msgRowCustomer: {
    display: 'flex',
    justifyContent: 'flex-start',
    width: '100%',
  },
  msgRowAgent: {
    display: 'flex',
    justifyContent: 'flex-end',
    width: '100%',
  },
  msgRowSystem: {
    display: 'flex',
    justifyContent: 'center',
    width: '100%',
  },
  msgBubbleCustomer: {
    maxWidth: '70%',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: '0 var(--radius-md) var(--radius-md) var(--radius-md)',
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
    border: '1px solid rgba(255, 255, 255, 0.03)',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  msgBubbleAgent: {
    maxWidth: '70%',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-md) 0 var(--radius-md) var(--radius-md)',
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    border: '1px solid rgba(99, 102, 241, 0.2)',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  msgBubbleAI: {
    maxWidth: '70%',
    padding: 'var(--space-sm) var(--space-md)',
    borderRadius: 'var(--radius-md) 0 var(--radius-md) var(--radius-md)',
    backgroundColor: 'rgba(236, 72, 153, 0.1)',
    border: '1px solid rgba(236, 72, 153, 0.25)',
    boxShadow: '0 0 10px rgba(236, 72, 153, 0.05)',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  msgBubbleSystem: {
    maxWidth: '80%',
    padding: '4px 12px',
    borderRadius: 'var(--radius-full)',
    backgroundColor: 'rgba(255, 255, 255, 0.02)',
    border: '1px solid rgba(255, 255, 255, 0.05)',
    color: 'hsl(var(--text-muted))',
    fontSize: '0.75rem',
    textAlign: 'center',
  },
  aiBadgeRow: {
    display: 'flex',
    alignItems: 'center',
    gap: '4px',
    fontSize: '0.65rem',
    fontWeight: 700,
    textTransform: 'uppercase',
    color: 'hsl(var(--accent-secondary))',
    marginBottom: '2px',
  },
  msgTime: {
    fontSize: '0.65rem',
    color: 'hsl(var(--text-muted))',
    alignSelf: 'flex-end',
  },
  aiSuggestionBox: {
    margin: 'var(--space-sm) var(--space-md)',
    padding: 'var(--space-md)',
    borderRadius: 'var(--radius-md)',
    border: '1px solid rgba(236, 72, 153, 0.2)',
    boxShadow: 'var(--shadow-neon-pink)',
    display: 'flex',
    flexDirection: 'column',
    gap: 'var(--space-sm)',
    backgroundColor: 'rgba(30, 41, 59, 0.8)',
  },
  aiSuggestionHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  aiConfidence: {
    fontSize: '0.75rem',
    fontWeight: 700,
    color: 'hsl(var(--accent-success))',
    backgroundColor: 'rgba(16, 185, 129, 0.1)',
    padding: '2px 6px',
    borderRadius: 'var(--radius-sm)',
  },
  aiSuggestionText: {
    fontSize: '0.9rem',
    color: 'hsl(var(--text-primary))',
    fontStyle: 'italic',
    lineHeight: 1.4,
  },
  aiSuggestionFooter: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  aiReasoning: {
    fontSize: '0.75rem',
    color: 'hsl(var(--text-muted))',
    maxWidth: '70%',
  },
  composerForm: {
    display: 'flex',
    alignItems: 'center',
    gap: 'var(--space-sm)',
    padding: 'var(--space-md)',
    borderTop: '1px solid rgba(255, 255, 255, 0.05)',
  },
  attachmentBtn: {
    background: 'none',
    border: 'none',
    color: 'hsl(var(--text-muted))',
    cursor: 'pointer',
    padding: '8px',
    borderRadius: 'var(--radius-sm)',
    transition: 'var(--transition-fast)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  },
  composerInput: {
    flexGrow: 1,
    fontSize: '0.95rem',
  },
  sendBtn: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '40px',
    height: '40px',
    padding: 0,
    borderRadius: 'var(--radius-md)',
  },
  noActiveChat: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    textAlign: 'center',
    padding: 'var(--space-xxl)',
  },
  detailsPanel: {
    width: '320px',
    height: '100%',
    borderRadius: 'var(--radius-md)',
    border: '1px solid rgba(255, 255, 255, 0.05)',
    padding: 'var(--space-md)',
    overflowY: 'auto',
  },
  detailsTitle: {
    fontSize: '1.1rem',
    fontWeight: 700,
    borderBottom: '1px solid rgba(255, 255, 255, 0.05)',
    paddingBottom: 'var(--space-sm)',
    marginBottom: 'var(--space-xs)',
  },
  detailField: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  detailLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    fontSize: '0.75rem',
    color: 'hsl(var(--text-secondary))',
    fontWeight: 600,
  },
  detailInput: {
    width: '100%',
    padding: '6px 12px',
    fontSize: '0.85rem',
  },
  detailSelect: {
    width: '100%',
    padding: '6px 12px',
    fontSize: '0.85rem',
  },
  detailTextarea: {
    width: '100%',
    minHeight: '80px',
    padding: '8px 12px',
    fontSize: '0.85rem',
    resize: 'vertical',
    fontFamily: 'inherit',
  },
  tagsContainer: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '4px',
    minHeight: '30px',
    padding: '4px',
    borderRadius: 'var(--radius-sm)',
    backgroundColor: 'rgba(0,0,0,0.1)',
  },
  tagBadge: {
    display: 'flex',
    alignItems: 'center',
    gap: '4px',
    backgroundColor: 'rgba(99, 102, 241, 0.15)',
    color: 'hsl(var(--accent-primary))',
    fontSize: '0.75rem',
    fontWeight: 600,
    padding: '2px 8px',
    borderRadius: 'var(--radius-full)',
  },
  tagRemoveBtn: {
    background: 'none',
    border: 'none',
    color: 'hsl(var(--accent-primary))',
    cursor: 'pointer',
    fontSize: '0.875rem',
    lineHeight: 1,
    padding: '0 2px',
  },
};
