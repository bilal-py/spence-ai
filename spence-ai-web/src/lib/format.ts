const moneyFormatters = new Map<string, Intl.NumberFormat>();

export function money(value: number, currency: string, opts: { compact?: boolean; sign?: boolean } = {}): string {
  const key = `${currency}|${opts.compact}|${opts.sign}`;
  let f = moneyFormatters.get(key);
  if (!f) {
    try {
      f = new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency,
        notation: opts.compact ? 'compact' : 'standard',
        maximumFractionDigits: opts.compact ? 1 : 2,
        signDisplay: opts.sign ? 'exceptZero' : 'auto',
      });
    } catch {
      f = new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 });
    }
    moneyFormatters.set(key, f);
  }
  return f.format(value);
}

/** Percent change from previous to current, or null when there's nothing to compare against. */
export function change(current: number, previous: number): number | null {
  if (!previous) return null;
  return (current - previous) / previous;
}

export function percent(value: number, digits = 0): string {
  return new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: digits }).format(value);
}

// ---- Dates (ISO yyyy-MM-dd strings, local calendar, no time zones) ----

export function toIso(d: Date): string {
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${d.getFullYear()}-${m}-${day}`;
}

export function fromIso(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function today(): string {
  return toIso(new Date());
}

export function shortDate(iso: string): string {
  return fromIso(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
}

export function longDate(iso: string): string {
  return fromIso(iso).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
}

// ---- Periods: "2026-11" (a month) or "2026" (a year) ----

export interface Period {
  key: string;
  kind: 'month' | 'year';
  from: string;
  to: string;
  label: string;
  year: number;
}

export function currentMonthKey(): string {
  return today().slice(0, 7);
}

export function parsePeriod(key: string | null): Period {
  const k = key && /^\d{4}(-\d{2})?$/.test(key) ? key : currentMonthKey();
  if (k.length === 4) {
    const year = Number(k);
    return { key: k, kind: 'year', from: `${k}-01-01`, to: `${k}-12-31`, label: k, year };
  }
  const [year, month] = k.split('-').map(Number);
  const last = new Date(year, month, 0).getDate();
  const label = new Date(year, month - 1, 1).toLocaleDateString(undefined, { month: 'long' });
  return { key: k, kind: 'month', from: `${k}-01`, to: `${k}-${String(last).padStart(2, '0')}`, label, year };
}

export function shiftPeriod(p: Period, delta: number): string {
  if (p.kind === 'year') return String(p.year + delta);
  const d = fromIso(p.from);
  d.setMonth(d.getMonth() + delta);
  return toIso(d).slice(0, 7);
}

export function monthLabel(key: string, style: 'short' | 'narrow' | 'long' = 'short'): string {
  const [y, m] = key.split('-').map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString(undefined, { month: style });
}

export const CURRENCIES = ['USD', 'EUR', 'GBP', 'INR', 'AED', 'SAR', 'MAD', 'TRY', 'PKR', 'CAD', 'AUD', 'JPY', 'SGD', 'CHF'];
