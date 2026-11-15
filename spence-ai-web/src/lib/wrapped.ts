import type { Wrapped } from './api';
import { fromIso, monthLabel, percent } from './format';

/** One recap card. The same model draws the on-page card and the PNG, so they always match. */
export interface WrappedCard {
  id: string;
  eyebrow: string;
  big: string;
  sub: string;
  accent: string;
  bars?: { label: string; share: number; color: string }[];
}

/** Fixed share palette: the image must look the same whatever theme the app is in. */
export const CARD_COLORS = { bg: '#14201b', ink: '#f1f3ec', soft: '#a9b5ad', faint: '#5f6d65', pine: '#6fc2a0', ochre: '#e3b65a', brick: '#e57c62' };

const WEEKDAY_PLURAL: Record<string, string> = {
  Monday: 'Mondays', Tuesday: 'Tuesdays', Wednesday: 'Wednesdays', Thursday: 'Thursdays', Friday: 'Fridays', Saturday: 'Saturdays', Sunday: 'Sundays',
};

/**
 * Builds the cards for a month. With `hide`, no amount of money appears anywhere: cards fall back to counts, shares and names.
 * `fmt` formats money in the user's currency.
 */
export function buildCards(w: Wrapped, fmt: (n: number) => string, hide: boolean): WrappedCard[] {
  const month = monthLabel(w.month, 'long');
  const prevMonth = monthLabel(shiftMonth(w.month, -1), 'long');
  const cards: WrappedCard[] = [];
  const C = CARD_COLORS;

  const delta = w.previousSpent > 0 ? (w.spent - w.previousSpent) / w.previousSpent : null;
  const vs = delta === null ? '' : Math.abs(delta) < 0.02 ? ` About the same as ${prevMonth}.` : ` ${percent(Math.abs(delta))} ${delta < 0 ? 'less' : 'more'} than ${prevMonth}.`;
  cards.push({
    id: 'intro',
    eyebrow: `Your ${month} wrapped`,
    big: hide ? `${w.count} transactions` : fmt(w.spent),
    sub: (hide ? `${w.count} things logged this month.` : `spent across ${w.count} transactions.`) + vs,
    accent: C.pine,
  });

  if (w.categories.length > 0) {
    const top = w.categories[0];
    cards.push({
      id: 'top-category',
      eyebrow: 'Where most of it went',
      big: top.name,
      sub: `${percent(top.share)} of your spending${hide ? '' : `, ${fmt(top.total)}`}.`,
      accent: top.color,
      bars: w.categories.map((c) => ({ label: c.name, share: c.share, color: c.color })),
    });
  }

  if (w.topMerchant) {
    cards.push({
      id: 'top-merchant',
      eyebrow: 'Top place',
      big: w.topMerchant.name,
      sub: hide ? `${visits(w.topMerchant.visits)} this month.` : `${fmt(w.topMerchant.total)} over ${visits(w.topMerchant.visits)}.`,
      accent: C.ochre,
    });
  }

  if (w.regular && w.regular.name !== w.topMerchant?.name) {
    cards.push({ id: 'regular', eyebrow: 'Your regular', big: w.regular.name, sub: `You went ${w.regular.visits} times.`, accent: C.pine });
  }

  if (w.daysCovered > 0) {
    cards.push({
      id: 'no-spend',
      eyebrow: 'No-spend days',
      big: w.noSpendDays === 1 ? '1 day' : `${w.noSpendDays} days`,
      sub: w.longestNoSpendStreak >= 2 ? `Longest streak: ${w.longestNoSpendStreak} days in a row.` : 'Days without spending anything.',
      accent: C.pine,
    });
  }

  if (w.biggestDay) {
    const d = fromIso(w.biggestDay.date).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' });
    cards.push({
      id: 'biggest-day',
      eyebrow: 'Biggest day',
      big: d,
      sub: hide ? `${purchases(w.biggestDay.count)} in one day.` : `${fmt(w.biggestDay.total)} across ${purchases(w.biggestDay.count)}.`,
      accent: C.brick,
    });
  }

  if (w.largest) {
    cards.push({
      id: 'largest',
      eyebrow: 'Biggest single purchase',
      big: w.largest.description,
      sub: hide ? `${w.largest.category ?? 'Uncategorized'}.` : `${fmt(w.largest.amount)}${w.largest.category ? ` · ${w.largest.category}` : ''}.`,
      accent: C.ochre,
    });
  }

  if (w.busiestWeekday && w.busiestWeekdayShare > 0) {
    cards.push({
      id: 'weekday',
      eyebrow: 'Busiest day of the week',
      big: WEEKDAY_PLURAL[w.busiestWeekday] ?? w.busiestWeekday,
      sub: `${percent(w.busiestWeekdayShare)} of the month's spending.`,
      accent: C.pine,
    });
  }

  if (w.newMerchants > 0) {
    cards.push({
      id: 'new',
      eyebrow: 'New places',
      big: `${w.newMerchants} new`,
      sub: `First time at ${w.newMerchantNames.join(', ')}${w.newMerchants > w.newMerchantNames.length ? ' and more' : ''}.`,
      accent: C.ochre,
    });
  }

  if (w.budgetsTotal > 0) {
    cards.push({
      id: 'budgets',
      eyebrow: 'Budgets',
      big: `${w.budgetsKept} of ${w.budgetsTotal} kept`,
      sub: w.budgetsKept === w.budgetsTotal ? 'Every single one. Lovely.' : 'Each month is a fresh start.',
      accent: w.budgetsKept === w.budgetsTotal ? C.pine : C.ochre,
    });
  }

  if (w.savingsRate !== null || w.savedToPots > 0) {
    const kept = w.savingsRate !== null && w.savingsRate > 0;
    cards.push({
      id: 'saved',
      eyebrow: 'Saved',
      big: kept ? `${percent(w.savingsRate!)} kept` : w.savedToPots > 0 && !hide ? fmt(w.savedToPots) : 'Into pots',
      sub: [
        kept ? 'of what came in this month.' : w.savingsRate !== null ? 'Spending matched what came in this month.' : '',
        w.savedToPots > 0 ? (hide ? 'Money went into your savings pots.' : `${fmt(w.savedToPots)} went into your pots.`) : '',
      ]
        .filter(Boolean)
        .join(' '),
      accent: C.pine,
    });
  }

  return cards;
}

function visits(n: number) {
  return n === 1 ? '1 visit' : `${n} visits`;
}

function purchases(n: number) {
  return n === 1 ? '1 purchase' : `${n} purchases`;
}

export function shiftMonth(key: string, delta: number): string {
  const [y, m] = key.split('-').map(Number);
  const d = new Date(y, m - 1 + delta, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

// ---------- PNG export (plain canvas, no libraries) ----------

const W = 1080;
const H = 1350;
const DISPLAY = '"Bricolage Grotesque Variable", "Instrument Sans Variable", system-ui, sans-serif';
const SANS = '"Instrument Sans Variable", system-ui, sans-serif';

async function fontsReady() {
  try {
    await Promise.all([document.fonts.load(`700 100px ${DISPLAY}`), document.fonts.load(`500 40px ${SANS}`)]);
  } catch {
    /* fall back to system fonts */
  }
}

function wrap(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const lines: string[] = [];
  let line = '';
  for (const word of text.split(/\s+/)) {
    const next = line ? `${line} ${word}` : word;
    if (ctx.measureText(next).width > maxWidth && line) {
      lines.push(line);
      line = word;
    } else line = next;
  }
  if (line) lines.push(line);
  return lines;
}

/** Draws one card at 1080×1350 (a 4:5 portrait that social apps don't crop). */
export function drawCard(ctx: CanvasRenderingContext2D, card: WrappedCard, monthTitle: string) {
  const C = CARD_COLORS;
  ctx.fillStyle = C.bg;
  ctx.fillRect(0, 0, W, H);

  // Accent shapes: a big soft disc and a bar, so every card has its own colour without clutter.
  ctx.globalAlpha = 0.18;
  ctx.fillStyle = card.accent;
  ctx.beginPath();
  ctx.arc(W - 120, 170, 330, 0, Math.PI * 2);
  ctx.fill();
  ctx.globalAlpha = 1;
  ctx.fillRect(96, 140, 72, 12);

  ctx.textBaseline = 'alphabetic';
  ctx.fillStyle = C.soft;
  ctx.font = `500 40px ${SANS}`;
  ctx.fillText(card.eyebrow, 96, 236);

  // The big line shrinks until it fits in three lines.
  let size = 150;
  let lines: string[] = [];
  for (; size >= 64; size -= 8) {
    ctx.font = `700 ${size}px ${DISPLAY}`;
    lines = wrap(ctx, card.big, W - 192);
    if (lines.length <= 3 && lines.every((l) => ctx.measureText(l).width <= W - 192)) break;
  }
  ctx.fillStyle = C.ink;
  let y = 420 + size * 0.2;
  for (const l of lines.slice(0, 3)) {
    ctx.fillText(l, 96, y);
    y += size * 1.02;
  }

  ctx.font = `500 46px ${SANS}`;
  ctx.fillStyle = C.soft;
  y += 30;
  for (const l of wrap(ctx, card.sub, W - 192).slice(0, 4)) {
    ctx.fillText(l, 96, y);
    y += 62;
  }

  if (card.bars?.length) {
    y = Math.max(y + 40, 860);
    for (const b of card.bars.slice(0, 5)) {
      ctx.fillStyle = C.ink;
      ctx.font = `500 34px ${SANS}`;
      ctx.fillText(b.label, 96, y);
      ctx.fillStyle = C.soft;
      const pct = percent(b.share);
      ctx.fillText(pct, W - 96 - ctx.measureText(pct).width, y);
      ctx.fillStyle = 'rgba(255,255,255,0.08)';
      roundRect(ctx, 96, y + 14, W - 192, 14, 7);
      ctx.fillStyle = b.color;
      roundRect(ctx, 96, y + 14, Math.max(14, (W - 192) * b.share), 14, 7);
      y += 84;
    }
  }

  // Footer: brand + month.
  ctx.fillStyle = C.pine;
  roundRect(ctx, 96, H - 150, 52, 52, 12);
  ctx.fillStyle = C.ochre;
  roundRect(ctx, 104, H - 122, 14, 14, 3);
  ctx.fillStyle = C.ink;
  ctx.font = `700 46px ${DISPLAY}`;
  ctx.fillText('spence', 166, H - 109);
  ctx.fillStyle = C.faint;
  ctx.font = `500 34px ${SANS}`;
  ctx.fillText(monthTitle, W - 96 - ctx.measureText(monthTitle).width, H - 112);
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, r);
  ctx.fill();
}

function download(canvas: HTMLCanvasElement, name: string) {
  canvas.toBlob((blob) => {
    if (!blob) return;
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }, 'image/png');
}

export async function saveCardPng(card: WrappedCard, monthTitle: string, fileName: string) {
  await fontsReady();
  const canvas = document.createElement('canvas');
  canvas.width = W;
  canvas.height = H;
  drawCard(canvas.getContext('2d')!, card, monthTitle);
  download(canvas, fileName);
}

/** All cards in one image: a two-column grid of half-size cards. */
export async function saveAllPng(cards: WrappedCard[], monthTitle: string, fileName: string) {
  await fontsReady();
  const cols = 2;
  const gap = 24;
  const rows = Math.ceil(cards.length / cols);
  const cw = W / 2;
  const ch = H / 2;
  const canvas = document.createElement('canvas');
  canvas.width = cols * cw + (cols + 1) * gap;
  canvas.height = rows * ch + (rows + 1) * gap;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = '#0b120f';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  cards.forEach((card, i) => {
    ctx.save();
    ctx.translate(gap + (i % cols) * (cw + gap), gap + Math.floor(i / cols) * (ch + gap));
    ctx.scale(0.5, 0.5);
    ctx.beginPath();
    ctx.roundRect(0, 0, W, H, 40);
    ctx.clip();
    drawCard(ctx, card, monthTitle);
    ctx.restore();
  });
  download(canvas, fileName);
}
