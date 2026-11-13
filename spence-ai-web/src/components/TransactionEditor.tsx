import { useMutation } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react';
import { api, type Transaction, type TransactionInput, type TransactionType } from '../lib/api';
import { today } from '../lib/format';
import { useCategories, useCurrency, useInvalidateMoney } from '../lib/hooks';
import { CategorySelect } from './CategorySelect';
import { Dialog } from './Dialog';
import { useToast } from './Toast';
import { Button, ErrorNote, Segmented } from './ui';

interface EditorApi {
  /** Open with a transaction to edit it, or with nothing to add a new one. */
  open: (tx?: Transaction) => void;
}

const EditorContext = createContext<EditorApi>({ open: () => {} });

// eslint-disable-next-line react-refresh/only-export-components
export const useTransactionEditor = () => useContext(EditorContext);

export function TransactionEditorProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{ open: boolean; tx?: Transaction; nonce: number }>({ open: false, nonce: 0 });
  const open = useCallback((tx?: Transaction) => setState((s) => ({ open: true, tx, nonce: s.nonce + 1 })), []);
  const close = useCallback(() => setState((s) => ({ ...s, open: false })), []);
  const value = useMemo(() => ({ open }), [open]);

  // "N" adds a transaction from anywhere, unless the user is typing.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (e.key.toLowerCase() !== 'n' || e.metaKey || e.ctrlKey || e.altKey) return;
      if (target.closest('input, textarea, select, [contenteditable], dialog')) return;
      e.preventDefault();
      open();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  return (
    <EditorContext.Provider value={value}>
      {children}
      <Dialog open={state.open} onClose={close} title={state.tx ? 'Edit transaction' : 'Add a transaction'}>
        <TransactionForm key={state.nonce} tx={state.tx} onDone={close} />
      </Dialog>
    </EditorContext.Provider>
  );
}

function TransactionForm({ tx, onDone }: { tx?: Transaction; onDone: () => void }) {
  const { data: categories = [] } = useCategories();
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const toast = useToast();

  const [type, setType] = useState<TransactionType>(tx?.type ?? 'Expense');
  const [amount, setAmount] = useState(tx ? String(tx.amount) : '');
  const [date, setDate] = useState(tx?.date ?? today());
  const [description, setDescription] = useState(tx?.description ?? '');
  const [merchant, setMerchant] = useState(tx?.merchant ?? '');
  const [notes, setNotes] = useState(tx?.notes ?? '');
  const [categoryId, setCategoryId] = useState<number | null>(tx?.categoryId ?? null);
  const [remember, setRemember] = useState(false);

  const save = useMutation({
    mutationFn: (input: TransactionInput) => (tx ? api.transactions.update(tx.id, input) : api.transactions.create(input)),
    onSuccess: () => {
      invalidate();
      toast.show(tx ? 'Transaction updated' : 'Transaction added');
      onDone();
    },
  });

  const remove = useMutation({
    mutationFn: () => api.transactions.remove(tx!.id),
    onSuccess: () => {
      invalidate();
      toast.show('Transaction deleted');
      onDone();
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate({
      type,
      amount: Number(amount),
      date,
      description: description.trim() || merchant.trim(),
      merchant: merchant.trim() || null,
      notes: notes.trim() || null,
      categoryId,
      rememberCategory: remember,
    });
  };

  const categoryChanged = tx ? tx.categoryId !== categoryId : false;

  return (
    <form onSubmit={submit} className="grid gap-4">
      <Segmented
        label="Type"
        value={type}
        onChange={setType}
        options={[
          { value: 'Expense', label: 'Expense' },
          { value: 'Income', label: 'Income' },
          { value: 'Transfer', label: 'Transfer' },
        ]}
      />

      <div className="grid grid-cols-[1fr_auto] gap-3">
        <div>
          <label className="label" htmlFor="tx-amount">
            Amount ({currency})
          </label>
          <input
            id="tx-amount"
            className="field num font-display !h-12 !text-2xl font-semibold"
            inputMode="decimal"
            type="number"
            step="0.01"
            min="0.01"
            required
            autoFocus
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            placeholder="0.00"
          />
        </div>
        <div>
          <label className="label" htmlFor="tx-date">
            Date
          </label>
          <input id="tx-date" className="field !h-12" type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <div>
          <label className="label" htmlFor="tx-merchant">
            Merchant or payee
          </label>
          <input id="tx-merchant" className="field" value={merchant} onChange={(e) => setMerchant(e.target.value)} placeholder="e.g. Corner bakery" />
        </div>
        <div>
          <label className="label" htmlFor="tx-category">
            Category
          </label>
          <CategorySelect id="tx-category" categories={categories} value={categoryId} onChange={setCategoryId} type={type} />
        </div>
      </div>

      <div>
        <label className="label" htmlFor="tx-description">
          Description
        </label>
        <input
          id="tx-description"
          className="field"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="What was it for?"
          required={!merchant.trim()}
        />
      </div>

      <div>
        <label className="label" htmlFor="tx-notes">
          Notes
        </label>
        <textarea id="tx-notes" className="field !h-auto py-2" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
      </div>

      {(categoryChanged || !tx) && categoryId !== null && (merchant.trim() || description.trim()) && (
        <label className="flex items-center gap-2 text-sm text-ink-soft">
          <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={remember} onChange={(e) => setRemember(e.target.checked)} />
          Always use this category for {merchant.trim() || 'this merchant'}
        </label>
      )}

      <ErrorNote error={save.error ?? remove.error} />

      <div className="mt-1 flex items-center justify-between gap-2">
        {tx ? (
          <Button type="button" variant="danger" busy={remove.isPending} onClick={() => remove.mutate()}>
            Delete
          </Button>
        ) : (
          <span className="text-xs text-ink-faint">Tip: press N anywhere to add one</span>
        )}
        <div className="flex gap-2">
          <Button type="button" variant="ghost" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" variant="primary" busy={save.isPending}>
            {tx ? 'Save changes' : 'Add transaction'}
          </Button>
        </div>
      </div>
    </form>
  );
}
