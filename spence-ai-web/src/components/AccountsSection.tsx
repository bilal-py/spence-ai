import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Banknote, CreditCard, Landmark, Plus, Trash2, Wallet, type LucideIcon } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { api, type Account, type AccountInput, type AccountKind } from '../lib/api';
import { useAccounts, useInvalidateMoney } from '../lib/hooks';
import { accountLabel } from './AccountSelect';
import { SWATCHES } from './CategoryForm';
import { Dialog } from './Dialog';
import { useToast } from './Toast';
import { Button, ErrorNote, SectionTitle, Segmented, Spinner } from './ui';

// eslint-disable-next-line react-refresh/only-export-components
export const ACCOUNT_KINDS: { value: AccountKind; label: string; icon: LucideIcon; hint: string }[] = [
  { value: 'Bank', label: 'Bank', icon: Landmark, hint: 'Quick add uses it for “upi” or “bank”' },
  { value: 'Card', label: 'Card', icon: CreditCard, hint: 'Quick add uses it for “card”' },
  { value: 'Wallet', label: 'Wallet', icon: Wallet, hint: 'Quick add uses it for “gpay”, “paytm”, “wallet”' },
  { value: 'Cash', label: 'Cash', icon: Banknote, hint: 'Quick add uses it for “cash”' },
];

export function AccountsSection() {
  const { data: accounts, error, isPending } = useAccounts();
  const [editing, setEditing] = useState<{ open: boolean; account?: Account; nonce: number }>({ open: false, nonce: 0 });
  const open = (account?: Account) => setEditing((s) => ({ open: true, account, nonce: s.nonce + 1 }));
  const close = () => setEditing((s) => ({ ...s, open: false }));

  return (
    <section className="max-w-3xl">
      <SectionTitle
        aside={
          <Button size="sm" onClick={() => open()}>
            <Plus className="size-4" aria-hidden /> Add account
          </Button>
        }
      >
        Accounts and cards
      </SectionTitle>
      <p className="mb-4 max-w-prose text-sm text-ink-soft">
        Tag transactions with the bank account, card or wallet they went through, then filter by it. Pick the account when you review an import, or
        type its name (or “card”, “cash”, “upi”) in quick add.
      </p>
      <ErrorNote error={error} />
      {isPending && <Spinner />}
      {accounts?.length === 0 && <p className="text-sm text-ink-faint">No accounts yet.</p>}
      <ul className="divide-y divide-line">
        {accounts?.map((a) => {
          const Icon = ACCOUNT_KINDS.find((k) => k.value === a.kind)?.icon ?? Landmark;
          return (
            <li key={a.id}>
              <button onClick={() => open(a)} className={clsx('grid w-full grid-cols-[2rem_1fr_auto] items-center gap-3 py-2.5 text-left hover:bg-surface/60', a.isArchived && 'opacity-55')}>
                <span className="grid size-8 place-items-center rounded-md" style={{ background: `${a.color}22`, color: a.color }}>
                  <Icon className="size-4" aria-hidden />
                </span>
                <span>
                  <span className="font-medium">{accountLabel(a)}</span>
                  <span className="ml-2 text-xs text-ink-faint">
                    {a.kind} · {a.transactionCount} transactions{a.isArchived && ' · archived'}
                  </span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>

      <Dialog open={editing.open} onClose={close} title={editing.account ? `Edit ${editing.account.name}` : 'Add an account'}>
        <AccountForm key={editing.nonce} account={editing.account} onDone={close} />
      </Dialog>
    </section>
  );
}

function AccountForm({ account, onDone }: { account?: Account; onDone: () => void }) {
  const toast = useToast();
  const invalidate = useInvalidateMoney();
  const [form, setForm] = useState<AccountInput>({
    name: account?.name ?? '',
    kind: account?.kind ?? 'Bank',
    last4: account?.last4 ?? null,
    color: account?.color ?? SWATCHES[3],
    isArchived: account?.isArchived ?? false,
  });

  const save = useMutation({
    mutationFn: () => (account ? api.accounts.update(account.id, form) : api.accounts.create(form)),
    onSuccess: () => {
      invalidate();
      toast.show(account ? 'Account saved' : 'Account added');
      onDone();
    },
  });

  const remove = useMutation({
    mutationFn: () => api.accounts.remove(account!.id),
    onSuccess: () => {
      invalidate();
      toast.show(`Deleted ${account!.name}`);
      onDone();
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  const kind = ACCOUNT_KINDS.find((k) => k.value === form.kind)!;

  return (
    <form onSubmit={submit} className="grid gap-5">
      <div className="grid grid-cols-[1fr_7rem] gap-3">
        <div>
          <label className="label" htmlFor="acc-name">
            Name
          </label>
          <input
            id="acc-name"
            className="field"
            required
            maxLength={60}
            autoFocus
            placeholder="e.g. HDFC Savings"
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
          />
        </div>
        <div>
          <label className="label" htmlFor="acc-last4">
            Last digits
          </label>
          <input
            id="acc-last4"
            className="field num"
            inputMode="numeric"
            pattern="[0-9]{2,4}"
            maxLength={4}
            placeholder="4521"
            value={form.last4 ?? ''}
            onChange={(e) => setForm({ ...form, last4: e.target.value.replace(/\D/g, '') || null })}
          />
        </div>
      </div>

      <div>
        <Segmented
          label="Kind"
          value={form.kind}
          onChange={(k) => setForm({ ...form, kind: k })}
          options={ACCOUNT_KINDS.map((k) => ({ value: k.value, label: <><k.icon className="size-3.5" aria-hidden /> {k.label}</> }))}
        />
        <p className="mt-1.5 text-xs text-ink-faint">{kind.hint}</p>
      </div>

      <fieldset>
        <legend className="label">Color</legend>
        <div className="flex flex-wrap gap-2">
          {SWATCHES.map((c) => (
            <button
              key={c}
              type="button"
              aria-label={`Color ${c}`}
              aria-pressed={form.color === c}
              onClick={() => setForm({ ...form, color: c })}
              className={clsx('size-7 rounded-full ring-offset-2 ring-offset-surface', form.color === c && 'ring-2 ring-ink')}
              style={{ background: c }}
            />
          ))}
        </div>
      </fieldset>

      {account && (
        <label className="flex items-center gap-2 text-sm text-ink-soft">
          <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={form.isArchived} onChange={(e) => setForm({ ...form, isArchived: e.target.checked })} />
          Archived: hide it from pickers but keep it on past transactions
        </label>
      )}

      <ErrorNote error={save.error ?? remove.error} />

      <div className="flex items-center justify-between gap-2">
        {account ? (
          <Button type="button" variant="danger" busy={remove.isPending} onClick={() => remove.mutate()} title="Its transactions are kept, without an account">
            <Trash2 className="size-4" aria-hidden /> Delete
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button type="button" variant="ghost" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" variant="primary" busy={save.isPending}>
            {account ? 'Save changes' : 'Add account'}
          </Button>
        </div>
      </div>
    </form>
  );
}
