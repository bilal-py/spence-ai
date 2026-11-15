import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { Link } from 'react-router';
import { clsx } from 'clsx';
import type { ReactNode } from 'react';
import { Lock } from 'lucide-react';
import { api, type PetAccessory, type PetMood, type PetState } from '../lib/api';
import { money, today } from '../lib/format';
import { keys, useCurrency } from '../lib/hooks';
import { ErrorNote, SectionTitle } from './ui';

/**
 * Sprig: a round little seedling. Original artwork, drawn in SVG so it follows the theme.
 * The mood changes its eyes, mouth and leaf: perky when things go well, droopy when asleep.
 */
export function Sprig({ mood, className, accessory }: { mood: PetMood; className?: string; accessory?: PetAccessory | null }) {
  const leafAngle = { cheerful: -8, proud: -14, content: 0, watchful: 14, sleepy: 38 }[mood];
  return (
    <svg viewBox="0 0 64 64" className={className} role="img" aria-label={`Your pet looks ${mood}`}>
      <g className={mood === 'proud' || mood === 'cheerful' ? 'pet-bounce' : mood === 'sleepy' ? 'pet-breathe' : undefined}>
        {/* stem and leaves */}
        <g transform={`rotate(${leafAngle} 32 20)`}>
          <path d="M32 21 C32 16 32 13 32 10" stroke="var(--pine)" strokeWidth="2.6" strokeLinecap="round" fill="none" />
          <path d="M32 12 C26 4 18 7 18 11 C24 14 29 14 32 12Z" fill="var(--pine)" />
          <path d="M32 12 C37 3 46 5 46 9 C41 13 35 14 32 12Z" fill="var(--pine-strong)" />
          {accessory === 'flower' && (
            <g transform="translate(46 7)">
              {[0, 72, 144, 216, 288].map((a) => (
                <circle key={a} cx={Math.cos((a * Math.PI) / 180) * 2.6} cy={Math.sin((a * Math.PI) / 180) * 2.6} r="2.2" fill="#f2a7c3" />
              ))}
              <circle r="1.7" fill="var(--ochre)" />
            </g>
          )}
        </g>
        {/* body */}
        <ellipse cx="32" cy="40" rx="20" ry="18" fill="var(--pine-soft)" stroke="var(--pine)" strokeWidth="2.4" />
        {/* cheeks */}
        <ellipse cx="20.5" cy="44" rx="3.4" ry="2.2" fill="var(--ochre)" opacity={mood === 'sleepy' ? 0.35 : 0.7} />
        <ellipse cx="43.5" cy="44" rx="3.4" ry="2.2" fill="var(--ochre)" opacity={mood === 'sleepy' ? 0.35 : 0.7} />
        <Face mood={mood} />
        {accessory === 'shades' && (
          <g>
            <rect x="20.5" y="34" width="10" height="6.5" rx="2.6" fill="var(--ink)" />
            <rect x="33.5" y="34" width="10" height="6.5" rx="2.6" fill="var(--ink)" />
            <path d="M30.5 36.2 h3" stroke="var(--ink)" strokeWidth="1.6" />
            <path d="M22.5 35.6 l2.2 0" stroke="#fff" strokeOpacity="0.7" strokeWidth="1.1" strokeLinecap="round" />
            <path d="M35.5 35.6 l2.2 0" stroke="#fff" strokeOpacity="0.7" strokeWidth="1.1" strokeLinecap="round" />
          </g>
        )}
        {accessory === 'bowtie' && (
          <g>
            <path d="M32 54 l-6.5 -3.8 v7.6Z M32 54 l6.5 -3.8 v7.6Z" fill="var(--brick)" />
            <circle cx="32" cy="54" r="1.8" fill="var(--brick)" stroke="var(--paper)" strokeWidth="0.8" />
          </g>
        )}
        {accessory === 'crown' && (
          <path d="M24.5 25 L24 18.5 L28.2 21.5 L32 16 L35.8 21.5 L40 18.5 L39.5 25 Z" fill="var(--ochre)" stroke="var(--ink)" strokeWidth="1" strokeLinejoin="round" />
        )}
      </g>
      {mood === 'sleepy' && (
        <text x="50" y="22" fontSize="10" fontWeight="700" fill="var(--ink-faint)" className="pet-z">
          z
        </text>
      )}
      {mood === 'proud' && (
        <g fill="var(--ochre)">
          <path d="M10 18 l1.6 3.4 3.4 1.6 -3.4 1.6 -1.6 3.4 -1.6 -3.4 -3.4 -1.6 3.4 -1.6Z" />
          <path d="M54 26 l1.1 2.4 2.4 1.1 -2.4 1.1 -1.1 2.4 -1.1 -2.4 -2.4 -1.1 2.4 -1.1Z" />
        </g>
      )}
    </svg>
  );
}

function Face({ mood }: { mood: PetMood }) {
  const ink = 'var(--ink)';
  const line = { stroke: ink, strokeWidth: 2.4, strokeLinecap: 'round' as const, fill: 'none' };
  switch (mood) {
    case 'cheerful':
    case 'proud':
      return (
        <>
          <path d="M23 37 q3 -4 6 0" {...line} />
          <path d="M35 37 q3 -4 6 0" {...line} />
          <path d="M26 44 q6 6 12 0 Z" fill={ink} />
        </>
      );
    case 'sleepy':
      return (
        <>
          <path d="M23 38 q3 2 6 0" {...line} />
          <path d="M35 38 q3 2 6 0" {...line} />
          <path d="M30 46 q2 1.4 4 0" {...line} />
        </>
      );
    case 'watchful':
      return (
        <>
          <path d="M22.5 32.5 l5 1.2" {...line} strokeWidth={2} />
          <path d="M41.5 32.5 l-5 1.2" {...line} strokeWidth={2} />
          <circle cx="26" cy="38" r="2.4" fill={ink} />
          <circle cx="38" cy="38" r="2.4" fill={ink} />
          <circle cx="32" cy="46" r="2" {...line} strokeWidth={2} />
        </>
      );
    default:
      return (
        <>
          <circle cx="26" cy="38" r="2.4" fill={ink} />
          <circle cx="38" cy="38" r="2.4" fill={ink} />
          <path d="M28 44.5 q4 3 8 0" {...line} />
        </>
      );
  }
}

// eslint-disable-next-line react-refresh/only-export-components
export function usePet() {
  const day = today();
  return useQuery({ queryKey: [...keys.pet, day], queryFn: () => api.pet.get(day), staleTime: 60_000 });
}

/** The pet and its one nudge for today, shown at the top of the Overview. */
export function PetStrip() {
  const { data } = usePet();
  const currency = useCurrency();
  const qc = useQueryClient();
  const day = today();
  const dismiss = useMutation({
    mutationFn: () => api.pet.dismiss(day),
    onMutate: () => qc.setQueryData<PetState>([...keys.pet, day], (s) => (s ? { ...s, nudge: null } : s)),
  });

  if (!data?.enabled) return null;
  const nudge = data.nudge;
  const text = nudge ? nudge.text.replace('{amount}', nudge.amount !== null ? money(nudge.amount, currency) : '') : data.moodLine;

  return (
    <div className="flex min-w-0 items-center gap-3" aria-live="polite">
      <Sprig mood={data.mood} accessory={data.accessory} className="size-12 shrink-0" />
      <div className="min-w-0 text-sm">
        <p className="font-medium">{data.name}</p>
        <p className="text-ink-soft">
          {text}
          {nudge?.link && (
            <>
              {' '}
              <Link to={nudge.link} className="font-medium text-pine underline-offset-2 hover:underline">
                {nudge.link === '/pots' ? 'Open pots' : nudge.link === '/import' ? 'Import' : nudge.link === '/bills' ? 'Open bills' : 'Take a look'}
              </Link>
            </>
          )}
        </p>
      </div>
      {nudge && (
        <button onClick={() => dismiss.mutate()} className="shrink-0 rounded-md p-1.5 text-ink-faint hover:bg-sunken hover:text-ink" aria-label="Dismiss for today" title="Got it, see you tomorrow">
          <X className="size-4" />
        </button>
      )}
    </div>
  );
}

/** Settings: turn the pet on or off and rename it. */
export function PetSection() {
  const { data } = usePet();
  const qc = useQueryClient();
  const day = today();
  const save = useMutation({
    mutationFn: (input: { enabled: boolean; name: string; accessory?: PetAccessory | 'none' }) =>
      api.pet.update(input.enabled, input.name, input.accessory ?? null, day),
    onSuccess: (state) => qc.setQueryData([...keys.pet, day], state),
  });
  if (!data) return null;

  return (
    <section className="max-w-3xl">
      <SectionTitle>Pet</SectionTitle>
      <div className="flex flex-wrap items-center gap-5">
        <Sprig mood={data.enabled ? data.mood : 'sleepy'} accessory={data.accessory} className="size-16" />
        <div className="grid gap-3">
          <p className="max-w-prose text-sm text-ink-soft">
            A small companion on the Overview. Its mood follows how the month is pacing, and it offers at most one short nudge a day. It runs on simple
            rules, so it works without AI.
          </p>
          <div className="flex flex-wrap items-center gap-4">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                className="size-4 accent-[var(--pine)]"
                checked={data.enabled}
                onChange={(e) => save.mutate({ enabled: e.target.checked, name: data.name })}
              />
              Show the pet
            </label>
            <label className="flex items-center gap-2 text-sm">
              Name
              <input
                key={data.name}
                className="field h-8 w-40"
                maxLength={30}
                defaultValue={data.name}
                onBlur={(e) => {
                  const name = e.target.value.trim();
                  if (name && name !== data.name) save.mutate({ enabled: data.enabled, name });
                }}
              />
            </label>
          </div>
          <div>
            <p className="label">Outfit · level {data.level}</p>
            <div className="flex flex-wrap gap-1.5">
              <OutfitButton label="None" selected={data.accessory === null} onClick={() => save.mutate({ enabled: data.enabled, name: data.name, accessory: 'none' })} />
              {OUTFITS.map((o) => {
                const unlocked = data.unlocked.includes(o.name);
                return (
                  <OutfitButton
                    key={o.name}
                    label={o.label}
                    lockedAt={unlocked ? undefined : o.level}
                    selected={data.accessory === o.name}
                    onClick={() => unlocked && save.mutate({ enabled: data.enabled, name: data.name, accessory: o.name })}
                  >
                    <Sprig mood="cheerful" accessory={o.name} className="size-7" />
                  </OutfitButton>
                );
              })}
            </div>
            <p className="mt-1 text-xs text-ink-faint">Finish side quests on the Overview to level up and unlock more.</p>
          </div>
          <ErrorNote error={save.error} />
        </div>
      </div>
    </section>
  );
}

const OUTFITS: { name: PetAccessory; label: string; level: number }[] = [
  { name: 'flower', label: 'Flower', level: 2 },
  { name: 'bowtie', label: 'Bow tie', level: 3 },
  { name: 'shades', label: 'Shades', level: 4 },
  { name: 'crown', label: 'Crown', level: 5 },
];

function OutfitButton({ label, selected, lockedAt, onClick, children }: {
  label: string;
  selected: boolean;
  lockedAt?: number;
  onClick: () => void;
  children?: ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={lockedAt !== undefined}
      aria-pressed={selected}
      title={lockedAt !== undefined ? `Unlocks at level ${lockedAt}` : label}
      className={clsx(
        'inline-flex h-10 items-center gap-1.5 rounded-md border px-2.5 text-sm',
        selected ? 'border-pine bg-pine-soft font-medium' : 'border-line hover:border-ink-faint',
        lockedAt !== undefined && 'cursor-not-allowed opacity-50',
      )}
    >
      {lockedAt !== undefined ? <Lock className="size-3.5" aria-hidden /> : children}
      {label}
      {lockedAt !== undefined && <span className="text-xs text-ink-faint">L{lockedAt}</span>}
    </button>
  );
}
