import { CircleCheck, CircleAlert, X } from 'lucide-react';
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';

type Tone = 'ok' | 'error';
interface ToastItem {
  id: number;
  message: string;
  tone: Tone;
  action?: { label: string; run: () => void };
}

interface ToastApi {
  show: (message: string, opts?: { tone?: Tone; action?: ToastItem['action'] }) => void;
}

const ToastContext = createContext<ToastApi>({ show: () => {} });

// eslint-disable-next-line react-refresh/only-export-components
export const useToast = () => useContext(ToastContext);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setItems((all) => all.filter((t) => t.id !== id)), []);

  const show = useCallback<ToastApi['show']>(
    (message, opts) => {
      const id = nextId.current++;
      setItems((all) => [...all.slice(-2), { id, message, tone: opts?.tone ?? 'ok', action: opts?.action }]);
      setTimeout(() => dismiss(id), opts?.action ? 8000 : 4000);
    },
    [dismiss],
  );

  const value = useMemo(() => ({ show }), [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div aria-live="polite" className="pointer-events-none fixed inset-x-0 bottom-20 z-50 flex flex-col items-center gap-2 px-4 md:bottom-6">
        {items.map((t) => (
          <div
            key={t.id}
            className="pointer-events-auto flex max-w-md items-center gap-3 rounded-lg bg-ink px-4 py-2.5 text-sm text-paper shadow-lg"
          >
            {t.tone === 'ok' ? (
              <CircleCheck className="size-4 shrink-0 text-pine-soft" aria-hidden />
            ) : (
              <CircleAlert className="size-4 shrink-0 text-ochre" aria-hidden />
            )}
            <span>{t.message}</span>
            {t.action && (
              <button
                className="font-semibold text-ochre underline-offset-2 hover:underline"
                onClick={() => {
                  t.action!.run();
                  dismiss(t.id);
                }}
              >
                {t.action.label}
              </button>
            )}
            <button aria-label="Dismiss" onClick={() => dismiss(t.id)} className="ml-1 text-paper/60 hover:text-paper">
              <X className="size-3.5" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
