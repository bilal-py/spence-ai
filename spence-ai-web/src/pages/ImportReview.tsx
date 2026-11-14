import { useMutation, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ArrowLeft, LoaderCircle, Wand2 } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { AccountSelect } from '../components/AccountSelect';
import { CategorySelect } from '../components/CategorySelect';
import { useToast } from '../components/Toast';
import { Button, Empty, ErrorNote, Spinner } from '../components/ui';
import { api, type StagedRow, type TransactionType } from '../lib/api';
import { money } from '../lib/format';
import { useAccounts, useCategories, useCurrency, useInvalidateMoney } from '../lib/hooks';

export default function ImportReview() {
  const { id = '' } = useParams();
  const batch = useQuery({
    queryKey: ['import', id],
    queryFn: () => api.imports.get(id),
    refetchInterval: (q) => (q.state.data?.status === 'Queued' || q.state.data?.status === 'Processing' ? 2000 : false),
  });

  const back = (
    <Link to="/import" className="mb-6 inline-flex items-center gap-1.5 text-sm text-ink-soft hover:text-ink">
      <ArrowLeft className="size-4" aria-hidden /> All imports
    </Link>
  );

  if (batch.isPending) return <>{back}<Spinner /></>;
  if (batch.error) return <>{back}<ErrorNote error={batch.error} /></>;

  const b = batch.data;
  if (b.status === 'Queued' || b.status === 'Processing') {
    return (
      <>
        {back}
        <div className="flex flex-col items-start gap-3 py-10">
          <LoaderCircle className="size-7 animate-spin text-ochre" aria-hidden />
          <h1 className="font-display text-3xl font-semibold tracking-[-0.01em]">Reading {b.fileName}</h1>
          <p className="max-w-prose text-ink-soft">
            The AI is pulling out each transaction and choosing categories. Local models can take a minute or two for a long statement. You can leave this
            page; the import keeps going.
          </p>
        </div>
      </>
    );
  }

  if (b.status !== 'ReadyForReview') {
    return (
      <>
        {back}
        <Empty title={b.status === 'Failed' ? `Couldn't read ${b.fileName}` : `${b.fileName} is already ${b.status === 'Committed' ? 'saved' : 'discarded'}`}>
          {b.error}
        </Empty>
      </>
    );
  }

  return (
    <>
      {back}
      <Review id={b.id} fileName={b.fileName} initial={b.rows ?? []} />
    </>
  );
}

interface EditableRow extends StagedRow {
  /** The AI's proposed new category, kept so the user can switch back to it. */
  suggestedNew: string | null;
  /** What Spence suggested, to tell whether the user corrected the row. */
  suggested: Pick<StagedRow, 'type' | 'categoryId' | 'newCategoryName'>;
  /** Learn a rule from this correction (true), or apply it just this once (false). */
  remember: boolean;
}

/** True when the user changed the type or category Spence suggested. */
function isCorrected(r: EditableRow) {
  if (r.type !== r.suggested.type) return true;
  if (r.type === 'Transfer') return false;
  return r.categoryId !== r.suggested.categoryId || (r.categoryId === null && r.newCategoryName !== r.suggested.newCategoryName);
}

function Review({ id, fileName, initial }: { id: string; fileName: string; initial: StagedRow[] }) {
  const navigate = useNavigate();
  const toast = useToast();
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const { data: categories = [] } = useCategories();
  const { data: accounts = [] } = useAccounts();
  const [accountId, setAccountId] = useState<number | null>(null);
  const [rows, setRows] = useState<EditableRow[]>(() =>
    initial.map((r) => ({
      ...r,
      suggestedNew: r.newCategoryName,
      suggested: { type: r.type, categoryId: r.categoryId, newCategoryName: r.newCategoryName },
      remember: true,
    })),
  );

  const update = (index: number, patch: Partial<EditableRow>) => setRows((all) => all.map((r) => (r.index === index ? { ...r, ...patch } : r)));

  const included = rows.filter((r) => r.include);
  // Spending net of refunds, matching the dashboard.
  const spent = included.reduce((s, r) => s + (r.type === 'Expense' ? 1 : r.type === 'Refund' ? -1 : 0) * Number(r.amount || 0), 0);
  const duplicates = rows.filter((r) => r.isDuplicate).length;
  const flagged = rows.filter((r) => r.warning).length;
  const learning = included.filter((r) => r.remember && isCorrected(r)).length;
  const newCategories = [...new Set(included.filter((r) => r.categoryId === null && r.newCategoryName).map((r) => r.newCategoryName!))];

  const commit = useMutation({
    mutationFn: () =>
      api.imports.commit(
        id,
        included.map((r) => ({
          date: r.date,
          amount: Number(r.amount),
          type: r.type,
          description: r.description,
          merchant: r.merchant,
          categoryId: r.categoryId,
          newCategoryName: r.categoryId === null ? r.newCategoryName : null,
          index: r.index,
          remember: r.remember,
        })),
        accountId,
      ),
    onSuccess: (r) => {
      invalidate();
      toast.show(`Saved ${r.transactionCount} transactions from ${fileName}`);
      navigate('/transactions');
    },
  });

  const discard = useMutation({
    mutationFn: () => api.imports.discard(id),
    onSuccess: () => {
      invalidate();
      navigate('/import');
    },
  });

  if (rows.length === 0) {
    return (
      <Empty title="No transactions found" action={<Button onClick={() => discard.mutate()}>Discard import</Button>}>
        The AI didn't find any transactions in {fileName}. If it does contain some, try a stronger model in Settings and import it again.
      </Empty>
    );
  }

  return (
    <>
      <header className="mb-6">
        <h1 className="font-display text-[2.4rem] leading-tight font-semibold tracking-[-0.02em]">Check {rows.length} transactions</h1>
        <p className="mt-1 text-ink-soft">
          From {fileName}. Fix anything that looks off, untick rows you don't want, then save.
          {duplicates > 0 && ` ${duplicates} look like ${duplicates === 1 ? 'a duplicate' : 'duplicates'} of transactions you already have and are unticked.`}
          {flagged > 0 &&
            (flagged === 1
              ? " 1 has an amount that isn't printed on the statement, so the AI probably misread it. It's unticked; check it against the statement."
              : ` ${flagged} have amounts that aren't printed on the statement, so the AI probably misread them. They're unticked; check them against the statement.`)}
        </p>
        {accounts.length > 0 && (
          <div className="mt-4 flex items-center gap-2">
            <label className="text-sm text-ink-soft" htmlFor="import-account">
              Statement from
            </label>
            <AccountSelect
              id="import-account"
              className="field !h-8 w-auto px-2 text-sm"
              accounts={accounts}
              value={accountId}
              onChange={setAccountId}
              emptyLabel="Choose an account (optional)"
            />
          </div>
        )}
      </header>

      <div className="overflow-x-auto rounded-lg border border-line bg-surface">
        <table className="w-full min-w-[56rem] text-[0.93rem]">
          <thead>
            <tr className="border-b border-line text-left text-[0.8rem] text-ink-soft">
              <th className="w-10 py-2.5 pl-4">
                <input
                  type="checkbox"
                  aria-label="Include all"
                  className="size-4 accent-[var(--pine)]"
                  checked={rows.every((r) => r.include)}
                  onChange={(e) => setRows((all) => all.map((r) => ({ ...r, include: e.target.checked })))}
                />
              </th>
              <th className="w-36 font-medium">Date</th>
              <th className="font-medium">Description</th>
              <th className="w-32 font-medium">Type</th>
              <th className="w-56 font-medium">Category</th>
              <th className="w-32 pr-4 text-right font-medium">Amount</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map((r) => (
              <tr key={r.index} className={clsx(!r.include && 'opacity-45')}>
                <td className="py-2 pl-4 align-top">
                  <input
                    type="checkbox"
                    aria-label={`Include ${r.description}`}
                    className="mt-2 size-4 accent-[var(--pine)]"
                    checked={r.include}
                    onChange={(e) => update(r.index, { include: e.target.checked })}
                  />
                </td>
                <td className="py-2 pr-2 align-top">
                  <input type="date" className="field !h-8 px-2 text-sm" value={r.date} onChange={(e) => update(r.index, { date: e.target.value })} />
                </td>
                <td className="py-2 pr-3 align-top">
                  <input
                    className="field !h-8 px-2 text-sm"
                    aria-label="Description"
                    value={r.merchant ?? r.description}
                    onChange={(e) => update(r.index, { merchant: e.target.value })}
                  />
                  <p className="mt-1 flex flex-wrap gap-x-2 truncate px-2 text-xs text-ink-faint">
                    {r.merchant && <span className="truncate">{r.description}</span>}
                    {r.isDuplicate && <span className="font-medium text-ochre">{r.duplicateOf ?? 'Possible duplicate'}</span>}
                    {r.warning && <span className="font-medium text-brick">{r.warning}</span>}
                    {r.matchedRule && !isCorrected(r) && (
                      <span className="inline-flex items-center gap-0.5 text-pine" title="Set by a rule you taught Spence. Change it here to add an exception.">
                        <Wand2 className="size-3" aria-hidden /> Your rule: {r.matchedRule}
                      </span>
                    )}
                    {isCorrected(r) && (
                      <button
                        type="button"
                        onClick={() => update(r.index, { remember: !r.remember })}
                        aria-pressed={r.remember}
                        title={r.remember ? 'Spence will do this for similar transactions next time. Click for just this once.' : 'Only this row changes. Click to remember it.'}
                        className={clsx('inline-flex items-center gap-0.5 rounded font-medium underline-offset-2 hover:underline', r.remember ? 'text-pine' : 'text-ink-soft')}
                      >
                        <Wand2 className="size-3" aria-hidden /> {r.remember ? 'Remember next time' : 'Just this once'}
                      </button>
                    )}
                  </p>
                </td>
                <td className="py-2 pr-2 align-top">
                  <select
                    className="field !h-8 px-2 text-sm"
                    aria-label="Type"
                    value={r.type}
                    onChange={(e) => update(r.index, { type: e.target.value as TransactionType })}
                  >
                    <option value="Expense">Expense</option>
                    <option value="Refund">Refund</option>
                    <option value="Income">Income</option>
                    <option value="Transfer">Transfer</option>
                  </select>
                </td>
                <td className="py-2 pr-2 align-top">
                  {r.type === 'Transfer' ? (
                    <p className="px-2 pt-1.5 text-sm text-ink-faint">Not counted as spending</p>
                  ) : (
                  <CategorySelect
                    aria-label="Category"
                    className="field !h-8 px-2 text-sm"
                    categories={categories}
                    type={r.type}
                    value={r.categoryId}
                    extraOption={r.suggestedNew ?? undefined}
                    extraSelected={r.newCategoryName !== null}
                    onChange={(categoryId) => update(r.index, { categoryId, newCategoryName: null })}
                    onPickNew={() => update(r.index, { categoryId: null, newCategoryName: r.suggestedNew })}
                  />
                  )}
                </td>
                <td className="py-2 pr-4 align-top">
                  <input
                    className="field num !h-8 px-2 text-right text-sm"
                    aria-label="Amount"
                    type="number"
                    step="any"
                    min="0.01"
                    value={r.amount}
                    onChange={(e) => update(r.index, { amount: e.target.value as unknown as number })}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="sticky bottom-16 z-10 mt-4 flex flex-wrap items-center gap-x-6 gap-y-3 rounded-lg border border-line bg-surface px-5 py-3 shadow-lg md:bottom-4">
        <p className="text-sm text-ink-soft">
          <span className="num font-medium text-ink">{included.length}</span> to save,{' '}
          <span className="num font-medium text-ink">{money(spent, currency)}</span> in spending
          {newCategories.length > 0 && <>, new {newCategories.length === 1 ? 'category' : 'categories'}: {newCategories.join(', ')}</>}
          {learning > 0 && <>, {learning} {learning === 1 ? 'correction' : 'corrections'} to remember</>}
        </p>
        <div className="ml-auto flex gap-2">
          <Button variant="ghost" onClick={() => discard.mutate()} busy={discard.isPending}>
            Discard
          </Button>
          <Button variant="primary" onClick={() => commit.mutate()} busy={commit.isPending} disabled={included.length === 0}>
            Save {included.length} transactions
          </Button>
        </div>
        <ErrorNote error={commit.error ?? discard.error} className="w-full" />
      </div>
    </>
  );
}
