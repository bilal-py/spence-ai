import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { Link } from 'react-router';
import { api, type PetMood, type PetState } from '../lib/api';
import { money, today } from '../lib/format';
import { keys, useCurrency } from '../lib/hooks';
import { ErrorNote, SectionTitle } from './ui';

/**
 * Sprig: a round little seedling. Original artwork, drawn in SVG so it follows the theme.
 * The mood changes its eyes, mouth and leaf: perky when things go well, droopy when asleep.
 */
export function Sprig({ mood, className }: { mood: PetMood; className?: string }) {
  const leafAngle = { cheerful: -8, proud: -14, content: 0, watchful: 14, sleepy: 38 }[mood];
  return (
    <svg viewBox="0 0 64 64" className={className} role="img" aria-label={`Your pet looks ${mood}`}>
      <g className={mood === 'proud' || mood === 'cheerful' ? 'pet-bounce' : mood === 'sleepy' ? 'pet-breathe' : undefined}>
        {/* stem and leaves */}
        <g transform={`rotate(${leafAngle} 32 20)`}>
          <path d="M32 21 C32 16 32 13 32 10" stroke="var(--pine)" strokeWidth="2.6" strokeLinecap="round" fill="none" />
          <path d="M32 12 C26 4 18 7 18 11 C24 14 29 14 32 12Z" fill="var(--pine)" />
          <path d="M32 12 C37 3 46 5 46 9 C41 13 35 14 32 12Z" fill="var(--pine-strong)" />
        </g>
        {/* body */}
        <ellipse cx="32" cy="40" rx="20" ry="18" fill="var(--pine-soft)" stroke="var(--pine)" strokeWidth="2.4" />
        {/* cheeks */}
        <ellipse cx="20.5" cy="44" rx="3.4" ry="2.2" fill="var(--ochre)" opacity={mood === 'sleepy' ? 0.35 : 0.7} />
        <ellipse cx="43.5" cy="44" rx="3.4" ry="2.2" fill="var(--ochre)" opacity={mood === 'sleepy' ? 0.35 : 0.7} />
        <Face mood={mood} />
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
      <Sprig mood={data.mood} className="size-12 shrink-0" />
      <div className="min-w-0 text-sm">
        <p className="font-medium">{data.name}</p>
        <p className="text-ink-soft">
          {text}
          {nudge?.link && (
            <>
              {' '}
              <Link to={nudge.link} className="font-medium text-pine underline-offset-2 hover:underline">
                {nudge.link === '/pots' ? 'Open pots' : nudge.link === '/import' ? 'Import' : 'Take a look'}
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
    mutationFn: (input: { enabled: boolean; name: string }) => api.pet.update(input.enabled, input.name, day),
    onSuccess: (state) => qc.setQueryData([...keys.pet, day], state),
  });
  if (!data) return null;

  return (
    <section className="max-w-3xl">
      <SectionTitle>Pet</SectionTitle>
      <div className="flex flex-wrap items-center gap-5">
        <Sprig mood={data.enabled ? data.mood : 'sleepy'} className="size-16" />
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
          <ErrorNote error={save.error} />
        </div>
      </div>
    </section>
  );
}
