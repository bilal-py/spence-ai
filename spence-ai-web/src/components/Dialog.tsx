import { X } from 'lucide-react';
import { useEffect, useRef, type ReactNode } from 'react';

/** Native <dialog>: focus trapping, Escape and the backdrop come for free. */
export function Dialog({
  open,
  onClose,
  title,
  children,
  wide,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (open && !el.open) el.showModal();
    if (!open && el.open) el.close();
  }, [open]);

  return (
    <dialog
      ref={ref}
      onClose={onClose}
      onClick={(e) => e.target === ref.current && onClose()}
      className={`m-auto w-[calc(100%-2rem)] rounded-xl border border-line bg-surface p-0 text-ink shadow-2xl ${wide ? 'max-w-2xl' : 'max-w-lg'}`}
    >
      {open && (
        <div className="p-6">
          <div className="mb-5 flex items-start justify-between gap-4">
            <h2 className="font-display text-2xl font-semibold tracking-[-0.01em]">{title}</h2>
            <button onClick={onClose} aria-label="Close" className="-mt-1 -mr-2 rounded-md p-2 text-ink-soft hover:bg-sunken hover:text-ink">
              <X className="size-4" />
            </button>
          </div>
          {children}
        </div>
      )}
    </dialog>
  );
}
