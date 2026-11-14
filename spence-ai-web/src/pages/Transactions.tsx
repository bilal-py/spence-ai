import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ArrowDown, ArrowUp, Download, FileText, Image as ImageIcon, Search, Sheet, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { CategorySelect } from '../components/CategorySelect';
import { PeriodSwitcher } from '../components/PeriodSwitcher';
import { useToast } from '../components/Toast';
import { useTransactionEditor } from '../components/TransactionEditor';
import { Button, Empty, ErrorNote, PageHeader, Segmented, Spinner } from '../components/ui';
import { api, type Transaction, type TransactionQuery, type TransactionType } from '../lib/api';
import { money, shortDate } from '../lib/format';
import { keys, useCategories, useCurrency, useInvalidateMoney, usePeriod } from '../lib/hooks';
import { CategoryIcon } from '../lib/icons';

const PAGE_SIZE = 50;
const SOURCE_ICON = { Pdf: FileText, Csv: Sheet, Image: ImageIcon, Manual: null } as const;

export default function Transactions() {
  const [period] = usePeriod();
  const [params, setParams] = useSearchParams();
  const currency = useCurrency();
  const editor = useTransactionEditor();
  const toast = useToast();
  const invalidate = useInvalidateMoney();
  const { data: categories = [] } = useCategories();

  const category = params.get('category');
  const type = (params.get('type') as TransactionType | null) ?? undefined;
  const sort = (params.get('sort') as 'date' | 'amount' | null) ?? 'date';
  const dir = (params.get('dir') as 'asc' | 'desc' | null) ?? 'desc';
  const page = Number(params.get('page') ?? 1);
  const [search, setSearch] = useState(params.get('q') ?? '');
  const [selection, setSelection] = useState<{ key: string; ids: Set<number> }>({ key: '', ids: new Set() });

  const setParam = (key: string, value: string | null) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value === null || value === '') next.delete(key);
        else next.set(key, value);
        if (key !== 'page') next.delete('page');
        return next;
      },
      { replace: true },
    );

  // Debounce the search box into the URL.
  useEffect(() => {
    const t = setTimeout(() => {
      if ((params.get('q') ?? '') !== search) setParam('q', search);
    }, 250);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search]);

  const query: TransactionQuery = {
    from: period.from,
    to: period.to,
    categoryId: category !== null ? Number(category) : undefined,
    type,
    search: params.get('q') ?? undefined,
    sort,
    dir,
    page,
    pageSize: PAGE_SIZE,
  };

  const { data, error, isPending, isPlaceholderData } = useQuery({
    queryKey: [...keys.transactions, query],
    queryFn: () => api.transactions.list(query),
    placeholderData: keepPreviousData,
  });

  // Selection is tied to the exact filters + page, so it never survives a change of either.
  const queryKey = JSON.stringify(query);
  const selected = selection.key === queryKey ? selection.ids : new Set<number>();
  const setSelected = (next: Set<number> | ((prev: Set<number>) => Set<number>)) =>
    setSelection({ key: queryKey, ids: typeof next === 'function' ? next(selected) : next });

  const quickCategorize = useMutation({
    mutationFn: ({ ids, categoryId }: { ids: number[]; categoryId: number | null }) => api.transactions.bulkCategorize(ids, categoryId),
    onSuccess: (r) => {
      invalidate();
      setSelected(new Set());
      if (r.updated > 1) toast.show(`Moved ${r.updated} transactions`);
    },
  });

  const bulkDelete = useMutation({
    mutationFn: (ids: number[]) => api.transactions.bulkDelete(ids),
    onSuccess: (r) => {
      invalidate();
      setSelected(new Set());
      toast.show(`Deleted ${r.deleted} ${r.deleted === 1 ? 'transaction' : 'transactions'}`);
    },
  });

  const items = data?.items ?? [];
  const allSelected = items.length > 0 && items.every((t) => selected.has(t.id));
  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  const toggleSort = (column: 'date' | 'amount') => {
    if (sort === column) setParam('dir', dir === 'desc' ? 'asc' : 'desc');
    else setParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('sort', column);
      next.set('dir', 'desc');
      next.delete('page');
      return next;
    }, { replace: true });
  };

  return (
    <>
      <PageHeader title="Transactions">
        <PeriodSwitcher />
      </PageHeader>

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <label className="relative min-w-56 flex-1">
          <span className="sr-only">Search</span>
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-faint" aria-hidden />
          <input className="field pl-9" placeholder="Search descriptions, merchants, notes" value={search} onChange={(e) => setSearch(e.target.value)} />
        </label>
        <select
          aria-label="Category"
          className="field w-auto"
          value={category ?? ''}
          onChange={(e) => setParam('category', e.target.value)}
        >
          <option value="">All categories</option>
          <option value="0">Uncategorized</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <Segmented
          label="Type"
          value={type ?? 'all'}
          onChange={(v) => setParam('type', v === 'all' ? null : v)}
          options={[
            { value: 'all', label: 'All' },
            { value: 'Expense', label: 'Spent' },
            { value: 'Income', label: 'Received' },
            { value: 'Refund', label: 'Refunds' },
            { value: 'Transfer', label: 'Transfers' },
          ]}
        />
        <a href={api.transactions.exportUrl(query)} className="inline-flex h-9 items-center gap-1.5 rounded-md px-3 text-[0.93rem] text-ink-soft hover:bg-sunken hover:text-ink">
          <Download className="size-4" aria-hidden /> Export CSV
        </a>
      </div>

      {data && (
        <p className="mb-3 flex flex-wrap gap-x-5 text-sm text-ink-soft">
          <span>
            <span className="num font-medium text-ink">{data.total}</span> transactions
          </span>
          <span>
            <span className="num font-medium text-ink">{money(data.totalExpense, currency)}</span> spent{type === undefined && ' after refunds'}
          </span>
          {data.totalIncome > 0 && (
            <span>
              <span className="num font-medium text-pine">{money(data.totalIncome, currency)}</span> received
            </span>
          )}
        </p>
      )}

      {selected.size > 0 && (
        <div className="sticky top-2 z-10 mb-3 flex flex-wrap items-center gap-3 rounded-lg bg-ink px-4 py-2.5 text-paper shadow-lg">
          <span className="text-sm font-medium">{selected.size} selected</span>
          <CategorySelect
            aria-label="Move selected to category"
            className="h-8 rounded-md border-0 bg-paper/10 px-2 text-sm text-paper [&>*]:text-ink"
            categories={categories}
            value={null}
            onChange={(categoryId) => quickCategorize.mutate({ ids: [...selected], categoryId })}
          />
          <button
            className="ml-auto inline-flex items-center gap-1.5 text-sm text-paper/80 hover:text-paper"
            onClick={() => bulkDelete.mutate([...selected])}
          >
            <Trash2 className="size-4" aria-hidden /> Delete
          </button>
          <button className="text-sm text-paper/60 hover:text-paper" onClick={() => setSelected(new Set())}>
            Clear
          </button>
        </div>
      )}

      <ErrorNote error={error ?? quickCategorize.error ?? bulkDelete.error} className="mb-3" />

      {isPending ? (
        <Spinner />
      ) : items.length === 0 ? (
        <Empty title="No transactions match">Try another period or clear the filters. Press N to add one.</Empty>
      ) : (
        <div className={clsx('overflow-x-auto rounded-lg border border-line bg-surface', isPlaceholderData && 'opacity-60')}>
          <table className="w-full min-w-[44rem] text-[0.93rem]">
            <thead>
              <tr className="border-b border-line text-left text-[0.8rem] text-ink-soft">
                <th className="w-10 py-2.5 pl-4">
                  <input
                    type="checkbox"
                    aria-label="Select all on this page"
                    className="size-4 accent-[var(--pine)]"
                    checked={allSelected}
                    onChange={() => setSelected(allSelected ? new Set() : new Set(items.map((t) => t.id)))}
                  />
                </th>
                <th className="w-24 font-medium">
                  <SortButton label="Date" active={sort === 'date'} dir={dir} onClick={() => toggleSort('date')} />
                </th>
                <th className="font-medium">Description</th>
                <th className="w-52 font-medium">Category</th>
                <th className="w-36 pr-4 text-right font-medium">
                  <SortButton label="Amount" active={sort === 'amount'} dir={dir} onClick={() => toggleSort('amount')} alignRight />
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {items.map((t) => (
                <Row
                  key={t.id}
                  tx={t}
                  currency={currency}
                  selected={selected.has(t.id)}
                  onSelect={(on) =>
                    setSelected((prev) => {
                      const next = new Set(prev);
                      if (on) next.add(t.id);
                      else next.delete(t.id);
                      return next;
                    })
                  }
                  onOpen={() => editor.open(t)}
                  onCategory={(categoryId) => quickCategorize.mutate({ ids: [t.id], categoryId })}
                  categories={categories}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}

      {pages > 1 && (
        <nav className="mt-4 flex items-center justify-end gap-2 text-sm" aria-label="Pages">
          <Button size="sm" disabled={page <= 1} onClick={() => setParam('page', String(page - 1))}>
            Previous
          </Button>
          <span className="num text-ink-soft">
            Page {page} of {pages}
          </span>
          <Button size="sm" disabled={page >= pages} onClick={() => setParam('page', String(page + 1))}>
            Next
          </Button>
        </nav>
      )}
    </>
  );
}

function SortButton({ label, active, dir, onClick, alignRight }: { label: string; active: boolean; dir: string; onClick: () => void; alignRight?: boolean }) {
  const Arrow = dir === 'asc' ? ArrowUp : ArrowDown;
  return (
    <button onClick={onClick} className={clsx('inline-flex items-center gap-1 hover:text-ink', active && 'text-ink', alignRight && 'flex-row-reverse')}>
      {label}
      {active && <Arrow className="size-3" aria-hidden />}
    </button>
  );
}

function Row({
  tx,
  currency,
  selected,
  onSelect,
  onOpen,
  onCategory,
  categories,
}: {
  tx: Transaction;
  currency: string;
  selected: boolean;
  onSelect: (on: boolean) => void;
  onOpen: () => void;
  onCategory: (id: number | null) => void;
  categories: ReturnType<typeof useCategories>['data'] & object;
}) {
  const SourceIcon = SOURCE_ICON[tx.source];
  return (
    <tr className={clsx('group', selected ? 'bg-pine-soft/40' : 'hover:bg-paper/60')}>
      <td className="py-2.5 pl-4">
        <input type="checkbox" aria-label={`Select ${tx.description}`} className="size-4 accent-[var(--pine)]" checked={selected} onChange={(e) => onSelect(e.target.checked)} />
      </td>
      <td className="num whitespace-nowrap text-ink-soft">{shortDate(tx.date)}</td>
      <td className="max-w-0 pr-4">
        <button onClick={onOpen} className="block w-full text-left">
          <span className="flex items-center gap-1.5">
            <span className="truncate font-medium group-hover:underline group-hover:decoration-line group-hover:underline-offset-4">
              {tx.merchant ?? tx.description}
            </span>
            {SourceIcon && <SourceIcon className="size-3.5 shrink-0 text-ink-faint" aria-label={`Imported from ${tx.source}`} />}
          </span>
          {tx.merchant && <span className="block truncate text-xs text-ink-faint">{tx.description}</span>}
        </button>
      </td>
      <td className="pr-3">
        <div className="flex items-center gap-2">
          <span className="grid size-6 shrink-0 place-items-center rounded" style={{ background: `${tx.categoryColor ?? '#9a9a92'}22`, color: tx.categoryColor ?? 'var(--ink-faint)' }}>
            <CategoryIcon name={tx.categoryIcon} className="size-3.5" />
          </span>
          <CategorySelect
            aria-label={`Category for ${tx.description}`}
            className="h-8 w-full min-w-0 rounded-md border border-transparent bg-transparent px-1 text-sm hover:border-line focus:border-pine focus:outline-none"
            categories={categories}
            value={tx.categoryId}
            type={tx.type}
            onChange={onCategory}
          />
        </div>
      </td>
      <td
        className={clsx(
          'num pr-4 text-right whitespace-nowrap',
          tx.type === 'Income' || tx.type === 'Refund' ? 'font-medium text-pine' : tx.type === 'Transfer' ? 'text-ink-faint' : 'font-medium',
        )}
      >
        {tx.type === 'Income' || tx.type === 'Refund' ? '+' : tx.type === 'Transfer' ? '⇄ ' : ''}
        {money(tx.amount, currency)}
      </td>
    </tr>
  );
}
