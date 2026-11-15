import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { api, type Category, type CategoryInput } from '../lib/api';
import { useCurrency, useInvalidateMoney } from '../lib/hooks';
import { CATEGORY_ICONS, CategoryIcon } from '../lib/icons';
import { useToast } from './Toast';
import { Button, ErrorNote, Segmented } from './ui';

// eslint-disable-next-line react-refresh/only-export-components
export const SWATCHES = ['#3f8f5f', '#2a9d6f', '#2f8f9d', '#3b6fd9', '#5b6bd1', '#7a5bd1', '#a3589e', '#c94f8a', '#d94848', '#d9663b', '#c7653f', '#b5832a', '#6b7f2a', '#8a6a3b', '#7d7468'];

/** Create or edit a category. `onDone` gets the saved category (undefined on cancel or delete). */
export function CategoryForm({
  category,
  initialKind = 'Expense',
  onDone,
}: {
  category?: Category;
  /** For a new category: Spending or Income, picked from the transaction it's created for. */
  initialKind?: 'Expense' | 'Income';
  onDone: (saved?: Category) => void;
}) {
  const toast = useToast();
  const invalidate = useInvalidateMoney();
  const currency = useCurrency();
  const [form, setForm] = useState<CategoryInput>({
    name: category?.name ?? '',
    color: category?.color ?? SWATCHES[0],
    icon: category?.icon ?? 'tag',
    kind: category?.kind ?? initialKind,
    monthlyBudget: category?.monthlyBudget ?? null,
  });

  const save = useMutation({
    mutationFn: () => (category ? api.categories.update(category.id, form) : api.categories.create(form)),
    onSuccess: (saved) => {
      invalidate();
      toast.show(category ? 'Category saved' : 'Category created');
      onDone(saved);
    },
  });

  const remove = useMutation({
    mutationFn: () => api.categories.remove(category!.id),
    onSuccess: () => {
      invalidate();
      toast.show(`Deleted ${category!.name}`);
      onDone();
    },
  });

  const submit = (e: FormEvent) => {
    // Stop the submit reaching a form this dialog was opened from (e.g. the transaction editor).
    e.preventDefault();
    e.stopPropagation();
    save.mutate();
  };

  return (
    <form onSubmit={submit} className="grid gap-5">
      <div className="flex items-end gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-lg" style={{ background: `${form.color}22`, color: form.color }}>
          <CategoryIcon name={form.icon} className="size-6" />
        </span>
        <div className="flex-1">
          <label className="label" htmlFor="cat-name">
            Name
          </label>
          <input id="cat-name" className="field" required maxLength={60} autoFocus value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </div>
      </div>

      <Segmented
        label="Kind"
        value={form.kind === 'Income' ? 'Income' : 'Expense'}
        onChange={(kind) => setForm({ ...form, kind, monthlyBudget: kind === 'Income' ? null : form.monthlyBudget })}
        options={[
          { value: 'Expense', label: 'Spending' },
          { value: 'Income', label: 'Income' },
        ]}
      />

      <fieldset>
        <legend className="label">Color</legend>
        <div className="flex flex-wrap gap-2">
          {SWATCHES.map((c) => (
            <button
              key={c}
              type="button"
              aria-label={`Color ${c}`}
              aria-pressed={form.color === c}
              onClick={() => setForm({ ...form, color: c })}
              className={clsx('size-7 rounded-full ring-offset-2 ring-offset-surface', form.color === c && 'ring-2 ring-ink')}
              style={{ background: c }}
            />
          ))}
        </div>
      </fieldset>

      <fieldset>
        <legend className="label">Icon</legend>
        <div className="grid grid-cols-10 gap-1">
          {Object.keys(CATEGORY_ICONS).map((name) => (
            <button
              key={name}
              type="button"
              aria-label={name}
              aria-pressed={form.icon === name}
              onClick={() => setForm({ ...form, icon: name })}
              className={clsx('grid aspect-square place-items-center rounded-md', form.icon === name ? 'bg-ink text-paper' : 'text-ink-soft hover:bg-sunken')}
            >
              <CategoryIcon name={name} className="size-4" />
            </button>
          ))}
        </div>
      </fieldset>

      {form.kind !== 'Income' && (
        <div>
          <label className="label" htmlFor="cat-budget">
            Monthly budget ({currency}), optional
          </label>
          <input
            id="cat-budget"
            className="field num"
            type="number"
            min="0"
            step="1"
            placeholder="No budget"
            value={form.monthlyBudget ?? ''}
            onChange={(e) => setForm({ ...form, monthlyBudget: e.target.value === '' ? null : Number(e.target.value) })}
          />
        </div>
      )}

      <ErrorNote error={save.error ?? remove.error} />

      <div className="flex items-center justify-between gap-2">
        {category ? (
          <Button type="button" variant="danger" busy={remove.isPending} onClick={() => remove.mutate()} title="Its transactions become uncategorized">
            <Trash2 className="size-4" aria-hidden /> Delete
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button type="button" variant="ghost" onClick={() => onDone()}>
            Cancel
          </Button>
          <Button type="submit" variant="primary" busy={save.isPending}>
            {category ? 'Save changes' : 'Create category'}
          </Button>
        </div>
      </div>
    </form>
  );
}
