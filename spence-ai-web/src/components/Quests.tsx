import { useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { CalendarCheck, Check, ChevronDown, Feather, Flame, Leaf, Lock, Receipt, Sparkles, Trophy } from 'lucide-react';
import { useEffect } from 'react';
import type { PetAccessory, Quest, Streak } from '../lib/api';
import { celebrate } from '../lib/celebrate';
import { money, shortDate } from '../lib/format';
import { keys, useCurrency, useQuests } from '../lib/hooks';
import { useToast } from './Toast';
import { SectionTitle } from './ui';

const STREAK_ICON = { 'check-in': CalendarCheck, 'no-spend': Leaf, 'light-weeks': Feather, bills: Receipt } as const;
const STREAK_WORDS = { 'check-in': 'checking in', 'no-spend': 'no-spend', 'light-weeks': 'under your usual', bills: 'paid on time' } as const;

const OUTFIT_LABEL: Record<PetAccessory, string> = { flower: 'a flower', bowtie: 'a bow tie', shades: 'shades', crown: 'a crown', scarf: 'a scarf', acorn: 'an acorn cap' };

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

/** "Quest done", "Achievement" or "Streak bonus", from the key's prefix. */
function doneLabel(q: Quest): string {
  if (q.key.startsWith('ach:')) return `Achievement: ${q.title}`;
  if (q.key.startsWith('streak:')) return `Streak bonus: ${q.title}`;
  return `Quest done: ${q.title.replace(/^Bonus: /, 'bonus, ')}`;
}

const unitWord = (unit: Streak['unit'], n: number) =>
  unit === 'bills' ? (n === 1 ? 'bill' : 'bills') : unit === 'weeks' ? (n === 1 ? 'week' : 'weeks') : n === 1 ? 'day' : 'days';

/**
 * Streak chips, this week's three side quests (plus a bonus once they're done), monthly quests, achievements and the XP level.
 * Celebrates anything newly finished, or a new level, once.
 */
export function QuestsPanel() {
  const { data } = useQuests();
  const toast = useToast();
  const currency = useCurrency();
  const qc = useQueryClient();

  useEffect(() => {
    if (!data) return;
    const before = readLevel();
    writeLevel(data.level.number);
    // New XP can change the pet's outfit, so refetch it rather than wait for the next write.
    if (before !== data.level.number || data.justCompleted.length > 0) qc.invalidateQueries({ queryKey: keys.pet });
    if (before !== null && data.level.number > before) {
      const outfit = data.unlocked[data.unlocked.length - 1];
      celebrate({ big: true, emoji: ['🌱', '✨', '🎉'] });
      toast.show(`Level ${data.level.number}: ${data.level.title}!${outfit ? ` Your pet unlocked ${OUTFIT_LABEL[outfit]}.` : ''}`);
      return;
    }
    if (data.justCompleted.length > 0) {
      const done = data.justCompleted;
      celebrate({ emoji: done.some((q) => q.key.startsWith('ach:')) ? ['🏆'] : ['⭐'] });
      toast.show(done.length === 1 ? `${doneLabel(done[0])}. +${done[0].xp} XP` : `${done.length} things done. +${done.reduce((s, x) => s + x.xp, 0)} XP`);
    }
  }, [data, toast, qc]);

  if (!data) return null;
  const level = data.level;
  const span = Math.max(1, level.nextLevelXp - level.levelStartXp);
  const into = Math.min(span, level.xp - level.levelStartXp);
  const streaks = data.streaks.filter((s) => s.current > 0 || s.best > 1);
  const weeklyDone = data.quests.filter((q) => q.done).length;
  // The streak bonus closest to being reached, among streaks still going.
  const nextBonus = data.streaks
    .filter((s) => s.alive && s.nextMilestone !== null)
    .sort((a, b) => a.nextMilestone! - a.current - (b.nextMilestone! - b.current))[0];
  const earned = data.achievements.filter((a) => a.done).length;
  const fill = (q: Quest) => (q.amount !== null ? q.detail.replace('{amount}', money(q.amount, currency)) : q.detail);

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
        <ul className="mb-2 flex flex-wrap gap-1.5">
          {streaks.map((s) => (
            <StreakChip key={s.key} s={s} />
          ))}
        </ul>
      )}
      {nextBonus && (
        <p className="mb-4 text-xs text-ink-soft">
          Streak bonus at {nextBonus.nextMilestone} {unitWord(nextBonus.unit, nextBonus.nextMilestone!)} {STREAK_WORDS[nextBonus.key]}:{' '}
          <span className="num font-semibold text-ochre">+{nextBonus.nextMilestoneXp} XP</span>
        </p>
      )}

      <ul className="grid gap-2.5">
        {data.quests.map((q) => (
          <QuestCard key={q.key} q={q} detail={fill(q)} />
        ))}
        {data.bonus ? (
          <QuestCard q={data.bonus} detail={fill(data.bonus)} bonus />
        ) : (
          weeklyDone < data.quests.length && (
            <li className="flex items-center gap-1.5 rounded-lg border border-dashed border-line px-3 py-2 text-sm text-ink-faint">
              <Lock className="size-3.5" aria-hidden /> Finish all {data.quests.length} to unlock a bonus quest.
            </li>
          )
        )}
      </ul>

      {data.monthly.length > 0 && (
        <>
          <h3 className="mt-5 mb-2 text-sm font-medium text-ink-soft">This month</h3>
          <ul className="grid gap-2">
            {data.monthly.map((q) => (
              <QuestCard key={q.key} q={q} detail={fill(q)} compact />
            ))}
          </ul>
        </>
      )}

      <details className="group mt-5 rounded-lg border border-line">
        <summary className="flex cursor-pointer list-none items-center gap-2 px-3 py-2.5 text-sm font-medium [&::-webkit-details-marker]:hidden">
          <Trophy className="size-4 text-ochre" aria-hidden />
          Achievements
          <span className="num text-ink-faint">
            {earned} of {data.achievements.length}
          </span>
          <ChevronDown className="ml-auto size-4 text-ink-faint transition-transform group-open:rotate-180" aria-hidden />
        </summary>
        <ul className="grid gap-1 border-t border-line p-2">
          {data.achievements.map((a) => (
            <Achievement key={a.key} a={a} />
          ))}
        </ul>
      </details>

      <p className="mt-2 text-xs text-ink-faint">Week of {shortDate(data.weekStart)}. Level up to dress up your pet.</p>
    </section>
  );
}

function QuestCard({ q, detail, bonus, compact }: { q: Quest; detail: string; bonus?: boolean; compact?: boolean }) {
  return (
    <li
      className={clsx(
        'rounded-lg border px-3',
        compact ? 'py-2' : 'py-2.5',
        q.done ? 'border-pine/50 bg-pine-soft/60' : bonus ? 'border-ochre/60 bg-ochre-soft/40' : 'border-line',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <p className={clsx('flex items-center gap-1.5 font-medium', compact && 'text-sm', q.done && 'pop')}>
          {q.done ? (
            <span className="grid size-4 shrink-0 place-items-center rounded-full bg-pine text-on-pine">
              <Check className="size-3" strokeWidth={3} aria-hidden />
            </span>
          ) : (
            bonus && <Sparkles className="size-4 shrink-0 text-ochre" aria-hidden />
          )}
          {q.title}
        </p>
        <span className={clsx('num shrink-0 text-xs font-semibold', q.done ? 'text-pine' : 'text-ochre')}>+{q.xp} XP</span>
      </div>
      <p className={clsx('mt-0.5 text-ink-soft', compact ? 'text-xs' : 'text-sm')}>{detail}</p>
      {!q.done && q.goal > 1 && <Progress value={q.progress} goal={q.goal} />}
    </li>
  );
}

function Achievement({ a }: { a: Quest }) {
  return (
    <li className={clsx('flex items-start gap-2.5 rounded-md px-2 py-1.5', a.done ? '' : 'text-ink-soft')}>
      <span className={clsx('mt-0.5 grid size-6 shrink-0 place-items-center rounded-full', a.done ? 'bg-ochre-soft text-ochre' : 'bg-sunken text-ink-faint')}>
        {a.done ? <Trophy className="size-3.5" aria-hidden /> : <Lock className="size-3" aria-hidden />}
      </span>
      <div className="min-w-0 flex-1">
        <p className="flex justify-between gap-2 text-sm">
          <span className={clsx(a.done && 'font-medium text-ink')}>{a.title}</span>
          <span className={clsx('num shrink-0 text-xs font-semibold', a.done ? 'text-pine' : 'text-ink-faint')}>
            {a.done ? 'Earned' : `+${a.xp} XP`}
          </span>
        </p>
        <p className="text-xs text-ink-faint">{a.detail}</p>
        {!a.done && a.goal > 1 && <Progress value={a.progress} goal={a.goal} />}
      </div>
    </li>
  );
}

function Progress({ value, goal }: { value: number; goal: number }) {
  return (
    <div className="mt-1.5 flex items-center gap-2">
      <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-sunken">
        <div className="h-full rounded-full bg-ochre" style={{ width: `${(value / goal) * 100}%` }} />
      </div>
      <span className="num text-xs text-ink-faint">
        {value}/{goal}
      </span>
    </div>
  );
}

function StreakChip({ s }: { s: Streak }) {
  const Icon = STREAK_ICON[s.key] ?? Flame;
  const hot = s.current >= 3;
  return (
    <li
      className={clsx(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[0.8rem]',
        hot ? 'border-ochre/60 bg-ochre-soft text-ink' : s.current > 0 ? 'border-line text-ink' : 'border-line text-ink-faint',
      )}
      title={`${s.title}: ${s.current} now, best ${s.best}`}
    >
      {hot ? <Flame className="size-3.5 text-ochre" aria-hidden /> : <Icon className="size-3.5" aria-hidden />}
      <span className="num font-semibold">{s.current}</span> {unitWord(s.unit, s.current)}
      <span className="text-ink-soft">{STREAK_WORDS[s.key] ?? s.title.toLowerCase()}</span>
      {s.best > s.current && <span className="text-ink-faint">· best {s.best}</span>}
    </li>
  );
}
