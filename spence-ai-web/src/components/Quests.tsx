import { clsx } from 'clsx';
import { CalendarCheck, Check, Feather, Flame, Leaf, Receipt } from 'lucide-react';
import { useEffect } from 'react';
import type { PetAccessory, Streak } from '../lib/api';
import { celebrate } from '../lib/celebrate';
import { money, shortDate } from '../lib/format';
import { useCurrency, useQuests } from '../lib/hooks';
import { useToast } from './Toast';
import { SectionTitle } from './ui';

const STREAK_ICON = { 'check-in': CalendarCheck, 'no-spend': Leaf, 'light-weeks': Feather, bills: Receipt } as const;
const STREAK_WORDS = { 'check-in': 'checking in', 'no-spend': 'no-spend', 'light-weeks': 'under your usual', bills: 'paid on time' } as const;

const OUTFIT_LABEL: Record<PetAccessory, string> = { flower: 'a flower', bowtie: 'a bow tie', shades: 'shades', crown: 'a crown' };

const LEVEL_KEY = 'spence:level';

function readLevel(): number | null {
  try {
    const v = localStorage.getItem(LEVEL_KEY);
    return v ? Number(v) : null;
  } catch {
    return null;
  }
}

function writeLevel(n: number) {
  try {
    localStorage.setItem(LEVEL_KEY, String(n));
  } catch {
    /* fine */
  }
}

/** Streak chips, this week's three side quests and the XP level. Celebrates a finished quest or a new level once. */
export function QuestsPanel() {
  const { data } = useQuests();
  const toast = useToast();
  const currency = useCurrency();

  useEffect(() => {
    if (!data) return;
    const before = readLevel();
    writeLevel(data.level.number);
    if (before !== null && data.level.number > before) {
      const outfit = data.unlocked[data.unlocked.length - 1];
      celebrate({ big: true, emoji: ['🌱', '✨', '🎉'] });
      toast.show(`Level ${data.level.number}: ${data.level.title}!${outfit ? ` Your pet unlocked ${OUTFIT_LABEL[outfit]}.` : ''}`);
      return;
    }
    if (data.justCompleted.length > 0) {
      celebrate({ emoji: ['⭐'] });
      const q = data.justCompleted[0];
      toast.show(data.justCompleted.length === 1 ? `Quest done: ${q.title}. +${q.xp} XP` : `${data.justCompleted.length} quests done. +${data.justCompleted.reduce((s, x) => s + x.xp, 0)} XP`);
    }
  }, [data, toast]);

  if (!data) return null;
  const level = data.level;
  const span = Math.max(1, level.nextLevelXp - level.levelStartXp);
  const into = Math.min(span, level.xp - level.levelStartXp);
  const streaks = data.streaks.filter((s) => s.current > 0 || s.best > 1);

  return (
    <section aria-label="Streaks and side quests">
      <SectionTitle aside={<span>Resets Monday</span>}>Side quests</SectionTitle>

      <div className="mb-4 flex items-center gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-full bg-pine font-display text-lg font-bold text-on-pine" title={`Level ${level.number}`}>
          {level.number}
        </span>
        <div className="min-w-0 flex-1">
          <p className="flex justify-between gap-2 text-sm">
            <span className="font-medium">{level.title}</span>
            <span className="num text-ink-faint">
              {level.xp} / {level.nextLevelXp} XP
            </span>
          </p>
          <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-sunken">
            <div className="h-full rounded-full bg-pine transition-[width] duration-700" style={{ width: `${(into / span) * 100}%` }} />
          </div>
        </div>
      </div>

      {streaks.length > 0 && (
        <ul className="mb-4 flex flex-wrap gap-1.5">
          {streaks.map((s) => (
            <StreakChip key={s.key} s={s} />
          ))}
        </ul>
      )}

      <ul className="grid gap-2.5">
        {data.quests.map((q) => (
          <li key={q.key} className={clsx('rounded-lg border px-3 py-2.5', q.done ? 'border-pine/50 bg-pine-soft/60' : 'border-line')}>
            <div className="flex items-start justify-between gap-2">
              <p className={clsx('flex items-center gap-1.5 font-medium', q.done && 'pop')}>
                {q.done && (
                  <span className="grid size-4 place-items-center rounded-full bg-pine text-on-pine">
                    <Check className="size-3" strokeWidth={3} aria-hidden />
                  </span>
                )}
                {q.title}
              </p>
              <span className={clsx('num shrink-0 text-xs font-semibold', q.done ? 'text-pine' : 'text-ochre')}>+{q.xp} XP</span>
            </div>
            <p className="mt-0.5 text-sm text-ink-soft">{q.amount !== null ? q.detail.replace('{amount}', money(q.amount, currency)) : q.detail}</p>
            {!q.done && q.goal > 1 && (
              <div className="mt-2 flex items-center gap-2">
                <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-sunken">
                  <div className="h-full rounded-full bg-ochre" style={{ width: `${(q.progress / q.goal) * 100}%` }} />
                </div>
                <span className="num text-xs text-ink-faint">
                  {q.progress}/{q.goal}
                </span>
              </div>
            )}
          </li>
        ))}
      </ul>
      <p className="mt-2 text-xs text-ink-faint">
        Week of {shortDate(data.weekStart)}. Level up to dress up your pet.
      </p>
    </section>
  );
}

function StreakChip({ s }: { s: Streak }) {
  const Icon = STREAK_ICON[s.key] ?? Flame;
  const hot = s.current >= 3;
  const unit = s.unit === 'bills' ? (s.current === 1 ? 'bill' : 'bills') : s.unit === 'weeks' ? (s.current === 1 ? 'week' : 'weeks') : s.current === 1 ? 'day' : 'days';
  return (
    <li
      className={clsx(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[0.8rem]',
        hot ? 'border-ochre/60 bg-ochre-soft text-ink' : s.current > 0 ? 'border-line text-ink' : 'border-line text-ink-faint',
      )}
      title={`${s.title}: ${s.current} now, best ${s.best}`}
    >
      {hot ? <Flame className="size-3.5 text-ochre" aria-hidden /> : <Icon className="size-3.5" aria-hidden />}
      <span className="num font-semibold">{s.current}</span> {unit}
      <span className="text-ink-soft">{STREAK_WORDS[s.key] ?? s.title.toLowerCase()}</span>
      {s.best > s.current && <span className="text-ink-faint">· best {s.best}</span>}
    </li>
  );
}
