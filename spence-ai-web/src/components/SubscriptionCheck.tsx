import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Heart, Repeat, Scissors } from 'lucide-react';
import { useEffect, useRef, useState, type PointerEvent } from 'react';
import { api, type Bill } from '../lib/api';
import { perLabel } from '../lib/bills';
import { buzz, celebrate } from '../lib/celebrate';
import { money, shortDate, today } from '../lib/format';
import { useCurrency, useInvalidateMoney } from '../lib/hooks';
import { CategoryIcon } from '../lib/icons';
import { Button } from './ui';

const SWIPE = 110;

/**
 * One subscription at a time: swipe right (or →) to keep, left (or ←) to cut, or skip it.
 * Cutting marks it cancelled in Spence; the user still cancels with the provider.
 */
export function SubscriptionCheck({ bills, onDone }: { bills: Bill[]; onDone: () => void }) {
  const currency = useCurrency();
  const invalidate = useInvalidateMoney();
  const [queue] = useState(() => [...bills].sort((a, b) => b.yearlyCost - a.yearlyCost));
  const [index, setIndex] = useState(0);
  const [kept, setKept] = useState<Bill[]>([]);
  const [cut, setCut] = useState<Bill[]>([]);
  const [dx, setDx] = useState(0);
  const [leaving, setLeaving] = useState<'left' | 'right' | null>(null);
  const start = useRef<number | null>(null);
  const [dragging, setDragging] = useState(false);

  const review = useMutation({
    mutationFn: (v: { bill: Bill; decision: 'keep' | 'cancel' }) => api.bills.review(v.bill.id, v.decision, today()),
    onSuccess: invalidate,
  });

  const bill = queue[index];
  const finished = index >= queue.length;

  const decide = (decision: 'keep' | 'cancel' | 'skip') => {
    if (!bill || leaving) return;
    if (decision !== 'skip') review.mutate({ bill, decision });
    if (decision === 'keep') setKept((k) => [...k, bill]);
    if (decision === 'cancel') {
      setCut((c) => [...c, bill]);
      celebrate({ emoji: ['✂️', '💸'], origin: { x: 0.3, y: 0.5 } });
    } else buzz(10);
    setLeaving(decision === 'cancel' ? 'left' : 'right');
    setTimeout(() => {
      setLeaving(null);
      setDx(0);
      setIndex((i) => i + 1);
    }, 220);
  };

  // Keyboard: ← cut, → keep, ↓ not sure.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowLeft') decide('cancel');
      else if (e.key === 'ArrowRight') decide('keep');
      else if (e.key === 'ArrowDown') decide('skip');
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  const cutYearly = cut.reduce((s, b) => s + b.yearlyCost, 0);
  useEffect(() => {
    if (finished && cut.length > 0) celebrate({ big: true, emoji: ['💸', '✨'] });
  }, [finished, cut.length]);

  if (finished) {
    return (
      <div className="grid gap-4 text-center">
        <p className="pop font-display text-4xl font-bold">{cut.length > 0 ? 'Nice cuts.' : 'All reviewed.'}</p>
        <p className="text-ink-soft">
          You kept {kept.length} and cut {cut.length}.
          {cut.length > 0 && (
            <>
              {' '}
              That's <span className="num font-semibold text-pine">{money(cutYearly, currency)}</span> a year back. Remember to cancel{' '}
              {cut.length === 1 ? 'it' : 'them'} with the provider too.
            </>
          )}
        </p>
        <Button variant="primary" onClick={onDone} className="justify-self-center">
          Done
        </Button>
      </div>
    );
  }

  const onDown = (e: PointerEvent) => {
    start.current = e.clientX;
    setDragging(true);
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  };
  const onMove = (e: PointerEvent) => {
    if (start.current !== null) setDx(e.clientX - start.current);
  };
  const onUp = () => {
    start.current = null;
    setDragging(false);
    if (dx > SWIPE) decide('keep');
    else if (dx < -SWIPE) decide('cancel');
    else setDx(0);
  };

  const color = bill.categoryColor ?? '#5b6bd1';
  const lastPaid = bill.recent.find((o) => o.status === 'paid');
  const offset = leaving === 'left' ? -480 : leaving === 'right' ? 480 : dx;
  const lean = Math.max(-1, Math.min(1, dx / SWIPE));

  return (
    <div className="grid gap-5">
      <p className="text-center text-sm text-ink-soft">
        {index + 1} of {queue.length} · swipe or use ← →
      </p>
      <div className="relative h-64 touch-none select-none">
        <div
          onPointerDown={onDown}
          onPointerMove={onMove}
          onPointerUp={onUp}
          onPointerCancel={onUp}
          className={clsx(
            'absolute inset-0 flex cursor-grab flex-col items-center justify-center gap-2 rounded-2xl border-2 bg-paper p-6 text-center shadow-lg active:cursor-grabbing',
            !dragging && 'transition-transform duration-200',
          )}
          style={{
            transform: `translateX(${offset}px) rotate(${offset / 18}deg)`,
            borderColor: lean > 0.3 ? 'var(--pine)' : lean < -0.3 ? 'var(--brick)' : 'var(--line)',
          }}
        >
          <span className="grid size-12 place-items-center rounded-xl" style={{ background: `${color}22`, color }}>
            {bill.categoryIcon ? <CategoryIcon name={bill.categoryIcon} className="size-6" /> : <Repeat className="size-6" aria-hidden />}
          </span>
          <p className="font-display text-3xl font-bold tracking-[-0.02em]">{bill.name}</p>
          <p className="num text-ink-soft">
            {money(bill.amount, currency)} {perLabel(bill)}
          </p>
          <p className="num font-display text-xl font-semibold">= {money(bill.yearlyCost, currency)} a year</p>
          <p className="text-xs text-ink-faint">
            {lastPaid?.paidOn ? `Last charged ${shortDate(lastPaid.paidOn)}` : 'No charge seen yet'}
            {bill.lastReviewedOn && ` · last checked ${shortDate(bill.lastReviewedOn)}`}
          </p>
          {lean > 0.3 && <span className="absolute top-4 left-4 rotate-[-12deg] rounded border-2 border-pine px-2 font-display font-bold text-pine">KEEP</span>}
          {lean < -0.3 && <span className="absolute top-4 right-4 rotate-[12deg] rounded border-2 border-brick px-2 font-display font-bold text-brick">CUT</span>}
        </div>
      </div>
      <div className="flex justify-center gap-3">
        <Button variant="danger" onClick={() => decide('cancel')} aria-label={`Cut ${bill.name}`}>
          <Scissors className="size-4" aria-hidden /> Cut it
        </Button>
        <Button variant="ghost" onClick={() => decide('skip')}>
          Not sure
        </Button>
        <Button variant="primary" onClick={() => decide('keep')} aria-label={`Keep ${bill.name}`}>
          <Heart className="size-4" aria-hidden /> Keep
        </Button>
      </div>
    </div>
  );
}
