import { useMutation, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ChevronDown, Minus, Pencil, Plus, Trash2, Trophy, X } from 'lucide-react';
import { useEffect, useRef, useState, type FormEvent, type ReactNode, type RefObject } from 'react';
import { CategorySelect } from '../components/CategorySelect';
import { SWATCHES } from '../components/CategoryForm';
import { Dialog } from '../components/Dialog';
import { useToast } from '../components/Toast';
import { Button, Empty, ErrorNote, PageHeader, Segmented, SectionTitle, Spinner } from '../components/ui';
import { api, type Challenge, type ChallengeInput, type ChallengeKind, type Pot, type PotInput, type PotSource } from '../lib/api';
import { celebrate, celebrateFrom } from '../lib/celebrate';
import { longDate, money, percent, shortDate, today, toIso } from '../lib/format';
import { keys, useCategories, useCurrency, useInvalidateMoney } from '../lib/hooks';
import { CategoryIcon } from '../lib/icons';

const POT_ICONS = ['piggy-bank', 'plane', 'home', 'car', 'gift', 'graduation-cap', 'heart-pulse', 'smartphone', 'baby', 'paw-print', 'shirt', 'sparkles'];
const ROUND_UP_STEPS = [5, 10, 20, 50, 100, 500, 1000];

const SOURCE_LABEL: Record<PotSource, string> = {
  Manual: 'Added by hand',
  Transfer: 'Linked transfer',
  RoundUp: 'Round-ups',
  Leftover: 'Leftover budget',
  NoSpend: 'No-spend days',
  Challenge: 'Challenge',
};

const PACE_TEXT = { ahead: 'Ahead of pace', 'on-track': 'On track', behind: 'Behind pace', done: 'Goal reached', overdue: 'Past the deadline' } as const;

type DialogState =
  | { kind: 'none' }
  | { kind: 'pot'; pot?: Pot }
  | { kind: 'money'; pot: Pot; direction: 'in' | 'out' }
  | { kind: 'challenge'; pot: Pot };

export default function Pots() {
  const day = today();
  const { data: pots, error, isPending } = useQuery({ queryKey: [...keys.pots, day], queryFn: () => api.pots.list(day) });
  const [dialog, setDialog] = useState<DialogState>({ kind: 'none' });
  const [nonce, setNonce] = useState(0);
  const open = (d: DialogState) => {
    setNonce((n) => n + 1);
    setDialog(d);
  };
  const close = () => setDialog({ kind: 'none' });

  const active = pots?.filter((p) => !p.isArchived) ?? [];
  const archived = pots?.filter((p) => p.isArchived) ?? [];

  return (
    <>
      <PageHeader title="Savings pots">
        <Button variant="primary" onClick={() => open({ kind: 'pot' })}>
          <Plus className="size-4" aria-hidden /> New pot
        </Button>
      </PageHeader>

      <ErrorNote error={error} />
      {isPending && <Spinner />}
      {pots?.length === 0 && (
        <Empty title="Save toward something" action={<Button variant="primary" onClick={() => open({ kind: 'pot' })}>Create your first pot</Button>}>
          A pot is a goal with a deadline. Spence shows the pace you need, and fills it from what you add by hand, linked transfers (like a recurring
          deposit), round-ups on your spending, leftover budget, no-spend days and small challenges.
        </Empty>
      )}

      <div className="grid max-w-4xl gap-10">
        {active.map((pot) => (
          <PotRow key={pot.id} pot={pot} onEdit={() => open({ kind: 'pot', pot })} onMoney={(direction) => open({ kind: 'money', pot, direction })} onChallenge={() => open({ kind: 'challenge', pot })} />
        ))}
        {archived.length > 0 && (
          <section>
            <SectionTitle>Archived</SectionTitle>
            <ul className="divide-y divide-line">
              {archived.map((p) => (
                <li key={p.id}>
                  <button onClick={() => open({ kind: 'pot', pot: p })} className="flex w-full items-center justify-between gap-3 py-2.5 text-left opacity-70 hover:opacity-100">
                    <span className="flex items-center gap-2">
                      <CategoryIcon name={p.icon} className="size-4" /> {p.name}
                    </span>
                    <PotAmount pot={p} />
                  </button>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>

      <Dialog open={dialog.kind === 'pot'} onClose={close} title={dialog.kind === 'pot' && dialog.pot ? `Edit ${dialog.pot.name}` : 'New savings pot'} wide>
        {dialog.kind === 'pot' && <PotForm key={nonce} pot={dialog.pot} onDone={close} />}
      </Dialog>
      <Dialog open={dialog.kind === 'money'} onClose={close} title={dialog.kind === 'money' ? (dialog.direction === 'in' ? `Add to ${dialog.pot.name}` : `Take out of ${dialog.pot.name}`) : ''}>
        {dialog.kind === 'money' && <MoneyForm key={nonce} pot={dialog.pot} direction={dialog.direction} onDone={close} />}
      </Dialog>
      <Dialog open={dialog.kind === 'challenge'} onClose={close} title="New challenge">
        {dialog.kind === 'challenge' && <ChallengeForm key={nonce} pot={dialog.pot} onDone={close} />}
      </Dialog>
    </>
  );
}

function PotAmount({ pot }: { pot: Pot }) {
  const currency = useCurrency();
  return (
    <span className="num text-sm text-ink-soft">
      <span className="font-semibold text-ink">{money(pot.saved, currency)}</span> of {money(pot.targetAmount, currency)}
    </span>
  );
}

const MILESTONES = [0.25, 0.5, 0.75];
const MILESTONE_TEXT: Record<number, string> = { 0.25: 'A quarter of the way', 0.5: 'Halfway there', 0.75: 'Three quarters done' };

/**
 * Celebrates when a pot crosses 25/50/75% (a small burst) or reaches its goal (a big one, a buzz and a shine on the bar),
 * whatever filled it: money added by hand, a claim, or a linked transfer that just got imported. Nothing fires on first load.
 */
function usePotMilestones(pot: Pot, bar: RefObject<HTMLDivElement | null>) {
  const toast = useToast();
  const previous = useRef<number | null>(null);
  useEffect(() => {
    const before = previous.current;
    previous.current = pot.progress;
    if (before === null || pot.progress <= before || pot.isArchived) return;

    const el = bar.current;
    const flash = (cls: string[]) => {
      if (!el) return;
      el.classList.remove(...cls);
      void el.offsetWidth; // restart the animation
      el.classList.add(...cls);
      setTimeout(() => el.classList.remove(...cls), 3400);
    };

    if (before < 1 && pot.progress >= 1) {
      celebrate({ big: true, emoji: ['🎉', '✨', '💰'] });
      flash(['pop', 'shine']);
      toast.show(`${pot.name} is fully funded. You did it!`);
      return;
    }
    const crossed = MILESTONES.filter((m) => before < m && pot.progress >= m).pop();
    if (crossed) {
      celebrateFrom(el, { emoji: ['🪙'] });
      flash(['shine']);
      toast.show(`${MILESTONE_TEXT[crossed]} on ${pot.name}.`);
    }
  }, [pot.progress, pot.name, pot.isArchived, bar, toast]);
}

function usePotMutation<T>(fn: (arg: T) => Promise<Pot>, success?: (pot: Pot, arg: T) => string) {
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  return useMutation({
    mutationFn: fn,
    onSuccess: (pot, arg) => {
      invalidate();
      if (success) toast.show(success(pot, arg));
    },
    onError: (e) => toast.show(e instanceof Error ? e.message : String(e), { tone: 'error' }),
  });
}

function PotRow({ pot, onEdit, onMoney, onChallenge }: { pot: Pot; onEdit: () => void; onMoney: (d: 'in' | 'out') => void; onChallenge: () => void }) {
  const currency = useCurrency();
  const [expanded, setExpanded] = useState(false);
  const day = today();
  const bar = useRef<HTMLDivElement>(null);
  usePotMilestones(pot, bar);
  const claim = usePotMutation(
    (v: { source: PotSource; el: Element | null }) => api.pots.claim(pot.id, v.source, day).then((p) => (celebrateFrom(v.el, { emoji: ['🪙'], silent: true }), p)),
    (_, v) => `Moved ${SOURCE_LABEL[v.source].toLowerCase()} into ${pot.name}`,
  );
  const claimChallenge = usePotMutation(
    (v: { c: Challenge; el: Element | null }) => api.pots.claimChallenge(pot.id, v.c.id, day).then((p) => (celebrateFrom(v.el, { emoji: ['🏆'] }), p)),
    (_, v) => `Challenge won! Moved the reward for “${v.c.title}” in`,
  );
  const removeChallenge = usePotMutation((c: Challenge) => api.pots.removeChallenge(pot.id, c.id, day));
  const removeEntry = usePotMutation((entryId: number) => api.pots.removeEntry(pot.id, entryId, day), () => 'Entry removed');

  const pace = pot.pace;
  const expectedShare = pace && pot.targetAmount > 0 ? Math.min(1, pace.expected / pot.targetAmount) : null;

  return (
    <section aria-label={pot.name}>
      <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-2">
        <div className="flex items-center gap-3">
          <span className="grid size-11 place-items-center rounded-lg" style={{ background: `${pot.color}22`, color: pot.color }}>
            <CategoryIcon name={pot.icon} className="size-5" />
          </span>
          <div>
            <h2 className="font-display text-2xl leading-tight font-semibold tracking-[-0.01em]">{pot.name}</h2>
            <p className="text-sm text-ink-soft">{pot.deadline ? `By ${longDate(pot.deadline)}` : 'No deadline'}</p>
          </div>
        </div>
        <p className="num text-right">
          <span className="font-display text-3xl font-semibold tracking-[-0.02em]">{money(pot.saved, currency)}</span>
          <span className="ml-1.5 text-ink-soft">of {money(pot.targetAmount, currency)}</span>
        </p>
      </div>

      <div ref={bar} className="relative mt-4 h-3 overflow-hidden rounded-full bg-sunken" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(pot.progress * 100)} aria-label={`${pot.name} progress`}>
        <div className="h-full rounded-full transition-[width] duration-500" style={{ width: `${pot.progress * 100}%`, background: pot.color }} />
        {expectedShare !== null && pace?.status !== 'done' && (
          <div className="absolute inset-y-0 w-0.5 bg-ink/60" style={{ left: `${expectedShare * 100}%` }} title={`Where you'd be on an even pace: ${money(pace!.expected, currency)}`} />
        )}
      </div>

      <div className="mt-2 flex flex-wrap items-center justify-between gap-x-6 gap-y-1 text-sm">
        <p className="text-ink-soft">
          <span className="num font-medium text-ink">{percent(pot.progress)}</span>
          {pace && (
            <>
              {' · '}
              <span className={clsx('font-medium', pace.status === 'behind' || pace.status === 'overdue' ? 'text-ochre' : 'text-pine')}>{PACE_TEXT[pace.status]}</span>
              {pace.status !== 'done' && pace.status !== 'overdue' && (
                <>
                  {' · '}
                  <span className="num">{money(pace.perMonth, currency)}</span> a month (<span className="num">{money(pace.perWeek, currency)}</span> a week) for the
                  next {pace.daysLeft} days
                </>
              )}
              {pace.status === 'overdue' && <> · <span className="num">{money(pace.remaining, currency)}</span> to go. Move the deadline or keep adding.</>}
            </>
          )}
        </p>
      </div>

      {pot.available.length > 0 && (
        <ul className="mt-4 flex flex-wrap gap-2">
          {pot.available.map((a) => (
            <li key={a.source} className="flex items-center gap-2 rounded-md border border-ochre/50 bg-ochre-soft py-1 pr-1 pl-3 text-sm">
              <span>
                <span className="num font-semibold">{money(a.amount, currency)}</span> {SOURCE_LABEL[a.source].toLowerCase()}
                <span className="ml-1 text-ink-soft">({a.detail})</span>
              </span>
              <Button size="sm" variant="primary" busy={claim.isPending && claim.variables?.source === a.source} onClick={(e) => claim.mutate({ source: a.source, el: e.currentTarget })}>
                Move in
              </Button>
            </li>
          ))}
        </ul>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        <Button size="sm" onClick={() => onMoney('in')}>
          <Plus className="size-4" aria-hidden /> Add money
        </Button>
        <Button size="sm" variant="ghost" onClick={() => onMoney('out')}>
          <Minus className="size-4" aria-hidden /> Take out
        </Button>
        <Button size="sm" variant="ghost" onClick={onChallenge}>
          <Trophy className="size-4" aria-hidden /> Challenge
        </Button>
        <Button size="sm" variant="ghost" onClick={onEdit}>
          <Pencil className="size-4" aria-hidden /> Edit
        </Button>
        <Button size="sm" variant="ghost" onClick={() => setExpanded((x) => !x)} aria-expanded={expanded}>
          <ChevronDown className={clsx('size-4 transition-transform', expanded && 'rotate-180')} aria-hidden /> History
        </Button>
      </div>

      {pot.challenges.some((c) => c.status !== 'claimed' && c.status !== 'lost') && (
        <ul className="mt-5 grid gap-2 sm:grid-cols-2">
          {pot.challenges
            .filter((c) => c.status === 'active' || c.status === 'won')
            .map((c) => (
              <ChallengeItem key={c.id} c={c} onClaim={(el) => claimChallenge.mutate({ c, el })} onRemove={() => removeChallenge.mutate(c)} claiming={claimChallenge.isPending} />
            ))}
        </ul>
      )}

      {expanded && (
        <div className="mt-5 grid gap-6 sm:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
          <div>
            <h3 className="label">Recent</h3>
            {pot.history.length === 0 ? (
              <p className="text-sm text-ink-faint">Nothing in yet.</p>
            ) : (
              <ul className="divide-y divide-line text-sm">
                {pot.history.map((e, i) => (
                  <li key={`${e.contributionId ?? 't' + e.transactionId}-${i}`} className="group flex items-center justify-between gap-3 py-2">
                    <span className="min-w-0">
                      <span className="text-ink-faint">{shortDate(e.date)}</span> <span className="font-medium">{SOURCE_LABEL[e.source]}</span>
                      {e.note && <span className="truncate text-ink-soft"> · {e.note}</span>}
                    </span>
                    <span className="flex items-center gap-1">
                      <span className={clsx('num', e.amount < 0 ? 'text-brick' : 'text-pine')}>{money(e.amount, currency, { sign: true })}</span>
                      {e.contributionId && (
                        <button
                          onClick={() => removeEntry.mutate(e.contributionId!)}
                          aria-label="Remove entry"
                          className="rounded p-1 text-ink-faint opacity-0 group-hover:opacity-100 hover:text-brick focus:opacity-100"
                        >
                          <X className="size-3.5" />
                        </button>
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <div>
            <h3 className="label">Where it came from</h3>
            <ul className="grid gap-1.5 text-sm">
              {(Object.entries(pot.bySource) as [PotSource, number][])
                .filter(([, v]) => v !== 0)
                .map(([source, total]) => (
                  <li key={source} className="flex justify-between gap-3">
                    <span className="text-ink-soft">{SOURCE_LABEL[source]}</span>
                    <span className="num">{money(total, currency)}</span>
                  </li>
                ))}
            </ul>
            <h3 className="label mt-5">Feeds</h3>
            <ul className="grid gap-1 text-sm text-ink-soft">
              {pot.linkPattern && <li>Transfers matching “{pot.linkPattern}”</li>}
              {pot.roundUpTo && <li>Round-ups to the nearest {pot.roundUpTo}</li>}
              {pot.collectLeftover && <li>Leftover budget each month</li>}
              {pot.noSpendBonus && <li>{money(pot.noSpendBonus, currency)} per no-spend day</li>}
              {!pot.linkPattern && !pot.roundUpTo && !pot.collectLeftover && !pot.noSpendBonus && <li>Only what you add by hand. Edit the pot to add more.</li>}
            </ul>
            {pot.challenges.some((c) => c.status === 'lost' || c.status === 'claimed') && (
              <>
                <h3 className="label mt-5">Past challenges</h3>
                <ul className="grid gap-1 text-sm text-ink-soft">
                  {pot.challenges
                    .filter((c) => c.status === 'lost' || c.status === 'claimed')
                    .map((c) => (
                      <li key={c.id} className="flex justify-between gap-3">
                        <span>{c.title}</span>
                        <span>{c.status === 'claimed' ? 'Won' : 'Not this time'}</span>
                      </li>
                    ))}
                </ul>
              </>
            )}
          </div>
        </div>
      )}
    </section>
  );
}

function ChallengeItem({ c, onClaim, onRemove, claiming }: { c: Challenge; onClaim: (el: Element) => void; onRemove: () => void; claiming: boolean }) {
  const currency = useCurrency();
  return (
    <li className={clsx('rounded-lg border px-3 py-2.5 text-sm', c.status === 'won' ? 'wiggle border-pine bg-pine-soft' : 'border-line')}>
      <div className="flex items-start justify-between gap-2">
        <p className="font-medium">
          <Trophy className="mr-1 inline size-3.5 align-[-2px] text-ochre" aria-hidden />
          {c.title}
        </p>
        <button onClick={onRemove} aria-label={`Remove challenge ${c.title}`} className="rounded p-0.5 text-ink-faint hover:text-brick">
          <X className="size-3.5" />
        </button>
      </div>
      <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-sunken">
        <div className="h-full rounded-full bg-ochre" style={{ width: `${(c.progress / Math.max(1, c.goal)) * 100}%` }} />
      </div>
      <div className="mt-1.5 flex items-center justify-between gap-2 text-ink-soft">
        <span>
          {c.progress} of {c.goal} {c.kind === 'SkipCategory' ? 'days clean' : 'no-spend days'} · reward <span className="num">{money(c.reward, currency)}</span>
        </span>
        {c.status === 'won' && (
          <Button size="sm" variant="primary" busy={claiming} onClick={(e) => onClaim(e.currentTarget)}>
            Move it in
          </Button>
        )}
      </div>
    </li>
  );
}

function Field({ label, htmlFor, hint, children }: { label: string; htmlFor?: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <div>
      <label className="label" htmlFor={htmlFor}>
        {label}
      </label>
      {children}
      {hint && <p className="mt-1 text-xs text-ink-faint">{hint}</p>}
    </div>
  );
}

function PotForm({ pot, onDone }: { pot?: Pot; onDone: () => void }) {
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const toast = useToast();
  const day = today();
  const [form, setForm] = useState<PotInput>({
    name: pot?.name ?? '',
    targetAmount: pot?.targetAmount ?? 0,
    startDate: pot?.startDate ?? day,
    deadline: pot?.deadline ?? null,
    color: pot?.color ?? SWATCHES[2],
    icon: pot?.icon ?? 'piggy-bank',
    linkPattern: pot?.linkPattern ?? null,
    roundUpTo: pot?.roundUpTo ?? null,
    collectLeftover: pot?.collectLeftover ?? false,
    noSpendBonus: pot?.noSpendBonus ?? null,
    isArchived: pot?.isArchived ?? false,
  });
  const set = (patch: Partial<PotInput>) => setForm((f) => ({ ...f, ...patch }));

  const save = useMutation({
    mutationFn: () => (pot ? api.pots.update(pot.id, form, day) : api.pots.create(form, day)),
    onSuccess: () => {
      invalidate();
      toast.show(pot ? 'Pot saved' : 'Pot created');
      onDone();
    },
  });
  const remove = useMutation({
    mutationFn: () => api.pots.remove(pot!.id),
    onSuccess: () => {
      invalidate();
      toast.show(`Deleted ${pot!.name}`);
      onDone();
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <form onSubmit={submit} className="grid gap-5">
      <div className="grid gap-3 sm:grid-cols-[1fr_11rem]">
        <Field label="Name" htmlFor="pot-name">
          <input id="pot-name" className="field" required maxLength={60} autoFocus placeholder="e.g. Goa trip" value={form.name} onChange={(e) => set({ name: e.target.value })} />
        </Field>
        <Field label={`Goal (${currency})`} htmlFor="pot-target">
          <input
            id="pot-target"
            className="field num"
            type="number"
            min="1"
            step="any"
            required
            value={form.targetAmount || ''}
            onChange={(e) => set({ targetAmount: Number(e.target.value) })}
          />
        </Field>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Counting from" htmlFor="pot-start" hint="Transfers, round-ups and no-spend days count from this date.">
          <input id="pot-start" className="field" type="date" required value={form.startDate ?? ''} onChange={(e) => set({ startDate: e.target.value })} />
        </Field>
        <Field label="Deadline, optional" htmlFor="pot-deadline" hint="Sets the pace you need.">
          <input id="pot-deadline" className="field" type="date" value={form.deadline ?? ''} onChange={(e) => set({ deadline: e.target.value || null })} />
        </Field>
      </div>

      <fieldset>
        <legend className="label">Look</legend>
        <div className="flex flex-wrap gap-1">
          {POT_ICONS.map((name) => (
            <button
              key={name}
              type="button"
              aria-label={name}
              aria-pressed={form.icon === name}
              onClick={() => set({ icon: name })}
              className={clsx('grid size-8 place-items-center rounded-md', form.icon === name ? 'bg-ink text-paper' : 'text-ink-soft hover:bg-sunken')}
            >
              <CategoryIcon name={name} className="size-4" />
            </button>
          ))}
        </div>
        <div className="mt-2 flex flex-wrap gap-2">
          {SWATCHES.map((c) => (
            <button
              key={c}
              type="button"
              aria-label={`Color ${c}`}
              aria-pressed={form.color === c}
              onClick={() => set({ color: c })}
              className={clsx('size-6 rounded-full ring-offset-2 ring-offset-surface', form.color === c && 'ring-2 ring-ink')}
              style={{ background: c }}
            />
          ))}
        </div>
      </fieldset>

      <fieldset className="grid gap-4 border-t border-line pt-4">
        <legend className="label pr-2">What fills it</legend>
        <Field label="Linked transfers" htmlFor="pot-link" hint="Transfers whose description contains this text count automatically, e.g. “RD”, “Groww” or your savings account’s name.">
          <input id="pot-link" className="field" maxLength={60} placeholder="Leave empty to skip" value={form.linkPattern ?? ''} onChange={(e) => set({ linkPattern: e.target.value || null })} />
        </Field>
        <Field label="Round-ups" hint="Rounds every purchase up and offers the spare change. Only one pot collects round-ups.">
          <select className="field" value={form.roundUpTo ?? ''} onChange={(e) => set({ roundUpTo: e.target.value ? Number(e.target.value) : null })} aria-label="Round up to">
            <option value="">Off</option>
            {ROUND_UP_STEPS.map((s) => (
              <option key={s} value={s}>
                To the nearest {s}
              </option>
            ))}
          </select>
        </Field>
        <Field label={`No-spend bonus (${currency}), optional`} htmlFor="pot-nospend" hint="Pay yourself this much for each day with no spending.">
          <input
            id="pot-nospend"
            className="field num"
            type="number"
            min="1"
            step="any"
            placeholder="Off"
            value={form.noSpendBonus ?? ''}
            onChange={(e) => set({ noSpendBonus: e.target.value === '' ? null : Number(e.target.value) })}
          />
        </Field>
        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" className="mt-0.5 size-4 accent-[var(--pine)]" checked={form.collectLeftover} onChange={(e) => set({ collectLeftover: e.target.checked })} />
          <span>
            Offer leftover budget after each month
            <span className="block text-xs text-ink-faint">What your category budgets didn’t use. Only one pot collects it.</span>
          </span>
        </label>
      </fieldset>

      {pot && (
        <label className="flex items-center gap-2 text-sm text-ink-soft">
          <input type="checkbox" className="size-4 accent-[var(--pine)]" checked={form.isArchived} onChange={(e) => set({ isArchived: e.target.checked })} />
          Archived: done with it, keep the history
        </label>
      )}

      <ErrorNote error={save.error ?? remove.error} />

      <div className="flex items-center justify-between gap-2">
        {pot ? (
          <Button type="button" variant="danger" busy={remove.isPending} onClick={() => confirm(`Delete ${pot.name} and its history? Your transactions aren't touched.`) && remove.mutate()}>
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
            {pot ? 'Save changes' : 'Create pot'}
          </Button>
        </div>
      </div>
    </form>
  );
}

function MoneyForm({ pot, direction, onDone }: { pot: Pot; direction: 'in' | 'out'; onDone: () => void }) {
  const currency = useCurrency();
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(today());
  const [note, setNote] = useState('');
  const save = usePotMutation(
    () => api.pots.contribute(pot.id, (direction === 'in' ? 1 : -1) * Number(amount), date, note.trim() || null),
    () => (direction === 'in' ? `Added to ${pot.name}` : `Took money out of ${pot.name}`),
  );

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate(undefined, { onSuccess: onDone });
  };

  return (
    <form onSubmit={submit} className="grid gap-4">
      <div className="grid grid-cols-[1fr_10rem] gap-3">
        <Field label={`Amount (${currency})`} htmlFor="money-amount">
          <input id="money-amount" className="field num" type="number" min="0.01" step="any" required autoFocus value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
        <Field label="Date" htmlFor="money-date">
          <input id="money-date" className="field" type="date" required max={today()} value={date} onChange={(e) => setDate(e.target.value)} />
        </Field>
      </div>
      <Field label="Note, optional" htmlFor="money-note">
        <input id="money-note" className="field" maxLength={200} placeholder={direction === 'in' ? 'e.g. Birthday money' : 'e.g. Flight tickets'} value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      <p className="text-xs text-ink-faint">This records the move in Spence. Move the actual money in your bank or app too.</p>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" variant="primary" busy={save.isPending}>
          {direction === 'in' ? 'Add' : 'Take out'}
        </Button>
      </div>
    </form>
  );
}

function ChallengeForm({ pot, onDone }: { pot: Pot; onDone: () => void }) {
  const currency = useCurrency();
  const { data: categories = [] } = useCategories();
  const start = today();
  const end = new Date();
  end.setDate(end.getDate() + 13);
  const [form, setForm] = useState<ChallengeInput>({
    kind: 'SkipCategory',
    categoryId: categories.find((c) => c.name === 'Dining')?.id ?? null,
    target: 5,
    startDate: start,
    endDate: toIso(end),
    reward: 0,
  });
  const set = (patch: Partial<ChallengeInput>) => setForm((f) => ({ ...f, ...patch }));
  const save = usePotMutation(() => api.pots.addChallenge(pot.id, form, today()), () => 'Challenge on. Good luck!');

  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate(undefined, { onSuccess: onDone });
  };

  return (
    <form onSubmit={submit} className="grid gap-4">
      <Segmented<ChallengeKind>
        label="Challenge"
        value={form.kind}
        onChange={(kind) => set({ kind })}
        options={[
          { value: 'SkipCategory', label: 'Skip a category' },
          { value: 'NoSpendDays', label: 'No-spend days' },
        ]}
      />
      {form.kind === 'SkipCategory' ? (
        <Field label="Skip" hint="Win by not spending in it until the end date.">
          <CategorySelect categories={categories} value={form.categoryId} onChange={(categoryId) => set({ categoryId })} type="Expense" allowCreate={false} />
        </Field>
      ) : (
        <Field label="No-spend days to reach" htmlFor="ch-target" hint="Days with no spending at all, between the dates.">
          <input id="ch-target" className="field num" type="number" min="1" step="1" required value={form.target ?? ''} onChange={(e) => set({ target: Number(e.target.value) })} />
        </Field>
      )}
      <div className="grid grid-cols-2 gap-3">
        <Field label="From" htmlFor="ch-start">
          <input id="ch-start" className="field" type="date" required value={form.startDate} onChange={(e) => set({ startDate: e.target.value })} />
        </Field>
        <Field label="Until" htmlFor="ch-end">
          <input id="ch-end" className="field" type="date" required min={form.startDate} value={form.endDate} onChange={(e) => set({ endDate: e.target.value })} />
        </Field>
      </div>
      <Field label={`Reward into ${pot.name} (${currency})`} htmlFor="ch-reward" hint="When you win, this is offered to move into the pot.">
        <input id="ch-reward" className="field num" type="number" min="1" step="any" required value={form.reward || ''} onChange={(e) => set({ reward: Number(e.target.value) })} />
      </Field>
      <ErrorNote error={save.error} />
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" variant="primary" busy={save.isPending}>
          Start challenge
        </Button>
      </div>
    </form>
  );
}
