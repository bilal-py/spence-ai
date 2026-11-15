// Small moments of joy: a confetti burst drawn on a throwaway canvas, and a short haptic buzz on phones.
// No dependencies. Respects "reduce motion": the confetti is skipped, the buzz stays (it isn't motion).

interface Piece {
  x: number;
  y: number;
  vx: number;
  vy: number;
  size: number;
  rotation: number;
  spin: number;
  color: string;
  shape: 'rect' | 'circle' | 'emoji';
  emoji?: string;
  life: number;
}

const FALLBACK = ['#1e5a46', '#c99a2e', '#b4472e', '#6fc2a0', '#e3b65a', '#5b6bd1', '#c94f8a'];

function palette(): string[] {
  const css = getComputedStyle(document.documentElement);
  const fromTheme = ['--pine', '--ochre', '--brick', '--pine-soft'].map((v) => css.getPropertyValue(v).trim()).filter(Boolean);
  return fromTheme.length > 0 ? [...fromTheme, '#5b6bd1', '#c94f8a', '#e3b65a'] : FALLBACK;
}

function reducedMotion(): boolean {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
}

/** A short vibration where supported (most Android browsers; iOS Safari ignores it). */
export function buzz(pattern: number | number[] = [16, 50, 28]) {
  try {
    navigator.vibrate?.(pattern);
  } catch {
    /* not supported */
  }
}

export interface CelebrateOptions {
  /** A bigger, longer burst for goals reached. */
  big?: boolean;
  /** Where it bursts from, as fractions of the viewport (default: a little below the middle). */
  origin?: { x: number; y: number };
  /** Mix a few emoji into the confetti, e.g. ['💸', '✨']. */
  emoji?: string[];
  /** Skip the buzz (e.g. for tiny moments). */
  silent?: boolean;
}

/** Confetti from an element's center: `celebrateFrom(event.currentTarget)`. */
export function celebrateFrom(el: Element | null, opts: Omit<CelebrateOptions, 'origin'> = {}) {
  if (!el) return celebrate(opts);
  const r = el.getBoundingClientRect();
  celebrate({ ...opts, origin: { x: (r.left + r.width / 2) / window.innerWidth, y: (r.top + r.height / 2) / window.innerHeight } });
}

export function celebrate({ big = false, origin = { x: 0.5, y: 0.6 }, emoji = [], silent = false }: CelebrateOptions = {}) {
  if (!silent) buzz(big ? [20, 60, 20, 60, 40] : [16, 50, 28]);
  if (reducedMotion() || typeof document === 'undefined') return;

  const canvas = document.createElement('canvas');
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  canvas.width = window.innerWidth * dpr;
  canvas.height = window.innerHeight * dpr;
  canvas.setAttribute('aria-hidden', 'true');
  Object.assign(canvas.style, { position: 'fixed', inset: '0', width: '100vw', height: '100vh', pointerEvents: 'none', zIndex: '9999' });
  document.body.appendChild(canvas);
  const ctx = canvas.getContext('2d');
  if (!ctx) {
    canvas.remove();
    return;
  }
  ctx.scale(dpr, dpr);

  const colors = palette();
  const count = big ? 160 : 70;
  const ox = origin.x * window.innerWidth;
  const oy = origin.y * window.innerHeight;
  const pieces: Piece[] = Array.from({ length: count }, (_, i) => {
    const angle = -Math.PI / 2 + (Math.random() - 0.5) * (big ? Math.PI * 1.1 : Math.PI * 0.8);
    const speed = (big ? 9 : 7) + Math.random() * (big ? 9 : 6);
    const useEmoji = emoji.length > 0 && i % 9 === 0;
    return {
      x: ox,
      y: oy,
      vx: Math.cos(angle) * speed,
      vy: Math.sin(angle) * speed,
      size: useEmoji ? 18 + Math.random() * 8 : 5 + Math.random() * 6,
      rotation: Math.random() * Math.PI,
      spin: (Math.random() - 0.5) * 0.35,
      color: colors[i % colors.length],
      shape: useEmoji ? 'emoji' : Math.random() < 0.35 ? 'circle' : 'rect',
      emoji: useEmoji ? emoji[i % emoji.length] : undefined,
      life: 1,
    };
  });

  const start = performance.now();
  const duration = big ? 2600 : 1800;
  const tick = (now: number) => {
    const t = now - start;
    ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
    for (const p of pieces) {
      p.vy += 0.28; // gravity
      p.vx *= 0.985; // air
      p.vy *= 0.985;
      p.x += p.vx;
      p.y += p.vy;
      p.rotation += p.spin;
      p.life = Math.max(0, 1 - t / duration);
      ctx.save();
      ctx.globalAlpha = Math.min(1, p.life * 1.6);
      ctx.translate(p.x, p.y);
      ctx.rotate(p.rotation);
      if (p.shape === 'emoji' && p.emoji) {
        ctx.font = `${p.size}px system-ui, "Apple Color Emoji", "Segoe UI Emoji", sans-serif`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(p.emoji, 0, 0);
      } else {
        ctx.fillStyle = p.color;
        if (p.shape === 'circle') {
          ctx.beginPath();
          ctx.arc(0, 0, p.size / 2, 0, Math.PI * 2);
          ctx.fill();
        } else {
          // A flat ribbon that "flips" as it spins.
          ctx.fillRect(-p.size / 2, -p.size / 4, p.size, (p.size / 2) * Math.abs(Math.cos(p.rotation * 2)) + 1);
        }
      }
      ctx.restore();
    }
    if (t < duration) requestAnimationFrame(tick);
    else canvas.remove();
  };
  requestAnimationFrame(tick);
}
