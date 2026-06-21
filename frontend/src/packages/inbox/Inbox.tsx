'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useAuth } from '../../context/auth-context';
import { useToast } from '../../context/toast-context';
import Tooltip from '../../components/shared/Tooltip';
import PhantomLoader from '../../components/shared/PhantomLoader';
import { api } from '../../services/api';
import { SignalRService } from '../../services/signalr';
import { Conversation, Message, AISuggestion, ConversationStatus } from '../../types/chat';
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
  Inbox as InboxIcon,
  ShieldBan
} from 'lucide-react';
import styles from './inbox.module.css';

const statusLabels: Record<string, string> = {
  All: 'الكل',
  Open: 'مفتوحة',
  Pending: 'قيد المتابعة',
  Resolved: 'تم حلها',
  Closed: 'مغلقة',
};

const stageLabels: Record<string, string> = {
  New: 'جديد',
  Contacted: 'تم التواصل',
  Qualified: 'مؤهل',
  Proposal: 'عرض سعر',
  Negotiation: 'تفاوض',
  Won: 'تم البيع',
  Lost: 'خسارة',
};

const formatEgyptDateTime = (dateStr: string) => {
  try {
    const date = new Date(dateStr);
    return date.toLocaleString('ar-EG-u-nu-latn', {
      timeZone: 'Africa/Cairo',
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    });
  } catch {
    return dateStr;
  }
};

const formatEgyptTime = (dateStr: string) => {
  try {
    const date = new Date(dateStr);
    return date.toLocaleTimeString('ar-EG-u-nu-latn', {
      timeZone: 'Africa/Cairo',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    });
  } catch {
    return dateStr;
  }
};

export default function Inbox() {
  const { activeProject } = useAuth();
  const { showToast } = useToast();
  
  // State
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeConv, setActiveConv] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [mediaUrls, setMediaUrls] = useState<Record<string, string>>({});
  const [aiSuggestion, setAiSuggestion] = useState<AISuggestion | null>(null);
  const [aiTypingConversations, setAiTypingConversations] = useState<Record<string, boolean>>({});
  const [aiTypingCountdown, setAiTypingCountdown] = useState<number>(10);
  const [aiTypingStages, setAiTypingStages] = useState<Record<string, 'generating' | 'typing'>>({});
  const [hasMoreMessages, setHasMoreMessages] = useState<boolean>(true);
  const [loadingMore, setLoadingMore] = useState<boolean>(false);
  
  const isAiTyping = activeConv ? !!aiTypingConversations[activeConv.id] : false;
  const aiTypingStage = activeConv ? aiTypingStages[activeConv.id] || 'generating' : 'generating';

  useEffect(() => {
    if (isAiTyping && aiTypingStage === 'typing') {
      const interval = setInterval(() => {
        setAiTypingCountdown((prev) => (prev > 1 ? prev - 1 : 1));
      }, 1000);
      return () => clearInterval(interval);
    }
  }, [isAiTyping, aiTypingStage, activeConv?.id]);
  
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
  const [saveSuccess, setSaveSuccess] = useState(false);
  const [isBlacklisted, setIsBlacklisted] = useState(false);

  // AI Memory / Profile states
  const [editableSummary, setEditableSummary] = useState('');
  const [editableFacts, setEditableFacts] = useState('');
  const [editableTriggers, setEditableTriggers] = useState('');
  const [editableObjections, setEditableObjections] = useState('');
  const [loadingMemory, setLoadingMemory] = useState(false);
  const [generatingMemory, setGeneratingMemory] = useState(false);

  // Message composer
  const [inputMessage, setInputMessage] = useState('');
  const [sending, setSending] = useState(false);
  const [filterStatus, setFilterStatus] = useState<string>('All');
  const [searchQuery, setSearchQuery] = useState('');
  const [debouncedSearchQuery, setDebouncedSearchQuery] = useState('');

  const [hasMoreConvs, setHasMoreConvs] = useState(true);
  const [loadingMoreConvs, setLoadingMoreConvs] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearchQuery(searchQuery);
    }, 300);
    return () => clearTimeout(timer);
  }, [searchQuery]);

  // Refs
  const messageEndRef = useRef<HTMLDivElement>(null);
  const signalRServiceRef = useRef<SignalRService | null>(null);
  const activeConvRef = useRef<Conversation | null>(null);
  const loadingMoreConvsRef = useRef(false);
  const currentParamsRef = useRef({ status: 'All', search: '' });
  const searchInputRef = useRef<HTMLInputElement>(null);
  const composerInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    activeConvRef.current = activeConv;
  }, [activeConv]);

  useEffect(() => {
    currentParamsRef.current = { status: filterStatus, search: debouncedSearchQuery };
  }, [filterStatus, debouncedSearchQuery]);

  // Keyboard Shortcuts Listener
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // 1. "/" focuses search input
      if (e.key === '/' && document.activeElement?.tagName !== 'INPUT' && document.activeElement?.tagName !== 'TEXTAREA') {
        e.preventDefault();
        searchInputRef.current?.focus();
      }

      // 2. Escape to unselect active conversation or blur input fields
      if (e.key === 'Escape') {
        if (document.activeElement instanceof HTMLElement) {
          document.activeElement.blur();
        }
        setActiveConv(null);
      }

      // 3. "R" focuses composer input when a conversation is active
      if ((e.key === 'r' || e.key === 'R' || e.key === 'ق') && activeConvRef.current && document.activeElement?.tagName !== 'INPUT' && document.activeElement?.tagName !== 'TEXTAREA') {
        e.preventDefault();
        composerInputRef.current?.focus();
      }

      // 4. "B" toggles blacklist when a conversation is active
      if ((e.key === 'b' || e.key === 'B' || e.key === 'لا') && activeConvRef.current && document.activeElement?.tagName !== 'INPUT' && document.activeElement?.tagName !== 'TEXTAREA') {
        e.preventDefault();
        handleToggleBlacklist();
      }

      // 5. Cmd+Enter / Ctrl+Enter to send message
      if (e.key === 'Enter' && (e.metaKey || e.ctrlKey) && document.activeElement === composerInputRef.current) {
        e.preventDefault();
        void handleSendMessage();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isBlacklisted, inputMessage, activeConv]);

  // Load Conversations
  const fetchConversations = async () => {
    if (!activeProject) return;
    const activeStatus = filterStatus;
    const activeSearch = debouncedSearchQuery;
    try {
      const response = await api.get<Conversation[]>(`/api/projects/${activeProject.id}/conversations`, {
        params: {
          status: activeStatus,
          search: activeSearch || undefined,
          limit: 20
        }
      });
      if (activeStatus !== currentParamsRef.current.status || activeSearch !== currentParamsRef.current.search) {
        return;
      }
      setConversations(response.data);
      setHasMoreConvs(response.data.length === 20);

      // Initialize typing status from server response
      const typingMap: Record<string, boolean> = {};
      const stageMap: Record<string, 'generating' | 'typing'> = {};
      response.data.forEach(c => {
        if (c.isAiTyping) {
          typingMap[c.id] = true;
          if (c.aiTypingStage) {
            stageMap[c.id] = c.aiTypingStage;
          }
        }
      });
      setAiTypingConversations(prev => ({ ...prev, ...typingMap }));
      setAiTypingStages(prev => ({ ...prev, ...stageMap }));

      // If active conversation is typing, set countdown
      const currentActive = response.data.find(c => activeConvRef.current && c.id === activeConvRef.current.id);
      if (currentActive && currentActive.isAiTyping && currentActive.aiTypingCountdown) {
        setAiTypingCountdown(currentActive.aiTypingCountdown);
      }
    } catch (e) {
      console.error('Error loading conversations', e);
    }
  };

  const loadMoreConversations = async () => {
    if (!activeProject || !hasMoreConvs || loadingMoreConvsRef.current || conversations.length === 0) return;
    loadingMoreConvsRef.current = true;
    setLoadingMoreConvs(true);
    const activeStatus = filterStatus;
    const activeSearch = debouncedSearchQuery;
    try {
      const oldestTimestamp = conversations[conversations.length - 1].lastMessageAt;
      const response = await api.get<Conversation[]>(`/api/projects/${activeProject.id}/conversations`, {
        params: {
          status: activeStatus,
          search: activeSearch || undefined,
          before: oldestTimestamp,
          limit: 20
        }
      });
      if (activeStatus !== currentParamsRef.current.status || activeSearch !== currentParamsRef.current.search) {
        return;
      }
      if (response.data.length < 20) {
        setHasMoreConvs(false);
      }
      if (response.data.length > 0) {
        setConversations(prev => {
          const existingIds = new Set(prev.map(c => c.id));
          const uniqueNew = response.data.filter(c => !existingIds.has(c.id));
          return [...prev, ...uniqueNew];
        });
      }
    } catch (err) {
      console.error('Error loading more conversations', err);
    } finally {
      loadingMoreConvsRef.current = false;
      setLoadingMoreConvs(false);
    }
  };

  useEffect(() => {
    fetchConversations();
  }, [activeProject, filterStatus, debouncedSearchQuery]);

  // Load Messages & Customer Details for Active Conversation
  useEffect(() => {
    const fetchActiveDetails = async () => {
      if (!activeConv) {
        setMessages([]);
        setAiSuggestion(null);
        setHasMoreMessages(true);
        return;
      }

      try {
        // Load messages
        const msgResp = await api.get<Message[]>(`/api/conversations/${activeConv.id}/messages`, {
          params: { limit: 10 }
        });
        setMessages(msgResp.data);
        setHasMoreMessages(msgResp.data.length === 10);

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
        setIsBlacklisted(c.isBlacklisted || false);

        // Load AI memory
        try {
          setLoadingMemory(true);
          const memResp = await api.get(`/api/customers/${activeConv.customer.id}/memory`);
          if (memResp.data) {
            setEditableSummary(memResp.data.longTermSummary || '');
            try {
              const facts = JSON.parse(memResp.data.factsJson || '[]');
              setEditableFacts(facts.join(', '));
            } catch { setEditableFacts(''); }
            try {
              const triggers = JSON.parse(memResp.data.triggersJson || '[]');
              setEditableTriggers(triggers.join(', '));
            } catch { setEditableTriggers(''); }
            try {
              const objections = JSON.parse(memResp.data.objectionsJson || '[]');
              setEditableObjections(objections.join(', '));
            } catch { setEditableObjections(''); }
          } else {
            setEditableSummary('');
            setEditableFacts('');
            setEditableTriggers('');
            setEditableObjections('');
          }
        } catch (memErr) {
          console.warn('Customer memory not found or failed to load', memErr);
          setEditableSummary('');
          setEditableFacts('');
          setEditableTriggers('');
          setEditableObjections('');
        } finally {
          setLoadingMemory(false);
        }
        
        // Reset AI suggestion
        setAiSuggestion(null);
      } catch (e) {
        console.error('Error loading conversation details', e);
      }
    };

    fetchActiveDetails();
  }, [activeConv]);

  const loadMoreMessages = async () => {
    if (!activeConv || !hasMoreMessages || loadingMore || messages.length === 0) return;
    setLoadingMore(true);
    try {
      const oldestTimestamp = messages[0].createdAt;
      const response = await api.get<Message[]>(`/api/conversations/${activeConv.id}/messages`, {
        params: {
          before: oldestTimestamp,
          limit: 10
        }
      });
      if (response.data.length < 10) {
        setHasMoreMessages(false);
      }
      if (response.data.length > 0) {
        setMessages((prev) => [...response.data, ...prev]);
      }
    } catch (err) {
      console.error('Error loading more messages', err);
    } finally {
      setLoadingMore(false);
    }
  };

  const handleScroll = async (e: React.UIEvent<HTMLDivElement>) => {
    const target = e.currentTarget;
    if (target.scrollTop === 0 && hasMoreMessages && !loadingMore && messages.length > 0) {
      const prevScrollHeight = target.scrollHeight;
      await loadMoreMessages();
      requestAnimationFrame(() => {
        target.scrollTop = target.scrollHeight - prevScrollHeight;
      });
    }
  };

  const handleConvListScroll = (e: React.UIEvent<HTMLDivElement>) => {
    const target = e.currentTarget;
    const isNearBottom = target.scrollHeight - target.scrollTop - target.clientHeight < 50;
    if (isNearBottom && hasMoreConvs && !loadingMoreConvsRef.current) {
      loadMoreConversations();
    }
  };

  const lastMessageIdRef = useRef<string | null>(null);

  // Scroll to bottom of message list on new incoming/outgoing message or conversation change
  useEffect(() => {
    if (messages.length === 0) {
      lastMessageIdRef.current = null;
      return;
    }
    const newestMsg = messages[messages.length - 1];
    if (newestMsg.id !== lastMessageIdRef.current) {
      lastMessageIdRef.current = newestMsg.id;
      messageEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [messages]);

  // Load pre-signed URLs for media assets in messages
  useEffect(() => {
    const fetchMissingUrls = async () => {
      if (!activeProject || messages.length === 0) return;
      const missingIds = messages
        .filter(m => m.assetId && !mediaUrls[m.assetId])
        .map(m => m.assetId as string);

      if (missingIds.length === 0) return;

      const uniqueMissingIds = Array.from(new Set(missingIds));

      for (const id of uniqueMissingIds) {
        try {
          const resp = await api.get<{ assetId: string; url: string }>(`/api/projects/${activeProject.id}/assets/${id}/url`);
          if (resp.data && resp.data.url) {
            setMediaUrls(prev => ({ ...prev, [id]: resp.data.url }));
          }
        } catch (err) {
          console.error(`Failed to fetch pre-signed URL for asset ${id}`, err);
        }
      }
    };

    fetchMissingUrls();
  }, [messages, activeProject, mediaUrls]);

  // Initialize SignalR Connection
  useEffect(() => {
    if (!activeProject) return;
    const token = localStorage.getItem('accessToken');
    if (!token) return;

    const service = new SignalRService(activeProject.id, token);
    signalRServiceRef.current = service;
    let disposed = false;

    const initSignalR = async () => {
      service.registerOnMessage((message: Message) => {
        // Check if message belongs to current active conversation using ref
        const currentActiveConv = activeConvRef.current;
        if (currentActiveConv && message.conversationId === currentActiveConv.id) {
          setMessages((prev: Message[]) => {
            if (prev.find(m => m.id === message.id)) return prev;
            return [...prev, message];
          });
          if (message.senderType === 'AI' || message.senderType === 'Agent') {
            setAiTypingConversations((prev) => ({
              ...prev,
              [message.conversationId]: false
            }));
          }
        }
        
        // Update conversation list preview
        setConversations((prev: Conversation[]) => {
          const exists = prev.some((c) => c.id === message.conversationId);
          if (!exists) {
            fetchConversations();
            return prev;
          }
          return prev.map((c) => {
            if (c.id === message.conversationId) {
              return {
                ...c,
                lastMessageAt: message.createdAt,
                unreadCount: currentActiveConv?.id === c.id ? 0 : c.unreadCount + 1
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
        const currentActiveConv = activeConvRef.current;
        if (currentActiveConv && currentActiveConv.id === convId) {
          setActiveConv((prev: Conversation | null) => prev ? { ...prev, status } : null);
        }
      });

      service.registerOnAISuggestion((suggestion: AISuggestion) => {
        const currentActiveConv = activeConvRef.current;
        if (currentActiveConv && suggestion.conversationId === currentActiveConv.id) {
          setAiSuggestion(suggestion);
        }
      });

      service.registerOnAITyping((convId: string, isTyping: boolean, estimatedSeconds?: number, stage?: 'generating' | 'typing') => {
        setAiTypingConversations((prev) => ({
          ...prev,
          [convId]: isTyping
        }));
        if (stage) {
          setAiTypingStages((prev) => ({
            ...prev,
            [convId]: stage
          }));
        }
        if (isTyping) {
          setAiTypingCountdown(estimatedSeconds ?? 10);
        }
      });

      service.registerOnAITypingError((convId: string, message: string) => {
        showToast(message, 'error');
      });

      service.registerOnCustomerUpdate((customer: any) => {
        setConversations((prev: Conversation[]) => {
          return prev.map((c) => {
            if (c.customer.id === customer.id) {
              return {
                ...c,
                customer: {
                  ...c.customer,
                  name: customer.name || c.customer.name,
                  phone: customer.phone || c.customer.phone,
                  label: customer.label || c.customer.label
                }
              };
            }
            return c;
          });
        });

        const currentActiveConv = activeConvRef.current;
        if (currentActiveConv && currentActiveConv.customer.id === customer.id) {
          setActiveConv((prev) => {
            if (!prev) return null;
            return {
              ...prev,
              customer: {
                ...prev.customer,
                name: customer.name || prev.customer.name,
                phone: customer.phone || prev.customer.phone,
                label: customer.label || prev.customer.label
              }
            };
          });
          
          setCustomerName(customer.name || '');
          setCustomerCity(customer.city || '');
          setCustomerBudget(customer.budget ? customer.budget.toString() : '');
          setCustomerLeadScore(customer.leadScore || 0);
          setCustomerStage(customer.pipelineStage || 'New');
          setCustomerNotes(customer.notes || '');
          setCustomerTags(customer.tags || []);
        }
      });

      try {
        await service.start();
        if (disposed) {
          await service.updatePresence('Offline');
          await service.stop();
          return;
        }
        await service.updatePresence('Online');
      } catch (e) {
        console.error('Failed to connect to SignalR realtime gateway', e);
      }
    };

    initSignalR();

    return () => {
      disposed = true;
      void (async () => {
        await service.updatePresence('Offline');
        await service.stop();
        if (signalRServiceRef.current === service) {
          signalRServiceRef.current = null;
        }
      })();
    };
  }, [activeProject]);

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

      // Save memory changes as well
      const parseCsv = (csv: string) => csv.split(',').map(s => s.trim()).filter(Boolean);
      const memoryPayload = {
        longTermSummary: editableSummary,
        factsJson: JSON.stringify(parseCsv(editableFacts)),
        triggersJson: JSON.stringify(parseCsv(editableTriggers)),
        objectionsJson: JSON.stringify(parseCsv(editableObjections)),
      };
      await api.put(`/api/customers/${activeConv.customer.id}/memory`, memoryPayload);

      // Refresh list to update contact names if name changed
      fetchConversations();
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 2000);
      showToast('تم حفظ بيانات العميل وملف تعريفه بنجاح! ✨', 'success');
    } catch (e) {
      console.error('Failed to update CRM customer profile', e);
      showToast('فشل حفظ بيانات العميل. يرجى المحاولة مرة أخرى.', 'error');
    } finally {
      setSavingCustomer(false);
    }
  };

  const handleToggleBlacklist = async () => {
    if (!activeConv) return;
    const newVal = !isBlacklisted;
    try {
      await api.put(`/api/customers/${activeConv.customer.id}`, {
        isBlacklisted: newVal,
      });
      setIsBlacklisted(newVal);
      showToast(
        newVal ? 'تم حظر العميل من الرد التلقائي ⛔' : 'تم فك حظر العميل وتفعيل الرد التلقائي ✅',
        newVal ? 'error' : 'success'
      );
    } catch (e) {
      console.error('Failed to toggle blacklist', e);
      showToast('فشل تغيير حالة الحظر', 'error');
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

  const handleGenerateMemory = async () => {
    if (!activeConv || !activeProject || generatingMemory) return;
    setGeneratingMemory(true);
    try {
      const resp = await api.post(`/api/projects/${activeProject.id}/customers/${activeConv.customer.id}/memory/generate`);
      if (resp.data) {
        // Reload customer detail states
        const custResp = await api.get(`/api/customers/${activeConv.customer.id}`);
        const c = custResp.data;
        setCustomerName(c.name || '');
        setCustomerCity(c.city || '');
        setCustomerBudget(c.budget ? c.budget.toString() : '');
        setCustomerLeadScore(c.leadScore || 0);
        setCustomerStage(c.pipelineStage || 'New');
        setCustomerNotes(c.notes || '');
        setCustomerTags(c.tags || []);

        // Reload memory states
        const memResp = await api.get(`/api/customers/${activeConv.customer.id}/memory`);
        if (memResp.data) {
          setEditableSummary(memResp.data.longTermSummary || '');
          try {
            const facts = JSON.parse(memResp.data.factsJson || '[]');
            setEditableFacts(facts.join(', '));
          } catch { setEditableFacts(''); }
          try {
            const triggers = JSON.parse(memResp.data.triggersJson || '[]');
            setEditableTriggers(triggers.join(', '));
          } catch { setEditableTriggers(''); }
          try {
            const objections = JSON.parse(memResp.data.objectionsJson || '[]');
            setEditableObjections(objections.join(', '));
          } catch { setEditableObjections(''); }
        }
        showToast('تم تحديث وتوليد ملف التعريف بالذكاء الاصطناعي بنجاح! 🧠', 'success');
      }
    } catch (err: any) {
      console.error('Failed to generate customer profile', err);
      const errMsg = err.response?.data || 'فشل توليد ملف التعريف. تأكد من وجود رسائل سابقة للعميل.';
      showToast(errMsg, 'error');
    } finally {
      setGeneratingMemory(false);
    }
  };

  // Filters & Searches
  const filteredConversations = conversations
    .filter((c) => {
      if (!c || !c.customer) return false;
      const statusMatch = filterStatus === 'All' || c.status === filterStatus;
      const nameMatch = (c.customer.name || '').toLowerCase().includes(searchQuery.toLowerCase()) || 
                        (c.customer.phone || '').includes(searchQuery);
      return statusMatch && nameMatch;
    })
    .sort((a, b) => new Date(b.lastMessageAt).getTime() - new Date(a.lastMessageAt).getTime());

  return (
    <div className={styles.inboxContainer}>
      {/* Panel 1: Conversation List */}
      <div className={`glass-panel ${styles.convPanel}`}>
        <div className={styles.panelHeader}>
          <h3 className={styles.panelTitle}>المحادثات</h3>
          <div className={styles.searchWrapper}>
            <Search size={16} className={styles.searchIcon} />
            <input
              ref={searchInputRef}
              type="text"
              placeholder="ابحث في المحادثات..."
              className={`neon-input ${styles.searchInput}`}
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            <kbd style={{ position: 'absolute', left: '10px', top: '50%', transform: 'translateY(-50%)', pointerEvents: 'none' }}>/</kbd>
          </div>
          <div className={styles.filterBar}>
            {['All', 'Open', 'Pending', 'Resolved'].map((st) => (
              <button
                key={st}
                onClick={() => setFilterStatus(st)}
                className={`${styles.filterBtn} ${filterStatus === st ? styles.filterBtnActive : ''}`}
              >
                {statusLabels[st] || st}
              </button>
            ))}
          </div>
        </div>

        <div className={styles.convList} onScroll={handleConvListScroll}>
          {filteredConversations.length === 0 ? (
            <div className={styles.emptyState}>لا توجد محادثات</div>
          ) : (
            <>
              {filteredConversations.map((c) => (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => setActiveConv(c)}
                  className={`${styles.convCard} ${activeConv?.id === c.id ? styles.convCardActive : ''}`}
                  style={{ background: 'none', border: 'none', width: '100%', textAlign: 'right', display: 'flex', font: 'inherit', color: 'inherit' }}
                >
                  <div className={styles.avatar}>
                    {c.customer.name.charAt(0).toUpperCase()}
                  </div>
                  <div className={styles.convMeta}>
                    <div className={styles.convNameRow}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '6px', minWidth: 0, flex: 1 }}>
                        <span className={styles.convName}>{c.customer.name}</span>
                        {c.customer.label && (
                          <span className={styles.convLabelBadge}>{c.customer.label}</span>
                        )}
                      </div>
                      <span className={styles.convTime}>
                        {formatEgyptTime(c.lastMessageAt)}
                      </span>
                    </div>
                    <div className={styles.convPreviewRow}>
                      <span className={styles.convPhone}>{c.customer.phone}</span>
                      {c.unreadCount > 0 && (
                        <span className={styles.unreadBadge}>{c.unreadCount}</span>
                      )}
                    </div>
                  </div>
                </button>
              ))}
              {loadingMoreConvs && (
                <div style={{ padding: '12px', display: 'flex', justifyContent: 'center' }}>
                  <PhantomLoader loading={loadingMoreConvs} label="تحميل المزيد من المحادثات...">
                    <div style={{ fontSize: '0.8rem', color: 'var(--text-soft)' }}>جاري تحميل محادثات إضافية...</div>
                  </PhantomLoader>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* Panel 2: Chat Log */}
      <div className={`glass-panel ${styles.chatPanel}`}>
        {activeConv ? (
          <>
            <div className={styles.chatHeader}>
              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <h4 className={styles.chatHeaderName}>{activeConv.customer.name}</h4>
                  {activeConv.customer.label && (
                    <span className={styles.convLabelBadge}>{activeConv.customer.label}</span>
                  )}
                </div>
                <p className={styles.chatHeaderPhone}>{activeConv.customer.phone}</p>
              </div>
              <div className={styles.chatHeaderStatus}>
                <span className={styles.statusLabel}>الحالة:</span>
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
                  className={styles.statusSelect}
                >
                  <option value="Open">مفتوحة</option>
                  <option value="Pending">قيد المتابعة</option>
                  <option value="Resolved">تم حلها</option>
                  <option value="Closed">مغلقة</option>
                </select>
              </div>
            </div>

            {/* Message Thread */}
            <div className={styles.messageThread} onScroll={handleScroll}>
              {loadingMore && (
                <div style={{ textAlign: 'center', padding: '10px', color: 'hsl(var(--text-secondary))', fontSize: '0.875rem' }}>
                  جاري تحميل الرسائل السابقة...
                </div>
              )}
              {messages.length === 0 ? (
                <div className={styles.emptyState}>لا توجد رسائل في هذه المحادثة</div>
              ) : (
                [...messages]
                  .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime())
                  .map((m) => {
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
                    <div key={m.id} className={alignStyle}>
                      <div className={bubbleStyle}>
                        {isAI && (
                          <div className={styles.aiBadgeRow}>
                            <Sparkles size={12} style={{ color: 'hsl(var(--accent-secondary))' }} />
                            <span>مساعد الذكاء الاصطناعي</span>
                          </div>
                        )}
                        {/* Media rendering */}
                        {m.mediaType === 'Image' && m.assetId && (
                          <div className={styles.mediaContainer}>
                            {mediaUrls[m.assetId] ? (
                              <img 
                                src={mediaUrls[m.assetId]} 
                                alt="Shared Image" 
                                className={styles.mediaImage}
                                onClick={() => window.open(mediaUrls[m.assetId as string], '_blank')}
                              />
                            ) : (
                              <PhantomLoader loading label="تحميل الصورة">
                                <div className={styles.mediaLoading}>معاينة الصورة المشتركة</div>
                              </PhantomLoader>
                            )}
                          </div>
                        )}

                        {m.mediaType === 'Voice' && m.assetId && (
                          <div className={styles.mediaContainer}>
                            {mediaUrls[m.assetId] ? (
                              <div className={styles.voiceNotePlayer}>
                                <audio controls src={mediaUrls[m.assetId]} className={styles.mediaVoice} />
                                {m.transcription && (
                                  <div className={styles.transcriptionText}>
                                    <strong>النسخ النصي الذكي:</strong>
                                    <p>{m.transcription}</p>
                                  </div>
                                )}
                              </div>
                            ) : (
                              <PhantomLoader loading label="تحميل المقطع الصوتي">
                                <div className={styles.mediaLoading}>مشغل المقطع الصوتي</div>
                              </PhantomLoader>
                            )}
                          </div>
                        )}

                        {!(m.mediaType === 'Voice' && m.assetId) && !(m.mediaType === 'Image' && m.content === '[Image]') && (
                          <p style={{ whiteSpace: 'pre-wrap' }}>{m.content}</p>
                        )}
                        <span className={styles.msgTime}>
                          {formatEgyptDateTime(m.createdAt)}
                        </span>
                      </div>
                    </div>
                  );
                })
              )}
              {isAiTyping && (
                <div className={styles.msgRowAgent}>
                  <div className={styles.msgBubbleAI} style={{ opacity: 0.85 }}>
                    <div className={styles.aiBadgeRow}>
                      <Sparkles size={12} className={styles.typingSparkle} />
                      <span>الذكاء الاصطناعي</span>
                    </div>
                    <div className={styles.typingDots}>
                      {aiTypingStage === 'generating' ? (
                        <span>جاري التفكير وتوليد الرد...</span>
                      ) : (
                        <span>جاري الرد تلقائياً خلال {aiTypingCountdown} ثوانٍ</span>
                      )}
                      <span className={styles.dot}>.</span>
                      <span className={styles.dot}>.</span>
                      <span className={styles.dot}>.</span>
                    </div>
                  </div>
                </div>
              )}
              <div ref={messageEndRef} />
            </div>

            {/* AI Suggestion Box */}
            {aiSuggestion && (
              <div className={`glass-panel ${styles.aiSuggestionBox}`}>
                <div className={styles.aiSuggestionHeader}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-sm)' }}>
                    <Sparkles size={16} className="text-pink" />
                    <span style={{ fontWeight: 700, color: 'hsl(var(--text-primary))' }}>اقتراح رد من Gemini</span>
                  </div>
                  <span className={styles.aiConfidence}>
                    تطابق {(aiSuggestion.confidenceScore * 100).toFixed(0)}%
                  </span>
                </div>
                <p className={styles.aiSuggestionText}>{aiSuggestion.suggestionText}</p>
                <div className={styles.aiSuggestionFooter}>
                  <p className={styles.aiReasoning}>{aiSuggestion.reasoning}</p>
                  <button 
                    onClick={() => applyAISuggestion(aiSuggestion.suggestionText)}
                    className="neon-btn-secondary"
                    style={{ padding: '4px 8px', fontSize: '0.75rem' }}
                  >
                    استخدام الاقتراح
                  </button>
                </div>
              </div>
            )}

            {/* Composer */}
            <form onSubmit={handleSendMessage} className={styles.composerForm}>
              <button type="button" className={styles.attachmentBtn}>
                <Paperclip size={20} />
              </button>
              <div style={{ position: 'relative', display: 'flex', alignItems: 'center', flexGrow: 1 }}>
                <input
                  ref={composerInputRef}
                  type="text"
                  placeholder="اكتب رسالة..."
                  className={`neon-input ${styles.composerInput}`}
                  value={inputMessage}
                  onChange={(e) => setInputMessage(e.target.value)}
                  style={{ width: '100%', paddingLeft: '56px' }}
                />
                <kbd style={{ position: 'absolute', left: '10px', top: '50%', transform: 'translateY(-50%)', pointerEvents: 'none' }}>⌘↵</kbd>
              </div>
              <button 
                type="submit" 
                className={`neon-btn ${styles.sendBtn}`}
                disabled={!inputMessage.trim() || sending}
              >
                <Send size={18} />
              </button>
            </form>
          </>
        ) : (
          <div className={styles.noActiveChat}>
            <InboxIcon size={48} style={{ color: 'hsl(var(--text-muted))', marginBottom: 'var(--space-md)' }} />
            <h3>اختر محادثة</h3>
            <p style={{ color: 'hsl(var(--text-muted))' }}>اختر عميل من القائمة لعرض الرسائل.</p>
          </div>
        )}
      </div>

      {/* Panel 3: Customer Details Panel */}
      <div className={`glass-panel ${styles.detailsPanel}`}>
        {activeConv ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-md)' }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderBottom: '1px solid hsl(var(--bg-tertiary))', paddingBottom: 'var(--space-sm)', marginBottom: 'var(--space-xs)' }}>
              <h3 style={{ fontSize: '1.1rem', fontWeight: 700, margin: 0 }}>بيانات العميل</h3>
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                {activeConv.customer.label && (
                  <span className={styles.convLabelBadge}>{activeConv.customer.label}</span>
                )}
                <button
                  onClick={handleToggleBlacklist}
                  title={isBlacklisted ? 'فك حظر العميل (تفعيل الرد التلقائي)' : 'حظر العميل (إيقاف الرد التلقائي)'}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '4px',
                    padding: '4px 10px',
                    fontSize: '0.75rem',
                    fontWeight: 600,
                    borderRadius: '6px',
                    border: isBlacklisted ? '1px solid rgba(239, 68, 68, 0.3)' : '1px solid rgba(255,255,255,0.1)',
                    background: isBlacklisted ? 'rgba(239, 68, 68, 0.12)' : 'rgba(255,255,255,0.04)',
                    color: isBlacklisted ? '#ef4444' : 'hsl(var(--text-secondary))',
                    cursor: 'pointer',
                    transition: 'all 0.2s',
                  }}
                >
                  <ShieldBan size={13} />
                  {isBlacklisted ? 'محظور' : 'حظر'}
                </button>
              </div>
            </div>
            
            {/* Field: Name */}
            <div className={styles.detailField}>
              <label className={styles.detailLabel}><User size={14} /> الاسم</label>
              <input
                type="text"
                className={`neon-input ${styles.detailInput}`}
                value={customerName}
                onChange={(e) => setCustomerName(e.target.value)}
              />
            </div>

            {/* Field: Phone (Read Only) */}
            <div className={styles.detailField}>
              <label className={styles.detailLabel}><Phone size={14} /> رقم واتساب</label>
              <input
                type="text"
                className={`neon-input ${styles.detailInput}`}
                value={activeConv.customer.phone}
                disabled
                style={{ opacity: 0.6, cursor: 'not-allowed' }}
              />
            </div>

            {/* Field: City */}
            <div className={styles.detailField}>
              <label className={styles.detailLabel}><MapPin size={14} /> المدينة</label>
              <input
                type="text"
                className={`neon-input ${styles.detailInput}`}
                placeholder="غير محدد"
                value={customerCity}
                onChange={(e) => setCustomerCity(e.target.value)}
              />
            </div>

            {/* Field: Budget */}
            <div className={styles.detailField}>
              <label className={styles.detailLabel}><DollarSign size={14} /> الميزانية</label>
              <input
                type="number"
                className={`neon-input ${styles.detailInput}`}
                placeholder="غير محدد"
                value={customerBudget}
                onChange={(e) => setCustomerBudget(e.target.value)}
              />
            </div>

            {/* Field: Lead Score & Pipeline Stage */}
            <div style={{ display: 'flex', gap: 'var(--space-sm)' }}>
              <div className={styles.detailField} style={{ flex: 1 }}>
                <label className={styles.detailLabel}><Award size={14} /> تقييم العميل</label>
                <input
                  type="number"
                  className={`neon-input ${styles.detailInput}`}
                  value={customerLeadScore}
                  onChange={(e) => setCustomerLeadScore(parseInt(e.target.value) || 0)}
                />
              </div>

              <div className={styles.detailField} style={{ flex: 1.2 }}>
                <label className={styles.detailLabel}><CheckSquare size={14} /> مرحلة CRM</label>
                <select
                  className={`neon-input ${styles.detailSelect}`}
                  value={customerStage}
                  onChange={(e) => setCustomerStage(e.target.value)}
                >
                  {Object.entries(stageLabels).map(([value, label]) => (
                    <option key={value} value={value}>{label}</option>
                  ))}
                </select>
              </div>
            </div>

            {/* Field: Tags */}
            <div className={styles.detailField}>
              <label className={styles.detailLabel}><Tag size={14} /> الوسوم</label>
              <div className={styles.tagsContainer}>
                {customerTags.map((tag) => (
                  <span key={tag} className={styles.tagBadge}>
                    {tag}
                    <button onClick={() => removeTag(tag)} className={styles.tagRemoveBtn}>×</button>
                  </span>
                ))}
              </div>
              <div style={{ display: 'flex', gap: 'var(--space-xs)', marginTop: 'var(--space-xs)' }}>
                <input
                  type="text"
                  placeholder="أضف وسم..."
                  className={`neon-input ${styles.detailInput}`}
                  value={newTag}
                  onChange={(e) => setNewTag(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && addTag()}
                  style={{ flex: 1, padding: '4px 8px', fontSize: '0.8rem' }}
                />
                <button onClick={addTag} className="neon-btn-secondary" style={{ padding: '4px 8px', fontSize: '0.8rem' }}>إضافة</button>
              </div>
            </div>

            {/* Field: Notes */}
            <div className={styles.detailField}>
              <label className={styles.detailLabel}>ملاحظات</label>
              <textarea
                className={`neon-input ${styles.detailTextarea}`}
                placeholder="اكتب ملاحظات المحادثة هنا..."
                value={customerNotes}
                onChange={(e) => setCustomerNotes(e.target.value)}
              />
            </div>

            {/* AI Customer Memory Section */}
            <div style={{ borderTop: '1px solid hsl(var(--bg-tertiary))', paddingTop: 'var(--space-sm)', marginTop: 'var(--space-xs)', display: 'flex', flexDirection: 'column', gap: 'var(--space-sm)' }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <h4 style={{ fontSize: '0.95rem', fontWeight: 700, margin: 0, display: 'flex', alignItems: 'center', gap: '6px' }}>
                  <Sparkles size={14} style={{ color: 'hsl(var(--accent-secondary))' }} /> ملف التعريف بالذكاء الاصطناعي
                </h4>
                <Tooltip content="تحليل محادثات العميل بالذكاء الاصطناعي لتحديث الملخص والحقائق تلقائياً" position="top">
                  <button
                    type="button"
                    onClick={handleGenerateMemory}
                    disabled={generatingMemory || savingCustomer}
                    className="neon-btn-secondary"
                    style={{ padding: '4px 8px', fontSize: '0.75rem', display: 'flex', alignItems: 'center', gap: '4px' }}
                  >
                    <Sparkles size={12} />
                    <PhantomLoader loading={generatingMemory} label="تحليل ملف العميل">
                      <span>تحديث ذكي</span>
                    </PhantomLoader>
                  </button>
                </Tooltip>
              </div>

              {loadingMemory ? (
                <PhantomLoader loading label="تحميل ملف التعريف">
                  <div className={styles.memoryLoading}>
                    <div>ملخص العميل</div>
                    <div>الحقائق المكتشفة من المحادثات</div>
                    <div>الاعتراضات والمحفزات</div>
                  </div>
                </PhantomLoader>
              ) : (
                <>
                  <div className={styles.detailField}>
                    <label className={styles.detailLabel}>ملخص العميل</label>
                    <textarea
                      className={`neon-input ${styles.detailTextarea}`}
                      style={{ minHeight: '60px', fontSize: '0.85rem' }}
                      placeholder="ملخص طويل المدى للعميل..."
                      value={editableSummary}
                      onChange={(e) => setEditableSummary(e.target.value)}
                    />
                  </div>

                  <div className={styles.detailField}>
                    <label className={styles.detailLabel}>الحقائق المكتشفة</label>
                    <input
                      type="text"
                      className={`neon-input ${styles.detailInput}`}
                      style={{ fontSize: '0.85rem' }}
                      placeholder="حقيقة 1, حقيقة 2..."
                      value={editableFacts}
                      onChange={(e) => setEditableFacts(e.target.value)}
                    />
                  </div>

                  <div style={{ display: 'flex', gap: 'var(--space-sm)' }}>
                    <div className={styles.detailField} style={{ flex: 1 }}>
                      <label className={styles.detailLabel}>الاعتراضات</label>
                      <input
                        type="text"
                        className={`neon-input ${styles.detailInput}`}
                        style={{ fontSize: '0.85rem' }}
                        placeholder="الاعتراضات..."
                        value={editableObjections}
                        onChange={(e) => setEditableObjections(e.target.value)}
                      />
                    </div>
                    <div className={styles.detailField} style={{ flex: 1 }}>
                      <label className={styles.detailLabel}>المحفزات</label>
                      <input
                        type="text"
                        className={`neon-input ${styles.detailInput}`}
                        style={{ fontSize: '0.85rem' }}
                        placeholder="المحفزات..."
                        value={editableTriggers}
                        onChange={(e) => setEditableTriggers(e.target.value)}
                      />
                    </div>
                  </div>
                </>
              )}
            </div>

            {/* Save Profile Button */}
            <button
              onClick={handleUpdateCustomer}
              className={`neon-btn ${saveSuccess ? styles.btnSaveSuccess : ''}`}
              disabled={savingCustomer}
              style={{ marginTop: 'var(--space-xs)', justifyContent: 'center', transition: 'all 0.3s var(--transition-normal)' }}
            >
              {savingCustomer ? (
                <PhantomLoader loading label="حفظ بيانات العميل">
                  <span>حفظ بيانات العميل</span>
                </PhantomLoader>
              ) : saveSuccess ? (
                <span style={{ display: 'flex', alignItems: 'center', gap: '6px', animation: 'scaleUp 0.25s ease' }}>
                  <CheckSquare size={16} /> تم الحفظ بنجاح! ✨
                </span>
              ) : (
                <>
                  <span>حفظ بيانات العميل</span>
                  <kbd style={{ marginRight: '8px' }}>⌘S</kbd>
                </>
              )}
            </button>
          </div>
        ) : (
          <div className={styles.noActiveChat}>
            <p style={{ color: 'hsl(var(--text-muted))' }}>لم يتم اختيار عميل</p>
          </div>
        )}
      </div>
    </div>
  );
}
