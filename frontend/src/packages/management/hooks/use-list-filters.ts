'use client';

import { useCallback, useEffect, useSyncExternalStore } from 'react';
import { useSearchParams } from 'next/navigation';

type HistoryMode = 'push' | 'replace';
interface FilterConfig<Status extends string> {
  statuses: readonly Status[];
  defaultStatus: Status;
  search?: boolean;
  pagination?: boolean;
}
interface ListFilters<Status extends string> {
  search: string;
  status: Status;
  page: number;
  pageSize: number;
}

const sanitizeSearch = (search: string) => search.replace(/\p{Cc}/gu, '').slice(0, 200);
const FILTERS_CHANGED = 'management-list-filters-changed';
const browserQuery = () => window.location.search;

function subscribeToFilters(listener: () => void) {
  window.addEventListener('popstate', listener);
  window.addEventListener(FILTERS_CHANGED, listener);
  return () => {
    window.removeEventListener('popstate', listener);
    window.removeEventListener(FILTERS_CHANGED, listener);
  };
}

function readFilters<Status extends string>(params: URLSearchParams, config: FilterConfig<Status>): ListFilters<Status> {
  const status = params.get('status');
  const page = params.get('page') ?? '1';
  const pageSize = params.get('pageSize') ?? '10';
  const parsedPageSize = ['5', '10', '25', '50'].includes(pageSize) ? Number(pageSize) : 10;
  return {
    search: config.search === false ? '' : sanitizeSearch(params.get('q') ?? ''),
    status: config.statuses.find(candidate => candidate === status) ?? config.defaultStatus,
    page: config.pagination !== false && /^[1-9]\d*$/.test(page) && (Number(page) - 1) * parsedPageSize <= 2_147_483_647 ? Number(page) : 1,
    pageSize: config.pagination === false ? 10 : parsedPageSize,
  };
}

function writeFilters<Status extends string>(url: URL, filters: ListFilters<Status>, config: FilterConfig<Status>) {
  const entries = {
    ...(config.search === false ? {} : { q: filters.search || null }),
    status: filters.status === config.defaultStatus ? null : filters.status,
    ...(config.pagination === false ? {} : {
      page: filters.page === 1 ? null : String(filters.page),
      pageSize: filters.pageSize === 10 ? null : String(filters.pageSize),
    }),
  };
  for (const [key, value] of Object.entries(entries)) {
    if (value === null) url.searchParams.delete(key);
    else url.searchParams.set(key, value);
  }
}

export function useListFilters<Status extends string>(config: FilterConfig<Status>) {
  const routerQuery = useSearchParams().toString();
  // Native history reaches Next in a transition; controlled typing needs its synchronous URL snapshot.
  const query = useSyncExternalStore(subscribeToFilters, browserQuery, () => routerQuery ? `?${routerQuery}` : '');
  const filters = readFilters(new URLSearchParams(query), config);
  const update = useCallback((changes: Partial<ListFilters<Status>>, mode: HistoryMode) => {
    const url = new URL(window.location.href);
    const current = readFilters(url.searchParams, config);
    writeFilters(url, { ...current, ...changes }, config);
    if (url.href !== window.location.href) {
      window.history[mode === 'push' ? 'pushState' : 'replaceState'](null, '', `${url.pathname}${url.search}${url.hash}`);
      window.dispatchEvent(new Event(FILTERS_CHANGED));
    }
  }, [config]);

  useEffect(() => { update({}, 'replace'); }, [query, update]);

  const setSearch = (search: string) => update({ search: sanitizeSearch(search), page: 1 }, 'replace');
  const setStatus = (status: Status) => update({ status, page: 1 }, 'push');
  const setPage = useCallback((page: number, mode: HistoryMode = 'push') => update({ page }, mode), [update]);
  const setPageSize = (pageSize: number) => update({ pageSize, page: 1 }, 'push');
  const clearFilters = () => update({ search: '', status: config.defaultStatus, page: 1, pageSize: 10 }, 'push');

  return { ...filters, setSearch, setStatus, setPage, setPageSize, clearFilters,
    hasFilters: Boolean(filters.search) || filters.status !== config.defaultStatus || filters.pageSize !== 10 };
}
