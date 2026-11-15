import type { Bill, BillCadence, OccurrenceStatus } from './api';
import { fromIso, shortDate, today } from './format';

export function daysFrom(todayIso: string, iso: string): number {
  return Math.round((fromIso(iso).getTime() - fromIso(todayIso).getTime()) / 86_400_000);
}

const UNIT: Record<BillCadence, [string, string]> = { Weekly: ['week', 'weeks'], Monthly: ['month', 'months'], Yearly: ['year', 'years'] };

export function cadenceLabel(b: Pick<Bill, 'cadence' | 'interval'>): string {
  if (b.interval === 1) return b.cadence === 'Weekly' ? 'Weekly' : b.cadence === 'Monthly' ? 'Monthly' : 'Yearly';
  if (b.cadence === 'Monthly' && b.interval === 3) return 'Quarterly';
  if (b.cadence === 'Weekly' && b.interval === 2) return 'Every 2 weeks';
  return `Every ${b.interval} ${UNIT[b.cadence][1]}`;
}

/** "a month", "a week", "every 3 months": for "₹649 a month". */
export function perLabel(b: Pick<Bill, 'cadence' | 'interval'>): string {
  return b.interval === 1 ? `a ${UNIT[b.cadence][0]}` : `every ${b.interval} ${UNIT[b.cadence][1]}`;
}

/** "today", "tomorrow", "in 4 days", "3 days late", "paid 5 Nov". */
export function dueLabel(item: { dueDate: string; status: OccurrenceStatus; paidOn?: string | null }, todayIso = today()): string {
  const d = daysFrom(todayIso, item.dueDate);
  switch (item.status) {
    case 'paid':
      return item.paidOn ? `paid ${shortDate(item.paidOn)}` : 'paid';
    case 'skipped':
      return 'skipped';
    case 'late':
      return d === -1 ? 'due yesterday' : `${-d} days late`;
    case 'unconfirmed':
      return `due ${shortDate(item.dueDate)}, not in your data yet`;
    default:
      if (d === 0) return 'due today';
      if (d === 1) return 'due tomorrow';
      if (d > 1 && d <= 10) return `in ${d} days`;
      return shortDate(item.dueDate);
  }
}

export const STATUS_TONE: Record<OccurrenceStatus, string> = {
  paid: 'bg-pine',
  skipped: 'bg-ink-faint/40',
  upcoming: 'bg-line',
  due: 'bg-ochre',
  late: 'bg-brick',
  unconfirmed: 'bg-ink-faint',
};
