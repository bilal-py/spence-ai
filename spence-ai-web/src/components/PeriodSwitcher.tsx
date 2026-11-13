import { ChevronLeft, ChevronRight } from 'lucide-react';
import { currentMonthKey, shiftPeriod } from '../lib/format';
import { usePeriod } from '../lib/hooks';
import { Segmented } from './ui';

export function PeriodSwitcher() {
  const [period, setPeriod] = usePeriod();
  const isCurrent = period.kind === 'month' ? period.key === currentMonthKey() : period.year === new Date().getFullYear();
  const title = period.kind === 'month' ? `${period.label} ${period.year}` : period.label;

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Segmented
        label="Period length"
        value={period.kind}
        onChange={(kind) => setPeriod(kind === 'year' ? String(period.year) : period.year === new Date().getFullYear() ? currentMonthKey() : `${period.year}-12`)}
        options={[
          { value: 'month', label: 'Month' },
          { value: 'year', label: 'Year' },
        ]}
      />
      <div className="inline-flex items-center rounded-md border border-line bg-surface">
        <button aria-label="Previous period" className="grid size-8 place-items-center text-ink-soft hover:text-ink" onClick={() => setPeriod(shiftPeriod(period, -1))}>
          <ChevronLeft className="size-4" />
        </button>
        <span className="min-w-28 px-1 text-center text-[0.85rem] font-medium" aria-live="polite">
          {title}
        </span>
        <button
          aria-label="Next period"
          className="grid size-8 place-items-center text-ink-soft hover:text-ink disabled:opacity-30"
          disabled={isCurrent}
          onClick={() => setPeriod(shiftPeriod(period, 1))}
        >
          <ChevronRight className="size-4" />
        </button>
      </div>
    </div>
  );
}
