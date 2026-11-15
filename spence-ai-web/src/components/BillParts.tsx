import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Check, Repeat, SkipForward } from 'lucide-react';
import { Link } from 'react-router';
import { api, type BillsOverview, type CalendarItem, type SafeToSpend } from '../lib/api';
import { dueLabel } from '../lib/bills';
import { celebrate, celebrateFrom } from '../lib/celebrate';
import { money, today } from '../lib/format';
import { useCurrency, useInvalidateMoney } from '../lib/hooks';
import { CategoryIcon } from '../lib/icons';
import { useToast } from './Toast';
import { Button } from './ui';

/** Marks a due date paid (logging the transaction), with confetti and an Undo. */
// eslint-disable-next-line react-refresh/only-export-components
export function useMarkPaid(overview?: BillsOverview) {
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  const currency = useCurrency();
  return useMutation({
    mutationFn: (v: { item: CalendarItem; amount?: number; paidOn?: string; el?: Element | null }) =>
      api.bills.markPaid(v.item.billId, v.item.dueDate, v.paidOn ?? today(), v.amount ?? null, today()),
    onSuccess: (_, v) => {
      invalidate();
      const month = today().slice(0, 7);
      const lastOfMonth = overview && v.item.dueDate.startsWith(month) && overview.thisMonth.count - overview.thisMonth.paidCount === 1;
      if (lastOfMonth) celebrate({ big: true, emoji: ['✨', '💸', '🎉'] });
      else celebrateFrom(v.el ?? null, { emoji: ['💸'] });
      toast.show(lastOfMonth ? 'Every bill this month: done. Big win.' : `${v.item.name} paid, ${money(v.amount ?? v.item.amount, currency)}`, {
        action: {
          label: 'Undo',
          run: () => api.bills.unmark(v.item.billId, v.item.dueDate, today()).then(invalidate),
        },
      });
    },
    onError: (e) => toast.show(e instanceof Error ? e.message : String(e), { tone: 'error' }),
  });
}

/** Bills due in the next week or recently missed, each with Paid and Skip. */
export function DueSoonList({ items, overview, compact }: { items: CalendarItem[]; overview?: BillsOverview; compact?: boolean }) {
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  const markPaid = useMarkPaid(overview);
  const skip = useMutation({
    mutationFn: (item: CalendarItem) => api.bills.skip(item.billId, item.dueDate, today()),
    onSuccess: (_, item) => {
      invalidate();
      toast.show(`Skipped ${item.name} this time`, { action: { label: 'Undo', run: () => api.bills.unmark(item.billId, item.dueDate, today()).then(invalidate) } });
    },
  });
  const day = today();

  return (
    <ul className="grid gap-2">
      {items.map((item) => (
        <DueRow
          key={`${item.billId}-${item.dueDate}`}
          item={item}
          currency={currency}
          compact={compact}
          label={dueLabel(item, day)}
          busy={markPaid.isPending && markPaid.variables?.item === item}
          onPaid={(el) => markPaid.mutate({ item, el })}
          onSkip={() => skip.mutate(item)}
        />
      ))}
    </ul>
  );
}

function DueRow({ item, currency, label, compact, busy, onPaid, onSkip }: {
  item: CalendarItem;
  currency: string;
  label: string;
  compact?: boolean;
  busy: boolean;
  onPaid: (el: Element | null) => void;
  onSkip: () => void;
}) {
  const late = item.status === 'late';
  return (
    <li
      className={clsx(
        'rise-in flex items-center gap-3 rounded-lg border px-3 py-2',
        late ? 'border-brick/50 bg-brick-soft/60' : item.status === 'due' ? 'border-ochre/60 bg-ochre-soft/50' : 'border-line',
      )}
    >
      <span className="grid size-8 shrink-0 place-items-center rounded-md" style={{ background: `${item.color ?? '#5b6bd1'}22`, color: item.color ?? '#5b6bd1' }}>
        {item.icon ? <CategoryIcon name={item.icon} className="size-4" /> : <Repeat className="size-4" aria-hidden />}
      </span>
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{item.name}</p>
        <p className={clsx('text-xs', late ? 'font-medium text-brick' : 'text-ink-soft')}>
          {label}
          {item.autopay && ' · autopay'}
        </p>
      </div>
      <span className="num shrink-0 font-medium">{money(item.amount, currency)}</span>
      <div className="flex shrink-0 gap-1">
        <Button size="sm" variant="primary" busy={busy} onClick={(e) => onPaid(e.currentTarget)} title="Mark paid: logs the payment as a transaction">
          <Check className="size-4" aria-hidden /> {compact ? '' : 'Paid'}
          {compact && <span className="sr-only">Mark {item.name} paid</span>}
        </Button>
        {!compact && (
          <Button size="sm" variant="ghost" onClick={onSkip} title="Not this time">
            <SkipForward className="size-4" aria-hidden />
            <span className="sr-only">Skip {item.name} this time</span>
          </Button>
        )}
      </div>
    </li>
  );
}

/** The one number: what's left to spend this month after spending so far and the bills still to come. */
export function SafeToSpendCard({ safe, className }: { safe: SafeToSpend; className?: string }) {
  const currency = useCurrency();
  const over = safe.amount < 0;
  const used = safe.baseline > 0 ? Math.min(1, (safe.spent + safe.billsDue) / safe.baseline) : 1;
  return (
    <div className={clsx('rounded-xl border border-line bg-surface p-4', className)}>
      <p className="text-sm text-ink-soft">Safe to spend this month</p>
      <p className={clsx('num font-display text-[2.1rem] leading-tight font-semibold tracking-[-0.02em]', over && 'text-brick')}>
        {over ? `−${money(-safe.amount, currency)}` : money(safe.amount, currency)}
      </p>
      <div className="mt-2 flex h-2 overflow-hidden rounded-full bg-sunken" aria-hidden>
        <div className="h-full bg-ink/70" style={{ width: `${Math.min(1, safe.spent / Math.max(1, safe.baseline)) * 100}%` }} />
        <div className="h-full bg-ochre" style={{ width: `${Math.max(0, used - Math.min(1, safe.spent / Math.max(1, safe.baseline))) * 100}%` }} />
      </div>
      <p className="mt-2 text-xs text-ink-soft">
        {over ? (
          <>Over by this much once the bills land. Next month is a fresh start.</>
        ) : (
          <>
            About <span className="num font-medium text-ink">{money(safe.perDay, currency)}</span> a day for {safe.daysLeft} {safe.daysLeft === 1 ? 'day' : 'days'}.
          </>
        )}{' '}
        {safe.baselineSource === 'budgets' ? (
          <>
            What's left in{' '}
            <Link to="/categories" className="underline-offset-2 hover:underline">
              your budgets
            </Link>{' '}
            (<span className="num">{money(safe.baseline, currency)}</span>) after <span className="num">{money(safe.spent, currency)}</span> spent and{' '}
            <span className="num">{money(safe.billsDue, currency)}</span> in bills to come.
          </>
        ) : (
          <>
            Your usual month (<span className="num">{money(safe.baseline, currency)}</span>) minus <span className="num">{money(safe.spent, currency)}</span> spent
            and <span className="num">{money(safe.billsDue, currency)}</span> in bills to come.
          </>
        )}
      </p>
    </div>
  );
}
