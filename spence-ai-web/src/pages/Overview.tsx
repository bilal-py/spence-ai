import { useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ArrowDownRight, ArrowUpRight, RefreshCw, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { Bar, BarChart, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { PeriodSwitcher } from '../components/PeriodSwitcher';
import { useTransactionEditor } from '../components/TransactionEditor';
import { Button, Empty, ErrorNote, SectionTitle, Spinner } from '../components/ui';
import { api, type CategoryTotal, type Overview as OverviewData } from '../lib/api';
import { change, currentMonthKey, monthLabel, money, percent, shortDate, today } from '../lib/format';
import { keys, useCurrency, usePeriod } from '../lib/hooks';
import { CategoryIcon } from '../lib/icons';

export default function Overview() {
  const [period] = usePeriod();
  const currency = useCurrency();
  const editor = useTransactionEditor();
  const { data, error, isPending } = useQuery({
    queryKey: [...keys.overview, period.from, period.to],
    queryFn: () => api.analytics.overview(period.from, period.to),
  });

  return (
    <>
      <div className="mb-6 flex justify-end">
        <PeriodSwitcher />
      </div>

      {isPending ? (
        <Spinner />
      ) : error ? (
        <ErrorNote error={error} />
      ) : (
        <>
          <Hero data={data} title={period.label} year={period.kind === 'month' ? period.year : null} currency={currency} />
          {data.count === 0 ? (
            <Empty
              title={`Nothing recorded for ${period.kind === 'month' ? `${period.label} ${period.year}` : period.label}`}
              action={
                <div className="flex flex-wrap gap-2">
                  <Link to="/import" className="inline-flex h-9 items-center rounded-md bg-pine px-3.5 font-medium text-on-pine hover:bg-pine-strong">
                    Import a statement
                  </Link>
                  <Button onClick={() => editor.open()}>Add a transaction</Button>
                </div>
              }
            >
              Import a bank or card statement and Spence will sort it into categories, or add expenses one at a time.
            </Empty>
          ) : (
            <div className="grid grid-cols-1 gap-x-12 gap-y-12 lg:grid-cols-[minmax(0,1.65fr)_minmax(0,1fr)]">
              <div className="grid min-w-0 grid-cols-1 content-start gap-12">
                <DailyChart data={data} isYear={period.kind === 'year'} currency={currency} />
                <Categories rows={data.byCategory} spent={data.spent} currency={currency} periodKey={period.key} showBudget={period.kind === 'month'} />
                <TrendChart data={data} currency={currency} />
              </div>
              <aside className="grid min-w-0 grid-cols-1 content-start gap-12">
                <Insights from={period.from} to={period.to} />
                <Recurring data={data} currency={currency} />
                <Merchants data={data} currency={currency} />
                <Largest data={data} currency={currency} />
              </aside>
            </div>
          )}
        </>
      )}
    </>
  );
}

// ---------- Hero: month name + total + the category ribbon ----------

function Hero({ data, title, year, currency }: { data: OverviewData; title: string; year: number | null; currency: string }) {
  const [hovered, setHovered] = useState<number | null>(null);
  const delta = change(data.spent, data.previousSpent);
  const segments = data.byCategory.filter((c) => c.total > 0);

  return (
    <section className="mb-12" aria-label="Period summary">
      <div className="flex flex-wrap items-end justify-between gap-x-10 gap-y-4">
        <h1 className="font-display leading-[0.85] font-bold tracking-[-0.045em] text-[clamp(3.6rem,10vw,7.5rem)]">
          {title}
          {year && <span className="ml-3 align-top text-[0.28em] font-medium tracking-normal text-ink-faint">{year}</span>}
        </h1>

        <div className="pb-1 sm:text-right">
          <p className="num font-display text-[clamp(2rem,4.5vw,3rem)] leading-none font-semibold tracking-[-0.02em]">{money(data.spent, currency)}</p>
          <p className="mt-2 flex flex-wrap items-center gap-x-4 sm:justify-end gap-y-1 text-sm text-ink-soft">
            <span>spent</span>
            {delta !== null && (
              <span className={clsx('inline-flex items-center gap-0.5 font-medium', delta > 0 ? 'text-brick' : 'text-pine')}>
                {delta > 0 ? <ArrowUpRight className="size-3.5" aria-hidden /> : <ArrowDownRight className="size-3.5" aria-hidden />}
                {percent(Math.abs(delta))} vs previous
              </span>
            )}
            {data.income > 0 && (
              <span>
                <span className="num font-medium text-pine">{money(data.income, currency)}</span> in
              </span>
            )}
          </p>
        </div>
      </div>

      {segments.length > 0 && (
        <>
          <div
            className="ribbon-grow mt-8 flex h-7 w-full gap-[3px] overflow-hidden rounded-[5px]"
            role="img"
            aria-label={`Spending split: ${segments.map((s) => `${s.name} ${percent(s.total / data.spent)}`).join(', ')}`}
          >
            {segments.map((s, i) => (
              <div
                key={s.categoryId ?? 'none'}
                title={`${s.name}: ${money(s.total, currency)}`}
                onMouseEnter={() => setHovered(i)}
                onMouseLeave={() => setHovered(null)}
                className="h-full transition-opacity duration-150"
                style={{
                  flexGrow: s.total,
                  flexBasis: 0,
                  minWidth: 4,
                  background: s.color,
                  opacity: hovered === null || hovered === i ? 1 : 0.3,
                }}
              />
            ))}
          </div>
          <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5 text-sm">
            {segments.slice(0, 7).map((s, i) => (
              <li
                key={s.categoryId ?? 'none'}
                onMouseEnter={() => setHovered(i)}
                onMouseLeave={() => setHovered(null)}
                className={clsx('flex items-center gap-1.5 transition-opacity', hovered !== null && hovered !== i && 'opacity-40')}
              >
                <span className="size-2.5 rounded-[2px]" style={{ background: s.color }} aria-hidden />
                <span>{s.name}</span>
                <span className="num text-ink-faint">{percent(s.total / data.spent)}</span>
              </li>
            ))}
            {segments.length > 7 && <li className="text-ink-faint">+{segments.length - 7} more</li>}
          </ul>
        </>
      )}
    </section>
  );
}

// ---------- Charts ----------

const axisTick = { fill: 'var(--ink-faint)', fontSize: 12 };

function ChartTooltip({ active, payload, label, currency, labelFormat }: {
  active?: boolean;
  payload?: { name: string; value: number; color: string }[];
  label?: string;
  currency: string;
  labelFormat: (l: string) => string;
}) {
  if (!active || !payload?.length || !label) return null;
  return (
    <div className="rounded-md border border-line bg-surface px-3 py-2 text-sm shadow-md">
      <p className="mb-1 font-medium">{labelFormat(label)}</p>
      {payload.map((p) => (
        <p key={p.name} className="num flex items-center gap-2 text-ink-soft">
          <span className="size-2 rounded-full" style={{ background: p.color }} />
          {p.name} <span className="ml-auto text-ink">{money(p.value, currency)}</span>
        </p>
      ))}
    </div>
  );
}

function DailyChart({ data, isYear, currency }: { data: OverviewData; isYear: boolean; currency: string }) {
  const todayIso = today();
  // For a year view, daily bars are too thin; show months of that year instead.
  const points = isYear
    ? data.monthly.filter((m) => m.month.startsWith(data.from.slice(0, 4))).map((m) => ({ key: m.month, spent: m.spent }))
    : data.daily.map((d) => ({ key: d.date, spent: d.spent }));
  const highlight = isYear ? currentMonthKey() : todayIso;

  return (
    <section>
      <SectionTitle aside={<span className="num">{money(data.dailyAverage, currency)} a day on average</span>}>
        {isYear ? 'Month by month' : 'Day by day'}
      </SectionTitle>
      <div className="h-44">
        <ResponsiveContainer>
          <BarChart data={points} margin={{ top: 8, right: 0, left: 0, bottom: 0 }} barCategoryGap="18%">
            <XAxis
              dataKey="key"
              tickLine={false}
              axisLine={{ stroke: 'var(--line)' }}
              tick={axisTick}
              interval={isYear ? 0 : 'preserveStartEnd'}
              tickFormatter={(k: string) => (isYear ? monthLabel(k, 'narrow') : String(Number(k.slice(8))))}
            />
            <YAxis hide />
            <Tooltip
              cursor={{ fill: 'var(--sunken)' }}
              content={<ChartTooltip currency={currency} labelFormat={(l) => (isYear ? `${monthLabel(l)} ${l.slice(0, 4)}` : shortDate(l))} />}
            />
            <Bar dataKey="spent" name="Spent" radius={[3, 3, 0, 0]} fill="var(--ink-faint)" isAnimationActive={false}
              shape={(props: { x?: number; y?: number; width?: number; height?: number; payload?: { key: string } }) => (
                <rect
                  x={props.x}
                  y={props.y}
                  width={props.width}
                  height={props.height}
                  rx={2}
                  fill={props.payload?.key === highlight ? 'var(--pine)' : 'var(--ink-faint)'}
                  opacity={props.payload?.key === highlight ? 1 : 0.55}
                />
              )}
            />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </section>
  );
}

function TrendChart({ data, currency }: { data: OverviewData; currency: string }) {
  return (
    <section>
      <SectionTitle aside="Spending bars, income line">Last twelve months</SectionTitle>
      <div className="h-56">
        <ResponsiveContainer>
          <ComposedChart data={data.monthly} margin={{ top: 8, right: 4, left: 4, bottom: 0 }}>
            <XAxis dataKey="month" tickLine={false} axisLine={{ stroke: 'var(--line)' }} tick={axisTick} tickFormatter={(m: string) => monthLabel(m, 'narrow')} />
            <YAxis tickLine={false} axisLine={false} tick={axisTick} width={56} tickFormatter={(v: number) => money(v, currency, { compact: true })} />
            <Tooltip cursor={{ fill: 'var(--sunken)' }} content={<ChartTooltip currency={currency} labelFormat={(m) => `${monthLabel(m)} ${m.slice(0, 4)}`} />} />
            <Bar dataKey="spent" name="Spent" fill="var(--ink)" opacity={0.75} radius={[3, 3, 0, 0]} barSize={18} isAnimationActive={false} />
            <Line dataKey="income" name="Income" stroke="var(--pine)" strokeWidth={2} dot={{ r: 2.5, fill: 'var(--pine)' }} isAnimationActive={false} />
          </ComposedChart>
        </ResponsiveContainer>
      </div>
    </section>
  );
}

// ---------- Category breakdown with budgets ----------

function Categories({ rows, spent, currency, periodKey, showBudget }: {
  rows: CategoryTotal[];
  spent: number;
  currency: string;
  periodKey: string;
  showBudget: boolean;
}) {
  const max = Math.max(...rows.map((r) => Math.max(r.total, showBudget ? (r.monthlyBudget ?? 0) : 0)), 1);

  return (
    <section>
      <SectionTitle aside={<Link to="/categories" className="hover:text-ink">Set budgets</Link>}>Where it went</SectionTitle>
      <ul className="divide-y divide-line">
        {rows.map((r) => {
          const delta = change(r.total, r.previousTotal);
          const budget = showBudget ? r.monthlyBudget : null;
          const over = budget !== null && r.total > budget;
          return (
            <li key={r.categoryId ?? 'none'}>
              <Link
                to={`/transactions?p=${periodKey}&category=${r.categoryId ?? 0}`}
                className="grid grid-cols-[2rem_minmax(0,1fr)_auto] items-center gap-x-3 py-3 hover:bg-surface/60"
              >
                <span className="grid size-8 place-items-center rounded-md" style={{ background: `${r.color}22`, color: r.color }}>
                  <CategoryIcon name={r.icon} className="size-4" />
                </span>
                <div className="min-w-0">
                  <div className="flex items-baseline gap-2">
                    <span className="truncate font-medium">{r.name}</span>
                    <span className="num text-xs text-ink-faint">{percent(r.total / spent)}</span>
                  </div>
                  <div className="relative mt-1.5 h-1.5 rounded-full bg-sunken">
                    <div className="absolute inset-y-0 left-0 rounded-full" style={{ width: `${(r.total / max) * 100}%`, background: over ? 'var(--brick)' : r.color }} />
                    {budget !== null && (
                      <div className="absolute -inset-y-1 w-0.5 rounded bg-ink" style={{ left: `${(budget / max) * 100}%` }} title={`Budget ${money(budget, currency)}`} />
                    )}
                  </div>
                </div>
                <div className="text-right">
                  <p className="num font-medium">{money(r.total, currency)}</p>
                  <p className={clsx('num text-xs', over ? 'font-medium text-brick' : 'text-ink-faint')}>
                    {budget !== null
                      ? over
                        ? `${money(r.total - budget, currency)} over budget`
                        : `${money(budget - r.total, currency)} left`
                      : delta !== null
                        ? `${delta > 0 ? '+' : ''}${percent(delta)} vs previous`
                        : `${r.count} ${r.count === 1 ? 'payment' : 'payments'}`}
                  </p>
                </div>
              </Link>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

// ---------- AI notes ----------

function Insights({ from, to }: { from: string; to: string }) {
  const q = useQuery({
    queryKey: ['insights', from, to],
    queryFn: () => api.analytics.insights(from, to),
    enabled: false,
    staleTime: Infinity,
    retry: false,
  });

  return (
    <section aria-label="AI summary">
      <SectionTitle
        aside={
          q.data && (
            <button onClick={() => q.refetch()} className="inline-flex items-center gap-1 hover:text-ink" disabled={q.isFetching}>
              <RefreshCw className={clsx('size-3.5', q.isFetching && 'animate-spin')} aria-hidden /> Rewrite
            </button>
          )
        }
      >
        Spence's read
      </SectionTitle>

      {!q.data && !q.isFetching && (
        <div className="border-l-[3px] border-ochre bg-ochre-soft/50 py-4 pr-4 pl-4">
          <p className="text-ink-soft">Get a short read on this period: what changed, what's over budget, and where you could save.</p>
          <Button className="mt-3" onClick={() => q.refetch()}>
            <Sparkles className="size-4 text-ochre" aria-hidden /> Write my summary
          </Button>
          <ErrorNote error={q.error} className="mt-3" />
        </div>
      )}

      {q.isFetching && !q.data && <Spinner label="Reading your numbers" />}

      {q.data && (
        <div className={clsx(q.isFetching && 'opacity-50')}>
          <p className="font-display text-lg leading-snug font-medium">{q.data.headline}</p>
          <ul className="mt-4 grid gap-4">
            {q.data.items.map((item, i) => (
              <li
                key={i}
                className={clsx(
                  'border-l-[3px] pl-3',
                  item.tone === 'watch' ? 'border-ochre' : item.tone === 'good' ? 'border-pine' : 'border-line',
                )}
              >
                <p className="font-medium">{item.title}</p>
                <p className="mt-0.5 text-sm text-ink-soft">{item.detail}</p>
              </li>
            ))}
          </ul>
          <ErrorNote error={q.error} className="mt-3" />
        </div>
      )}
    </section>
  );
}

// ---------- Side lists ----------

function Recurring({ data, currency }: { data: OverviewData; currency: string }) {
  if (data.recurring.length === 0) return null;
  const monthlyTotal = data.recurring.reduce((sum, r) => sum + r.typicalAmount, 0);
  return (
    <section>
      <SectionTitle aside={<span className="num">{money(monthlyTotal, currency)} a month</span>}>Recurring charges</SectionTitle>
      <ul className="grid gap-2.5">
        {data.recurring.map((r) => (
          <li key={r.merchant} className="flex items-baseline justify-between gap-3">
            <div className="min-w-0">
              <p className="truncate font-medium">{r.merchant}</p>
              <p className="text-xs text-ink-faint">
                Seen in {r.months} months, last on {shortDate(r.lastDate)}
              </p>
            </div>
            <span className="num shrink-0">{money(r.typicalAmount, currency)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Merchants({ data, currency }: { data: OverviewData; currency: string }) {
  if (data.topMerchants.length === 0) return null;
  const top = data.topMerchants[0].total;
  return (
    <section>
      <SectionTitle>Top merchants</SectionTitle>
      <ul className="grid gap-2">
        {data.topMerchants.map((m) => (
          <li key={m.name} className="relative flex items-baseline justify-between gap-3 py-1 pl-2">
            <span className="absolute inset-y-0 left-0 rounded-sm bg-sunken" style={{ width: `${(m.total / top) * 100}%` }} aria-hidden />
            <span className="relative truncate">{m.name}</span>
            <span className="num relative shrink-0 pr-1 text-sm">{money(m.total, currency)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Largest({ data, currency }: { data: OverviewData; currency: string }) {
  if (data.largest.length === 0) return null;
  return (
    <section>
      <SectionTitle>Biggest purchases</SectionTitle>
      <ul className="grid gap-2.5">
        {data.largest.map((t) => (
          <li key={t.id} className="flex items-baseline justify-between gap-3">
            <div className="min-w-0">
              <p className="truncate">{t.description}</p>
              <p className="flex items-center gap-1.5 text-xs text-ink-faint">
                {t.color && <span className="size-2 rounded-full" style={{ background: t.color }} aria-hidden />}
                {t.category ?? 'Uncategorized'}, {shortDate(t.date)}
              </p>
            </div>
            <span className="num shrink-0 font-medium">{money(t.amount, currency)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
