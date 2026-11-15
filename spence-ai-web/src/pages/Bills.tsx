import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Bell, BellOff, CalendarPlus, Flame, Pencil, Plus, Repeat, RotateCcw, Scissors, Sparkles, Trash2, TriangleAlert, X } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { AccountSelect } from '../components/AccountSelect';
import { DueSoonList, useMarkPaid } from '../components/BillParts';
import { CategorySelect } from '../components/CategorySelect';
import { Dialog } from '../components/Dialog';
import { SubscriptionCheck } from '../components/SubscriptionCheck';
import { useToast } from '../components/Toast';
import { Button, Empty, ErrorNote, PageHeader, Segmented, SectionTitle, Spinner } from '../components/ui';
import { api, type Bill, type BillCadence, type BillInput, type BillSuggestion, type BillsOverview, type CalendarItem } from '../lib/api';
import { cadenceLabel, dueLabel, perLabel, STATUS_TONE } from '../lib/bills';
import { celebrateFrom } from '../lib/celebrate';
import { fromIso, longDate, money, shortDate, today } from '../lib/format';
import { useAccounts, useBills, useCategories, useCurrency, useInvalidateMoney } from '../lib/hooks';
import { CategoryIcon } from '../lib/icons';
import { reminderPermission, remindersOn, setRemindersOn } from '../lib/reminders';

type DialogState = { kind: 'none' } | { kind: 'bill'; bill?: Bill; draft?: Partial<BillInput> } | { kind: 'pay'; item: CalendarItem } | { kind: 'review' };

export default function Bills() {
  const { data, error, isPending } = useBills();
  const [dialog, setDialog] = useState<DialogState>({ kind: 'none' });
  const [nonce, setNonce] = useState(0);
  const open = (d: DialogState) => {
    setNonce((n) => n + 1);
    setDialog(d);
  };
  const close = () => setDialog({ kind: 'none' });
  const currency = useCurrency();

  const active = data?.bills.filter((b) => b.cancelledOn === null) ?? [];
  const cancelled = data?.bills.filter((b) => b.cancelledOn !== null) ?? [];
  const subscriptions = active.filter((b) => b.isSubscription && !b.isPaused);

  return (
    <>
      <PageHeader title="Bills">
        <RemindersToggle />
        <a href={api.bills.calendarUrl()} download className="inline-flex h-9 items-center gap-1.5 rounded-md px-3 text-[0.93rem] font-medium text-ink-soft hover:bg-sunken hover:text-ink" title="Download a calendar file with every bill and a reminder. Open it in Google, Apple or Outlook calendar.">
          <CalendarPlus className="size-4" aria-hidden /> Add to calendar
        </a>
        <Button variant="primary" onClick={() => open({ kind: 'bill' })}>
          <Plus className="size-4" aria-hidden /> New bill
        </Button>
      </PageHeader>

      <ErrorNote error={error} />
      {isPending && <Spinner />}

      {data && (
        <div className="grid max-w-5xl gap-12">
          {data.bills.length === 0 && data.suggestions.length === 0 && (
            <Empty title="Never miss a payment" action={<Button variant="primary" onClick={() => open({ kind: 'bill' })}>Add your first bill</Button>}>
              Add rent, your phone, Netflix, an EMI or a SIP. Spence ticks each one off when the payment shows up in an import, reminds you before it's
              due, and tells you when a price goes up. You can also type <i>netflix 649 monthly on 5th</i> in quick add (press N).
            </Empty>
          )}

          {data.bills.length > 0 && <MonthSummary data={data} onReview={subscriptions.length > 0 ? () => open({ kind: 'review' }) : undefined} />}

          <Alerts bills={active} cancelled={cancelled} />

          {data.dueSoon.length > 0 && (
            <section>
              <SectionTitle aside={<span>Paid logs it as a transaction</span>}>Coming up</SectionTitle>
              <DueSoonList items={data.dueSoon} overview={data} />
            </section>
          )}

          {data.suggestions.length > 0 && <Suggestions items={data.suggestions} />}

          {active.length > 0 && (
            <section>
              <SectionTitle aside={<span className="num">{money(data.monthlyTotal, currency)} a month</span>}>All bills</SectionTitle>
              <ul className="divide-y divide-line">
                {active.map((b) => (
                  <BillRow key={b.id} bill={b} onEdit={() => open({ kind: 'bill', bill: b })} onPay={(item) => open({ kind: 'pay', item })} />
                ))}
              </ul>
            </section>
          )}

          {cancelled.length > 0 && <Cancelled bills={cancelled} total={data.cancelledYearly} />}
        </div>
      )}

      <Dialog open={dialog.kind === 'bill'} onClose={close} title={dialog.kind === 'bill' && dialog.bill ? `Edit ${dialog.bill.name}` : 'New bill'} wide>
        {dialog.kind === 'bill' && <BillForm key={nonce} bill={dialog.bill} draft={dialog.draft} onDone={close} />}
      </Dialog>
      <Dialog open={dialog.kind === 'pay'} onClose={close} title={dialog.kind === 'pay' ? `Paid ${dialog.item.name}` : ''}>
        {dialog.kind === 'pay' && <PayForm key={nonce} item={dialog.item} overview={data} onDone={close} />}
      </Dialog>
      <Dialog open={dialog.kind === 'review'} onClose={close} title="Subscription check">
        {dialog.kind === 'review' && <SubscriptionCheck key={nonce} bills={subscriptions} onDone={close} />}
      </Dialog>
    </>
  );
}

// ---------- This month at a glance + the month strip ----------

function MonthSummary({ data, onReview }: { data: BillsOverview; onReview?: () => void }) {
  const currency = useCurrency();
  const m = data.thisMonth;
  const done = m.count > 0 && m.paidCount === m.count;
  const day = today();
  const first = fromIso(day.slice(0, 7) + '-01');
  const daysInMonth = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate();
  const items = data.calendar.filter((c) => c.dueDate.startsWith(day.slice(0, 7)));
  const byDay = new Map<number, CalendarItem[]>();
  for (const c of items) {
    const d = Number(c.dueDate.slice(8));
    byDay.set(d, [...(byDay.get(d) ?? []), c]);
  }
  const todayNum = Number(day.slice(8));
  const monthName = first.toLocaleDateString(undefined, { month: 'long' });

  return (
    <section aria-label="This month">
      <div className="flex flex-wrap items-end justify-between gap-x-10 gap-y-4">
        <div>
          <p className="text-sm text-ink-soft">{monthName}</p>
          <p className="font-display text-[clamp(2.2rem,6vw,3.4rem)] leading-none font-bold tracking-[-0.03em]">
            {done ? (
              <span className="pop inline-block">All paid ✓</span>
            ) : (
              <>
                <span className="num">{m.paidCount}</span>
                <span className="text-ink-faint">/{m.count}</span> <span className="text-[0.5em] font-semibold tracking-normal text-ink-soft">bills paid</span>
              </>
            )}
          </p>
        </div>
        <dl className="grid grid-cols-2 gap-x-8 gap-y-1 text-sm sm:grid-cols-3">
          <Stat label="Still to pay" value={money(m.remaining, currency)} />
          <Stat label="Paid so far" value={money(m.paid, currency)} />
          {data.subscriptionsYearly > 0 && (
            <div>
              <dt className="text-ink-soft">Subscriptions</dt>
              <dd className="num font-medium">
                {money(data.subscriptionsYearly, currency)}/yr
                {onReview && (
                  <button onClick={onReview} className="ml-2 inline-flex items-center gap-0.5 text-[0.85rem] font-medium text-pine underline-offset-2 hover:underline">
                    <Sparkles className="size-3.5" aria-hidden /> Check
                  </button>
                )}
              </dd>
            </div>
          )}
        </dl>
      </div>

      {/* The month as a strip: one cell per day, a dot per bill. */}
      <div className="mt-6 grid gap-[3px]" style={{ gridTemplateColumns: `repeat(${daysInMonth}, minmax(0, 1fr))` }} role="list" aria-label={`${monthName} bill calendar`}>
        {Array.from({ length: daysInMonth }, (_, i) => i + 1).map((d) => {
          const dayItems = byDay.get(d) ?? [];
          const label = dayItems.length ? `${d} ${monthName}: ${dayItems.map((c) => `${c.name} ${money(c.amount, currency)} (${c.status})`).join(', ')}` : undefined;
          return (
            <div
              key={d}
              role="listitem"
              title={label}
              aria-label={label}
              className={clsx(
                'flex min-h-14 flex-col items-center gap-1 rounded-[4px] pt-1 pb-1.5',
                d === todayNum ? 'bg-ink text-paper' : d < todayNum ? 'bg-sunken/60 text-ink-faint' : 'bg-sunken text-ink-soft',
              )}
            >
              {/* On phones only a few day numbers fit; the dots still show every day. */}
              <span className={clsx('num text-[0.65rem] leading-none', !(d === 1 || d % 5 === 0 || d === todayNum) && 'hidden sm:inline')}>{d}</span>
              <div className="flex flex-col items-center gap-0.5">
                {dayItems.slice(0, 3).map((c) => (
                  <span key={c.billId} className={clsx('size-2 rounded-full ring-1 ring-paper/60', STATUS_TONE[c.status])} />
                ))}
              </div>
            </div>
          );
        })}
      </div>
      <p className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-ink-soft">
        {(['paid', 'due', 'late', 'upcoming'] as const).map((s) => (
          <span key={s} className="inline-flex items-center gap-1.5">
            <span className={clsx('size-2 rounded-full', STATUS_TONE[s])} /> {s === 'due' ? 'due soon' : s}
          </span>
        ))}
      </p>
    </section>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-ink-soft">{label}</dt>
      <dd className="num font-medium">{value}</dd>
    </div>
  );
}

// ---------- Alerts: price rises and charges after cancelling ----------

function Alerts({ bills, cancelled }: { bills: Bill[]; cancelled: Bill[] }) {
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  const accept = useMutation({
    mutationFn: (b: Bill) => api.bills.update(b.id, { ...toInput(b), amount: b.priceChange!.to }, today()),
    onSuccess: (b) => {
      invalidate();
      toast.show(`${b.name} is now ${money(b.amount, currency)}`);
    },
  });
  const rises = bills.filter((b) => b.priceChange);
  const zombies = cancelled.filter((b) => b.chargedAfterCancel);
  if (rises.length === 0 && zombies.length === 0) return null;

  return (
    <section className="grid gap-2" aria-label="Heads-up">
      {zombies.map((b) => (
        <p key={`z${b.id}`} className="flex flex-wrap items-center gap-2 rounded-lg border-l-[3px] border-brick bg-brick-soft px-3 py-2.5 text-sm">
          <TriangleAlert className="size-4 text-brick" aria-hidden />
          <span>
            <b>{b.name}</b> charged you on {shortDate(b.chargedAfterCancel!)}, after you cancelled it on {shortDate(b.cancelledOn!)}. Worth checking it's really
            cancelled.
          </span>
        </p>
      ))}
      {rises.map((b) => {
        const up = b.priceChange!.to > b.priceChange!.from;
        const pct = Math.round((Math.abs(b.priceChange!.to - b.priceChange!.from) / b.priceChange!.from) * 100);
        return (
          <div key={`p${b.id}`} className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border-l-[3px] border-ochre bg-ochre-soft/60 px-3 py-2.5 text-sm">
            <span className="flex-1">
              <b>{b.name}</b> went {up ? 'up' : 'down'} from <span className="num">{money(b.priceChange!.from, currency)}</span> to{' '}
              <span className="num font-semibold">{money(b.priceChange!.to, currency)}</span> ({up ? '+' : '−'}
              {pct}%) on {shortDate(b.priceChange!.since)}.
              {up && <> That's <span className="num">{money((b.priceChange!.to - b.priceChange!.from) * (b.yearlyCost / b.amount), currency)}</span> more a year.</>}
            </span>
            <Button size="sm" busy={accept.isPending && accept.variables?.id === b.id} onClick={() => accept.mutate(b)}>
              Update to {money(b.priceChange!.to, currency)}
            </Button>
          </div>
        );
      })}
    </section>
  );
}

// ---------- Suggestions from repeat charges ----------

function Suggestions({ items }: { items: BillSuggestion[] }) {
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  const track = useMutation({
    mutationFn: (v: { s: BillSuggestion; el: Element | null }) =>
      api.bills.create(
        {
          name: v.s.name,
          amount: v.s.amount,
          amountVaries: false,
          type: v.s.type,
          cadence: 'Monthly',
          interval: 1,
          firstDueDate: v.s.firstDue,
          endsOn: null,
          matchPattern: v.s.pattern,
          categoryId: v.s.categoryId,
          accountId: null,
          autopay: v.s.looksLikeSubscription,
          isSubscription: v.s.looksLikeSubscription,
          remindDaysBefore: 3,
          isPaused: false,
          notes: null,
        },
        today(),
      ),
    onSuccess: (bill, v) => {
      invalidate();
      celebrateFrom(v.el, { silent: true });
      toast.show(bill.onTimeStreak > 1 ? `Tracking ${bill.name}. ${bill.onTimeStreak} payments on time already.` : `Tracking ${bill.name}`);
    },
    onError: (e) => toast.show(e instanceof Error ? e.message : String(e), { tone: 'error' }),
  });
  const dismiss = useMutation({ mutationFn: (s: BillSuggestion) => api.bills.dismissSuggestion(s.pattern), onSuccess: invalidate });

  return (
    <section>
      <SectionTitle aside={<span>From your transactions</span>}>Spotted repeat charges</SectionTitle>
      <ul className="grid gap-2 sm:grid-cols-2">
        {items.map((s) => (
          <li key={s.pattern} className="rise-in flex items-center gap-3 rounded-lg border border-dashed border-line px-3 py-2.5">
            <Repeat className="size-4 shrink-0 text-ink-faint" aria-hidden />
            <div className="min-w-0 flex-1">
              <p className="truncate font-medium">{s.name}</p>
              <p className="text-xs text-ink-soft">
                <span className="num">{money(s.amount, currency)}</span> in {s.months} months · next around {shortDate(s.nextDue)}
              </p>
            </div>
            <Button size="sm" variant="primary" busy={track.isPending && track.variables?.s === s} onClick={(e) => track.mutate({ s, el: e.currentTarget })}>
              Track
            </Button>
            <button onClick={() => dismiss.mutate(s)} className="rounded p-1 text-ink-faint hover:text-ink" aria-label={`${s.name} isn't a bill`} title="Not a bill">
              <X className="size-4" />
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

// ---------- One bill ----------

function BillRow({ bill: b, onEdit, onPay }: { bill: Bill; onEdit: () => void; onPay: (item: CalendarItem) => void }) {
  const currency = useCurrency();
  const markPaid = useMarkPaid();
  const color = b.categoryColor ?? '#5b6bd1';
  const next = b.next;
  const history = [...b.recent].reverse().filter((o) => o.dueDate <= today());
  const asItem = (): CalendarItem | null =>
    next && { billId: b.id, name: b.name, dueDate: next.dueDate, amount: b.amount, status: next.status, color: b.categoryColor, icon: b.categoryIcon, autopay: b.autopay };

  return (
    <li className={clsx('grid grid-cols-[2.25rem_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 py-3', b.isPaused && 'opacity-60')}>
      <span className="grid size-9 place-items-center rounded-lg" style={{ background: `${color}22`, color }}>
        {b.categoryIcon ? <CategoryIcon name={b.categoryIcon} className="size-4" /> : <Repeat className="size-4" aria-hidden />}
      </span>
      <div className="min-w-0">
        <p className="flex flex-wrap items-center gap-x-2 font-medium">
          <span className="truncate">{b.name}</span>
          {b.onTimeStreak >= 3 && (
            <span className="inline-flex items-center gap-0.5 text-xs font-semibold text-ochre" title={`${b.onTimeStreak} payments on time in a row`}>
              <Flame className="size-3.5" aria-hidden /> {b.onTimeStreak}
            </span>
          )}
          {b.isPaused && <span className="text-xs text-ink-faint">paused</span>}
        </p>
        <p className="text-xs text-ink-soft">
          {cadenceLabel(b)}
          {b.autopay && ' · autopay'}
          {b.type === 'Transfer' && ' · transfer'}
          {b.paymentsLeft !== null && ` · ${b.paymentsLeft} left`}
          {next && (
            <>
              {' · '}
              <span className={clsx(next.status === 'late' && 'font-medium text-brick', next.status === 'due' && 'font-medium text-ochre')}>{dueLabel(next)}</span>
            </>
          )}
        </p>
        {history.length > 0 && (
          <div className="mt-1.5 flex gap-1" aria-label="Recent payments">
            {history.map((o) => (
              <span
                key={o.dueDate}
                className={clsx('h-1.5 w-4 rounded-full', STATUS_TONE[o.status])}
                title={`${longDate(o.dueDate)}: ${o.status}${o.paidAmount ? `, ${money(o.paidAmount, currency)}` : ''}`}
              />
            ))}
          </div>
        )}
      </div>
      <div className="flex items-center gap-1.5">
        <div className="text-right">
          <p className="num font-medium">
            {b.amountVaries && '~'}
            {money(b.amount, currency)}
          </p>
          <p className="text-xs text-ink-faint">{perLabel(b)}</p>
        </div>
        {next && next.status !== 'upcoming' && !b.isPaused && (
          <Button
            size="sm"
            variant="ghost"
            busy={markPaid.isPending}
            onClick={(e) => {
              const item = asItem()!;
              if (b.amountVaries) onPay(item);
              else markPaid.mutate({ item, el: e.currentTarget });
            }}
            title="Mark the next one paid"
          >
            Paid
          </Button>
        )}
        <Button size="sm" variant="ghost" onClick={onEdit} aria-label={`Edit ${b.name}`}>
          <Pencil className="size-4" aria-hidden />
        </Button>
      </div>
    </li>
  );
}

function Cancelled({ bills, total }: { bills: Bill[]; total: number }) {
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const restore = useMutation({ mutationFn: (b: Bill) => api.bills.review(b.id, 'restore', today()), onSuccess: invalidate });
  return (
    <section>
      <SectionTitle aside={<span className="num font-medium text-pine">{money(total, currency)} a year back in your pocket</span>}>Cut</SectionTitle>
      <ul className="grid gap-1.5 text-sm">
        {bills.map((b) => (
          <li key={b.id} className="flex items-center justify-between gap-3">
            <span className="flex items-center gap-2 text-ink-soft">
              <Scissors className="size-3.5" aria-hidden /> <span className="text-ink line-through decoration-ink-faint">{b.name}</span> since{' '}
              {shortDate(b.cancelledOn!)}
            </span>
            <span className="flex items-center gap-2">
              <span className="num text-ink-soft">{money(b.yearlyCost, currency)}/yr</span>
              <button onClick={() => restore.mutate(b)} className="rounded p-1 text-ink-faint hover:text-ink" title="Track it again" aria-label={`Track ${b.name} again`}>
                <RotateCcw className="size-3.5" />
              </button>
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

// ---------- Browser reminders ----------

function RemindersToggle() {
  const [on, setOn] = useState(remindersOn());
  const toast = useToast();
  if (reminderPermission() === 'unsupported') return null;

  const toggle = async () => {
    if (on) {
      setRemindersOn(false);
      setOn(false);
      return;
    }
    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
      toast.show('Notifications are blocked for this site. Allow them in the browser to get reminders.', { tone: 'error' });
      return;
    }
    setRemindersOn(true);
    setOn(true);
    toast.show("Reminders on. While Spence is open, you'll get a heads-up the day before a bill.");
  };

  return (
    <Button variant="ghost" onClick={toggle} aria-pressed={on} title={on ? 'Browser reminders are on' : 'Get a browser notification before bills are due'}>
      {on ? <Bell className="size-4 text-pine" aria-hidden /> : <BellOff className="size-4" aria-hidden />} Reminders {on ? 'on' : 'off'}
    </Button>
  );
}

// ---------- Forms ----------

function toInput(b: Bill): BillInput {
  return {
    name: b.name,
    amount: b.amount,
    amountVaries: b.amountVaries,
    type: b.type,
    cadence: b.cadence,
    interval: b.interval,
    firstDueDate: b.firstDueDate,
    endsOn: b.endsOn,
    matchPattern: b.matchPattern,
    categoryId: b.categoryId,
    accountId: b.accountId,
    autopay: b.autopay,
    isSubscription: b.isSubscription,
    remindDaysBefore: b.remindDaysBefore,
    isPaused: b.isPaused,
    notes: b.notes,
  };
}

function Field({ label, htmlFor, hint, children, className }: { label: string; htmlFor?: string; hint?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <div className={className}>
      <label className="label" htmlFor={htmlFor}>
        {label}
      </label>
      {children}
      {hint && <p className="mt-1 text-xs text-ink-faint">{hint}</p>}
    </div>
  );
}

function BillForm({ bill, draft, onDone }: { bill?: Bill; draft?: Partial<BillInput>; onDone: () => void }) {
  const currency = useCurrency();
  const { data: categories = [] } = useCategories();
  const { data: accounts = [] } = useAccounts();
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  const [form, setForm] = useState<BillInput>(
    bill
      ? toInput(bill)
      : {
          name: '',
          amount: 0,
          amountVaries: false,
          type: 'Expense',
          cadence: 'Monthly',
          interval: 1,
          firstDueDate: today(),
          endsOn: null,
          matchPattern: null,
          categoryId: categories.find((c) => c.name === 'Bills & utilities')?.id ?? null,
          accountId: null,
          autopay: false,
          isSubscription: false,
          remindDaysBefore: 3,
          isPaused: false,
          notes: null,
          ...draft,
        },
  );
  const set = (patch: Partial<BillInput>) => setForm((f) => ({ ...f, ...patch }));
  const day = today();

  const save = useMutation({
    mutationFn: () => (bill ? api.bills.update(bill.id, form, day) : api.bills.create(form, day)),
    onSuccess: (saved) => {
      invalidate();
      toast.show(bill ? 'Bill saved' : `Tracking ${saved.name}`);
      onDone();
    },
  });
  const remove = useMutation({
    mutationFn: () => api.bills.remove(bill!.id),
    onSuccess: () => {
      invalidate();
      toast.show(`Deleted ${bill!.name}`);
      onDone();
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <form onSubmit={submit} className="grid gap-5">
      <div className="grid gap-3 sm:grid-cols-[1fr_10rem]">
        <Field label="Name" htmlFor="bill-name">
          <input id="bill-name" className="field" required maxLength={60} autoFocus placeholder="e.g. Rent, Netflix, Car EMI" value={form.name} onChange={(e) => set({ name: e.target.value })} />
        </Field>
        <Field label={`Amount (${currency})`} htmlFor="bill-amount">
          <input id="bill-amount" className="field num" type="number" min="0.01" step="any" required value={form.amount || ''} onChange={(e) => set({ amount: Number(e.target.value) })} />
        </Field>
      </div>
      <label className="-mt-3 flex items-center gap-2 text-sm text-ink-soft">
        <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={form.amountVaries} onChange={(e) => set({ amountVaries: e.target.checked })} />
        The amount changes each time (electricity, a card bill). Use a typical amount.
      </label>

      <div className="grid gap-3 sm:grid-cols-[auto_1fr_1fr]">
        <Field label="Repeats">
          <div className="flex gap-1.5">
            <input aria-label="Every how many" className="field num w-16" type="number" min="1" max="52" step="1" required value={form.interval} onChange={(e) => set({ interval: Number(e.target.value) })} />
            <select aria-label="Period" className="field w-32" value={form.cadence} onChange={(e) => set({ cadence: e.target.value as BillCadence })}>
              <option value="Weekly">{form.interval === 1 ? 'week' : 'weeks'}</option>
              <option value="Monthly">{form.interval === 1 ? 'month' : 'months'}</option>
              <option value="Yearly">{form.interval === 1 ? 'year' : 'years'}</option>
            </select>
          </div>
        </Field>
        <Field label={bill ? 'First due date' : 'Next due'} htmlFor="bill-first" hint={bill ? 'Later dates follow from this one.' : undefined}>
          <input id="bill-first" className="field" type="date" required value={form.firstDueDate} onChange={(e) => set({ firstDueDate: e.target.value })} />
        </Field>
        <Field label="Last payment, optional" htmlFor="bill-end" hint="For EMIs and plans that end.">
          <input id="bill-end" className="field" type="date" min={form.firstDueDate} value={form.endsOn ?? ''} onChange={(e) => set({ endsOn: e.target.value || null })} />
        </Field>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Kind" hint={form.type === 'Transfer' ? "Card bills, SIPs and savings aren't spending, so they don't lower safe-to-spend." : undefined}>
          <Segmented<'Expense' | 'Transfer'>
            label="Kind"
            value={form.type}
            onChange={(type) => set({ type })}
            options={[
              { value: 'Expense', label: 'Spending' },
              { value: 'Transfer', label: 'Transfer' },
            ]}
          />
        </Field>
        {form.type === 'Expense' && (
          <Field label="Category">
            <CategorySelect categories={categories} value={form.categoryId} onChange={(categoryId) => set({ categoryId })} type="Expense" aria-label="Category" />
          </Field>
        )}
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <Field
          label="Matches transactions with"
          htmlFor="bill-pattern"
          hint="Words on the statement line. An import with these words near the due date ticks the bill off."
        >
          <input id="bill-pattern" className="field" maxLength={100} placeholder={form.name || 'e.g. Netflix'} value={form.matchPattern ?? ''} onChange={(e) => set({ matchPattern: e.target.value || null })} />
        </Field>
        <Field label="Remind me">
          <select className="field" aria-label="Remind me" value={form.remindDaysBefore} onChange={(e) => set({ remindDaysBefore: Number(e.target.value) })}>
            {[0, 1, 2, 3, 5, 7, 14].map((d) => (
              <option key={d} value={d}>
                {d === 0 ? 'On the day' : d === 1 ? 'A day before' : `${d} days before`}
              </option>
            ))}
          </select>
        </Field>
      </div>

      <div className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
        <label className="flex items-center gap-2">
          <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={form.autopay} onChange={(e) => set({ autopay: e.target.checked })} />
          Autopay
        </label>
        <label className="flex items-center gap-2">
          <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={form.isSubscription} onChange={(e) => set({ isSubscription: e.target.checked })} />
          It's a subscription
        </label>
        {bill && (
          <label className="flex items-center gap-2">
            <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={form.isPaused} onChange={(e) => set({ isPaused: e.target.checked })} />
            Paused
          </label>
        )}
      </div>

      {accounts.length > 0 && (
        <Field label="Paid from, optional">
          <AccountSelect accounts={accounts} value={form.accountId} onChange={(accountId) => set({ accountId })} aria-label="Paid from" />
        </Field>
      )}

      <ErrorNote error={save.error ?? remove.error} />

      <div className="flex items-center justify-between gap-2">
        {bill ? (
          <Button type="button" variant="danger" busy={remove.isPending} onClick={() => confirm(`Delete ${bill.name}? Your transactions stay.`) && remove.mutate()}>
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
            {bill ? 'Save changes' : 'Add bill'}
          </Button>
        </div>
      </div>
    </form>
  );
}

/** For bills whose amount varies: what was actually paid, and when. */
function PayForm({ item, overview, onDone }: { item: CalendarItem; overview?: BillsOverview; onDone: () => void }) {
  const currency = useCurrency();
  const [amount, setAmount] = useState(String(item.amount));
  const [date, setDate] = useState(today());
  const markPaid = useMarkPaid(overview);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    markPaid.mutate({ item, amount: Number(amount), paidOn: date }, { onSuccess: onDone });
  };

  return (
    <form onSubmit={submit} className="grid gap-4">
      <p className="text-sm text-ink-soft">Due {longDate(item.dueDate)}. Spence logs this as a transaction.</p>
      <div className="grid grid-cols-[1fr_10rem] gap-3">
        <Field label={`Amount paid (${currency})`} htmlFor="pay-amount">
          <input id="pay-amount" className="field num" type="number" min="0.01" step="any" required autoFocus value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
        <Field label="Paid on" htmlFor="pay-date">
          <input id="pay-date" className="field" type="date" required max={today()} value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
      </div>
      <ErrorNote error={markPaid.error} />
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" variant="primary" busy={markPaid.isPending}>
          Mark paid
        </Button>
      </div>
    </form>
  );
}
