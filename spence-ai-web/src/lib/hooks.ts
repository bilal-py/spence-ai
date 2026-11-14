import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useSearchParams } from 'react-router';
import { api } from './api';
import { parsePeriod } from './format';

export const keys = {
  settings: ['settings'] as const,
  categories: ['categories'] as const,
  accounts: ['accounts'] as const,
  rules: ['rules'] as const,
  imports: ['imports'] as const,
  transactions: ['transactions'] as const,
  overview: ['overview'] as const,
  pots: ['pots'] as const,
  wrapped: ['wrapped'] as const,
  pet: ['pet'] as const,
};

export function useSettings() {
  return useQuery({ queryKey: keys.settings, queryFn: api.settings.get, staleTime: 60_000 });
}

export function useCurrency(): string {
  return useSettings().data?.currency ?? 'USD';
}

export function useCategories() {
  return useQuery({ queryKey: keys.categories, queryFn: api.categories.list, staleTime: 30_000 });
}

export function useAccounts() {
  return useQuery({ queryKey: keys.accounts, queryFn: api.accounts.list, staleTime: 30_000 });
}

/** Anything that changes transactions invalidates every view derived from them. */
export function useInvalidateMoney() {
  const qc = useQueryClient();
  return useCallback(() => {
    for (const key of [keys.transactions, keys.overview, keys.categories, keys.accounts, keys.imports, keys.rules, keys.pots, keys.wrapped, keys.pet]) {
      qc.invalidateQueries({ queryKey: key });
    }
  }, [qc]);
}

/** The selected period lives in the URL (?p=2026-11 or ?p=2026) so it survives reloads and is shared across pages. */
export function usePeriod() {
  const [params, setParams] = useSearchParams();
  const period = parsePeriod(params.get('p'));
  const setPeriod = useCallback(
    (key: string) =>
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.set('p', key);
          next.delete('page');
          return next;
        },
        { replace: true },
      ),
    [setParams],
  );
  return [period, setPeriod] as const;
}
