import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { CornerDownLeft, LoaderCircle, Sparkles, Wand2, Zap } from 'lucide-react';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { api, type Account, type Category, type QuickAddRow, type TransactionInput, type TransactionType } from '../lib/api';
import { money, today } from '../lib/format';
import { useAccounts, useCategories, useCurrency, useInvalidateMoney } from '../lib/hooks';
import { AccountSelect } from './AccountSelect';
import { CategorySelect } from './CategorySelect';
import { Dialog } from './Dialog';
import { useToast } from './Toast';
import { useTransactionEditor } from './TransactionEditor';
import { Button, ErrorNote } from './ui';

interface QuickAddApi {
  open: () => void;
}

const QuickAddContext = createContext<QuickAddApi>({ open: () => {} });

// eslint-disable-next-line react-refresh/only-export-components
export const useQuickAdd = () => useContext(QuickAddContext);

/** Quick add from anywhere: the N key or the Add button. Type a line, check the card, press Enter. */
export function QuickAddProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState({ open: false, nonce: 0 });
  const open = useCallback(() => setState((s) => ({ open: true, nonce: s.nonce + 1 })), []);
  const close = useCallback(() => setState((s) => ({ ...s, open: false })), []);
  const value = useMemo(() => ({ open }), [open]);

  // "N" opens quick add from anywhere, unless the user is typing or a dialog is open.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (e.key.toLowerCase() !== 'n' || e.metaKey || e.ctrlKey || e.altKey) return;
      if (target.closest('input, textarea, select, [contenteditable], dialog') || document.querySelector('dialog[open]')) return;
      e.preventDefault();
      open();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  return (
    <QuickAddContext.Provider value={value}>
      {children}
      <Dialog open={state.open} onClose={close} title="Quick add" wide>
        <QuickAddForm key={state.nonce} onDone={close} />
      </Dialog>
    </QuickAddContext.Provider>
  );
}

/** A parsed row plus what the user changed on its card. */
interface Row extends QuickAddRow {
  /** What Spence suggested, to tell whether the user corrected the category. */
  suggestedCategoryId: number | null;
  aiGuess: boolean;
  remember: boolean;
}

type Edit = Partial<Pick<Row, 'amount' | 'merchant' | 'type' | 'categoryId' | 'date' | 'accountId' | 'remember'>>;

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return debounced;
}

function QuickAddForm({ onDone }: { onDone: () => void }) {
  const { data: categories = [] } = useCategories();
  const { data: accounts = [] } = useAccounts();
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  const editor = useTransactionEditor();

  const [text, setText] = useState('');
  const debounced = useDebounced(text.trim(), 250);
  // Edits are keyed by the entry's position and text, so retyping an entry starts it fresh.
  const [edits, setEdits] = useState<Record<string, Edit>>({});

  const parsed = useQuery({
    queryKey: ['quick-add', debounced],
    queryFn: () => api.quickAdd.parse(debounced, today()),
    enabled: debounced.length > 0,
    placeholderData: keepPreviousData,
    staleTime: Infinity,
  });
  const parsedRows = useMemo(() => (debounced ? (parsed.data ?? []) : []), [debounced, parsed.data]);

  // The AI only sees the merchant names the rules couldn't place.
  const unknown = parsedRows.filter((r) => r.categoryId === null && r.type !== 'Transfer' && r.merchant);
  const suggestKey = unknown.map((r) => `${r.merchant}|${r.type}`);
  const suggest = useQuery({
    queryKey: ['quick-add-suggest', suggestKey],
    queryFn: () => api.quickAdd.suggest(unknown.map((r) => ({ merchant: r.merchant, type: r.type }))),
    enabled: unknown.length > 0 && !parsed.isPlaceholderData,
    staleTime: Infinity,
    retry: false,
  });

  const rows: Row[] = parsedRows.map((r, i) => {
    const u = unknown.indexOf(r);
    const aiId = u >= 0 ? (suggest.data?.categoryIds[u] ?? null) : null;
    const suggested = r.categoryId ?? aiId;
    const edit = edits[`${i}|${r.text}`] ?? {};
    return { ...r, suggestedCategoryId: suggested, categoryId: suggested, aiGuess: aiId !== null, remember: true, ...edit };
  });

  const update = (i: number, patch: Edit) => {
    const key = `${i}|${parsedRows[i].text}`;
    setEdits((all) => ({ ...all, [key]: { ...all[key], ...patch } }));
  };

  const corrected = (r: Row) => r.type !== 'Transfer' && r.categoryId !== null && r.categoryId !== r.suggestedCategoryId;
  const problems = rows.flatMap((r, i) => [
    ...(!r.amount || r.amount <= 0 ? [`${rows.length > 1 ? `Entry ${i + 1}: ` : ''}add an amount`] : []),
    ...(!r.merchant.trim() ? [`${rows.length > 1 ? `Entry ${i + 1}: ` : ''}say what it was`] : []),
  ]);
  const waiting = parsed.isFetching || debounced !== text.trim();
  const canSave = rows.length > 0 && problems.length === 0 && !waiting;

  const save = useMutation({
    mutationFn: () =>
      api.transactions.createMany(
        rows.map(
          (r): TransactionInput => ({
            date: r.date,
            amount: Number(r.amount),
            type: r.type,
            description: r.merchant.trim(),
            merchant: r.merchant.trim(),
            notes: null,
            categoryId: r.type === 'Transfer' ? null : r.categoryId,
            rememberCategory: corrected(r) && r.remember,
            accountId: r.accountId,
          }),
        ),
      ),
    onSuccess: (saved) => {
      invalidate();
      const total = saved.reduce((s, t) => s + t.amount, 0);
      toast.show(saved.length === 1 ? `Added ${saved[0].merchant ?? saved[0].description}, ${money(total, currency)}` : `Added ${saved.length} transactions`);
      onDone();
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (canSave && !save.isPending) save.mutate();
  };

  const openFullForm = () => {
    const r = rows[0];
    onDone();
    editor.open(
      undefined,
      r && { date: r.date, amount: r.amount ?? undefined, type: r.type, description: r.merchant, merchant: r.merchant, categoryId: r.categoryId, accountId: r.accountId },
    );
  };

  return (
    <form onSubmit={submit} className="grid gap-4">
      <div className="relative">
        <Zap className="pointer-events-none absolute top-1/2 left-3.5 size-5 -translate-y-1/2 text-ochre" aria-hidden />
        <input
          aria-label="What did you spend?"
          className="field !h-14 pl-11 font-display !text-xl"
          autoFocus
          autoComplete="off"
          spellCheck={false}
          maxLength={2000}
          placeholder="chai 40 upi, uber 250 yesterday"
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
        {waiting && text.trim() && <LoaderCircle className="absolute top-1/2 right-3.5 size-4 -translate-y-1/2 animate-spin text-ink-faint" aria-hidden />}
      </div>

      {rows.length === 0 ? (
        <p className="text-sm text-ink-soft">
          Type an amount and what it was for. Add a day (<i>yesterday</i>, <i>mon</i>, <i>12 nov</i>), how you paid (<i>cash</i>, <i>card</i>, <i>upi</i>,
          or an account name) and words like <i>got</i>, <i>refund</i> or <i>card bill</i> to change the type. Separate several with commas.
        </p>
      ) : (
        <ul className="grid gap-2.5" aria-live="polite">
          {rows.map((r, i) => (
            <RowCard
              key={`${i}|${r.text}`}
              row={r}
              accounts={accounts}
              categories={categories}
              currency={currency}
              guessing={suggest.isFetching && unknown.includes(parsedRows[i])}
              corrected={corrected(r)}
              onChange={(patch) => update(i, patch)}
            />
          ))}
        </ul>
      )}

      {suggest.data?.error && unknown.length > 0 && <p className="text-xs text-ink-faint">AI categories unavailable: {suggest.data.error}</p>}
      {rows.length > 0 && problems.length > 0 && !waiting && <p className="text-sm text-brick">{problems.join('. ')}.</p>}
      <ErrorNote error={parsed.error ?? save.error} />

      <div className="flex items-center justify-between gap-2">
        <button type="button" onClick={openFullForm} className="text-sm text-ink-soft underline-offset-4 hover:text-ink hover:underline">
          Use the full form
        </button>
        <div className="flex gap-2">
          <Button type="button" variant="ghost" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" variant="primary" busy={save.isPending} disabled={!canSave}>
            {rows.length > 1 ? `Save ${rows.length}` : 'Save'} <CornerDownLeft className="size-3.5 opacity-70" aria-hidden />
          </Button>
        </div>
      </div>
    </form>
  );
}

const chip = 'field !h-8 w-auto px-2 text-sm';
const unsureChip = '!border-ochre bg-ochre-soft/50';

function RowCard({
  row: r,
  accounts,
  categories,
  currency,
  guessing,
  corrected,
  onChange,
}: {
  row: Row;
  accounts: Account[];
  categories: Category[];
  currency: string;
  guessing: boolean;
  corrected: boolean;
  onChange: (patch: Edit) => void;
}) {
  const unsure = (field: string) => r.unsure.includes(field);
  const categoryUnsure = r.type !== 'Transfer' && (r.categoryId === null || (r.aiGuess && !corrected));

  return (
    <li className="rounded-lg border border-line bg-paper/60 p-3">
      <div className="flex items-center gap-2">
        <label className="sr-only" htmlFor={`qa-amount-${r.text}`}>
          Amount ({currency})
        </label>
        <input
          id={`qa-amount-${r.text}`}
          className={clsx('field num font-display !h-10 w-32 !text-lg font-semibold', unsure('amount') && unsureChip)}
          type="number"
          inputMode="decimal"
          step="any"
          min="0.01"
          placeholder="0"
          value={r.amount ?? ''}
          onChange={(e) => onChange({ amount: e.target.value === '' ? null : Number(e.target.value) })}
        />
        <input
          aria-label="What it was"
          className={clsx('field !h-10 flex-1 font-medium', unsure('merchant') && unsureChip)}
          placeholder="What was it?"
          value={r.merchant}
          onChange={(e) => onChange({ merchant: e.target.value })}
        />
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        <select aria-label="Type" className={chip} value={r.type} onChange={(e) => onChange({ type: e.target.value as TransactionType })}>
          <option value="Expense">Expense</option>
          <option value="Income">Income</option>
          <option value="Refund">Refund</option>
          <option value="Transfer">Transfer</option>
        </select>
        {r.type !== 'Transfer' &&
          (guessing && r.categoryId === null ? (
            <span className="inline-flex h-8 items-center gap-1.5 rounded-md border border-dashed border-line px-2 text-sm text-ink-faint">
              <LoaderCircle className="size-3.5 animate-spin" aria-hidden /> Picking a category…
            </span>
          ) : (
            <CategorySelect
              aria-label="Category"
              className={clsx(chip, 'max-w-48', categoryUnsure && unsureChip)}
              categories={categories}
              type={r.type}
              value={r.categoryId}
              onChange={(categoryId) => onChange({ categoryId })}
            />
          ))}
        <input aria-label="Date" type="date" className={chip} value={r.date} max={today()} onChange={(e) => e.target.value && onChange({ date: e.target.value })} />
        {accounts.length > 0 && (
          <AccountSelect
            aria-label="Account"
            className={clsx(chip, 'max-w-44', unsure('account') && unsureChip)}
            accounts={accounts}
            value={r.accountId}
            onChange={(accountId) => onChange({ accountId })}
          />
        )}
      </div>

      {((r.matchedRule && r.categorySource === 'rule') || r.aiGuess || corrected) && r.type !== 'Transfer' && (
        <p className="mt-1.5 flex flex-wrap gap-x-3 px-0.5 text-xs text-ink-faint">
          {corrected ? (
            <button
              type="button"
              onClick={() => onChange({ remember: !r.remember })}
              aria-pressed={r.remember}
              title={r.remember ? `Spence will use this category for ${r.merchant} next time. Click for just this once.` : 'Only this entry changes. Click to remember it.'}
              className={clsx('inline-flex items-center gap-0.5 rounded font-medium underline-offset-2 hover:underline', r.remember ? 'text-pine' : 'text-ink-soft')}
            >
              <Wand2 className="size-3" aria-hidden /> {r.remember ? 'Remember next time' : 'Just this once'}
            </button>
          ) : r.matchedRule && r.categorySource === 'rule' ? (
            <span className="inline-flex items-center gap-0.5 text-pine">
              <Wand2 className="size-3" aria-hidden /> Your rule: {r.matchedRule}
            </span>
          ) : r.aiGuess ? (
            <span className="inline-flex items-center gap-0.5 text-ochre">
              <Sparkles className="size-3" aria-hidden /> AI guess, check it
            </span>
          ) : null}
        </p>
      )}
    </li>
  );
}
