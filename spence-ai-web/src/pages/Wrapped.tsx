import { useQuery } from '@tanstack/react-query';
import { ChevronLeft, ChevronRight, Download, Eye, EyeOff, Images } from 'lucide-react';
import { useState } from 'react';
import { useSearchParams } from 'react-router';
import { Button, Empty, ErrorNote, PageHeader, Spinner } from '../components/ui';
import { api } from '../lib/api';
import { currentMonthKey, money, today } from '../lib/format';
import { keys, useCurrency } from '../lib/hooks';
import { buildCards, CARD_COLORS, saveAllPng, saveCardPng, shiftMonth, type WrappedCard } from '../lib/wrapped';

const HIDE_KEY = 'spence.wrapped.hideAmounts';

function readHide(): boolean {
  try {
    return localStorage.getItem(HIDE_KEY) === '1';
  } catch {
    return false;
  }
}

/** Monthly recap cards you can save as images. "Hide amounts" swaps every money figure for counts and shares, so it's safe to share. */
export default function Wrapped() {
  const [params, setParams] = useSearchParams();
  const thisMonth = currentMonthKey();
  const lastMonth = shiftMonth(thisMonth, -1);
  const m = /^\d{4}-\d{2}$/.test(params.get('m') ?? '') ? params.get('m')! : lastMonth;
  const currency = useCurrency();
  const [hide, setHide] = useState(readHide);
  const day = today();

  const { data, error, isPending } = useQuery({ queryKey: [...keys.wrapped, m, day], queryFn: () => api.wrapped(m, day) });

  const toggleHide = () => {
    setHide((h) => {
      try {
        localStorage.setItem(HIDE_KEY, h ? '0' : '1');
      } catch {
        /* per-browser convenience only */
      }
      return !h;
    });
  };

  const [y, mo] = m.split('-').map(Number);
  const title = new Date(y, mo - 1, 1).toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
  const cards = data ? buildCards(data, (n) => money(n, currency, { compact: n >= 100_000 }), hide) : [];
  const go = (key: string) => setParams({ m: key }, { replace: true });
  const file = (suffix: string) => `spence-wrapped-${m}${suffix}${hide ? '-private' : ''}.png`;

  return (
    <>
      <PageHeader title="Wrapped">
        <div className="flex items-center gap-1">
          <Button size="sm" variant="ghost" onClick={() => go(shiftMonth(m, -1))} aria-label="Previous month">
            <ChevronLeft className="size-4" />
          </Button>
          <span className="min-w-36 text-center font-medium">{title}</span>
          <Button size="sm" variant="ghost" onClick={() => go(shiftMonth(m, 1))} disabled={m >= thisMonth} aria-label="Next month">
            <ChevronRight className="size-4" />
          </Button>
        </div>
        <Button size="sm" onClick={toggleHide} aria-pressed={hide}>
          {hide ? <EyeOff className="size-4" aria-hidden /> : <Eye className="size-4" aria-hidden />} {hide ? 'Amounts hidden' : 'Hide amounts'}
        </Button>
        <Button size="sm" variant="primary" disabled={cards.length === 0} onClick={() => saveAllPng(cards, title, file(''))}>
          <Images className="size-4" aria-hidden /> Save all as PNG
        </Button>
      </PageHeader>

      {m === thisMonth && <p className="mb-6 text-sm text-ink-soft">{title} isn't over yet, so this is the story so far.</p>}

      <ErrorNote error={error} />
      {isPending && <Spinner />}
      {data && data.count === 0 && (
        <Empty title={`Nothing recorded for ${title}`}>Import that month's statement and its Wrapped will appear here.</Empty>
      )}

      {data && data.count > 0 && (
        <ul className="grid grid-cols-1 gap-5 sm:grid-cols-2 xl:grid-cols-3">
          {cards.map((card) => (
            <li key={card.id}>
              <Card card={card} title={title} onSave={() => saveCardPng(card, title, file(`-${card.id}`))} />
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

/** The on-page twin of the PNG (lib/wrapped.ts drawCard): same palette, same content. */
function Card({ card, title, onSave }: { card: WrappedCard; title: string; onSave: () => void }) {
  const C = CARD_COLORS;
  return (
    <article className="group relative flex aspect-[4/5] flex-col overflow-hidden rounded-2xl p-7" style={{ background: C.bg, color: C.ink }}>
      <div className="pointer-events-none absolute -top-24 -right-24 size-72 rounded-full opacity-[0.18]" style={{ background: card.accent }} aria-hidden />
      <span className="h-1.5 w-9 rounded-full" style={{ background: card.accent }} aria-hidden />
      <p className="mt-5 text-[0.95rem]" style={{ color: C.soft }}>
        {card.eyebrow}
      </p>
      <p className="font-display mt-4 text-[clamp(2rem,3.4vw,3rem)] leading-[1.02] font-bold tracking-[-0.03em] break-words">{card.big}</p>
      <p className="mt-3 text-[1.02rem] leading-snug" style={{ color: C.soft }}>
        {card.sub}
      </p>
      {card.bars && (
        <ul className="mt-6 grid gap-2.5 text-sm">
          {card.bars.map((b) => (
            <li key={b.label}>
              <div className="flex justify-between gap-2">
                <span>{b.label}</span>
                <span style={{ color: C.soft }}>{Math.round(b.share * 100)}%</span>
              </div>
              <div className="mt-1 h-1.5 rounded-full" style={{ background: 'rgba(255,255,255,0.08)' }}>
                <div className="h-full rounded-full" style={{ width: `${Math.max(2, b.share * 100)}%`, background: b.color }} />
              </div>
            </li>
          ))}
        </ul>
      )}
      <div className="mt-auto flex items-center justify-between pt-5 text-sm" style={{ color: C.faint }}>
        <span className="font-display text-base font-bold" style={{ color: C.ink }}>
          spence
        </span>
        <span>{title}</span>
      </div>
      <button
        onClick={onSave}
        className="absolute top-4 right-4 grid size-9 place-items-center rounded-md transition-opacity sm:opacity-0 sm:group-hover:opacity-100 sm:focus:opacity-100"
        style={{ background: 'rgba(255,255,255,0.1)', color: C.ink }}
        aria-label={`Save “${card.eyebrow}” as PNG`}
        title="Save as PNG"
      >
        <Download className="size-4" />
      </button>
    </article>
  );
}
