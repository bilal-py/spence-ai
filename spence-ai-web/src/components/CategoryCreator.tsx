import { useQueryClient } from '@tanstack/react-query';
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import type { Category } from '../lib/api';
import { keys } from '../lib/hooks';
import { CategoryForm } from './CategoryForm';
import { Dialog } from './Dialog';

interface CreatorApi {
  /** Opens the new-category dialog; `onCreated` gets the category once it's saved. */
  create: (opts: { kind: 'Expense' | 'Income'; onCreated: (category: Category) => void }) => void;
}

const CreatorContext = createContext<CreatorApi>({ create: () => {} });

// eslint-disable-next-line react-refresh/only-export-components
export const useCategoryCreator = () => useContext(CreatorContext);

/** One "New category" dialog for every category picker, stacked over whatever dialog it was opened from. */
export function CategoryCreatorProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{ open: boolean; kind: 'Expense' | 'Income'; nonce: number }>({ open: false, kind: 'Expense', nonce: 0 });
  const callback = useRef<((c: Category) => void) | null>(null);
  const qc = useQueryClient();

  const create = useCallback<CreatorApi['create']>(({ kind, onCreated }) => {
    callback.current = onCreated;
    setState((s) => ({ open: true, kind, nonce: s.nonce + 1 }));
  }, []);
  const close = useCallback(() => setState((s) => ({ ...s, open: false })), []);
  const value = useMemo(() => ({ create }), [create]);

  return (
    <CreatorContext.Provider value={value}>
      {children}
      <Dialog open={state.open} onClose={close} title="New category">
        <CategoryForm
          key={state.nonce}
          initialKind={state.kind}
          onDone={(saved) => {
            if (saved) {
              // Add it to the cached list right away, so the picker that asked for it can show it selected.
              qc.setQueryData<Category[]>(keys.categories, (all) => (all && !all.some((c) => c.id === saved.id) ? [...all, saved] : all));
              callback.current?.(saved);
            }
            callback.current = null;
            close();
          }}
        />
      </Dialog>
    </CreatorContext.Provider>
  );
}
