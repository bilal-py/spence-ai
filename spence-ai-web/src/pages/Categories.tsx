import { useMutation, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Plus, Trash2, Wand2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Dialog } from '../components/Dialog';
import { useToast } from '../components/Toast';
import { Button, ErrorNote, PageHeader, SectionTitle, Segmented, Spinner } from '../components/ui';
import { api, type Category, type CategoryInput } from '../lib/api';
import { money } from '../lib/format';
import { keys, useCategories, useCurrency, useInvalidateMoney } from '../lib/hooks';
import { CATEGORY_ICONS, CategoryIcon } from '../lib/icons';

const SWATCHES = ['#3f8f5f', '#2a9d6f', '#2f8f9d', '#3b6fd9', '#5b6bd1', '#7a5bd1', '#a3589e', '#c94f8a', '#d94848', '#d9663b', '#c7653f', '#b5832a', '#6b7f2a', '#8a6a3b', '#7d7468'];

export default function Categories() {
  const { data: categories, error, isPending } = useCategories();
  const currency = useCurrency();
  const [editing, setEditing] = useState<{ open: boolean; category?: Category; nonce: number }>({ open: false, nonce: 0 });
  const open = (category?: Category) => setEditing((s) => ({ open: true, category, nonce: s.nonce + 1 }));

  const groups = [
    { title: 'Spending', items: categories?.filter((c) => c.kind !== 'Income') ?? [] },
    { title: 'Income', items: categories?.filter((c) => c.kind === 'Income') ?? [] },
  ];

  return (
    <>
      <PageHeader title="Categories">
        <Button variant="primary" onClick={() => open()}>
          <Plus className="size-4" aria-hidden /> New category
        </Button>
      </PageHeader>

      <ErrorNote error={error} />
      {isPending && <Spinner />}

      <div className="grid grid-cols-1 gap-12 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
        <div className="grid min-w-0 grid-cols-1 content-start gap-10">
          {groups.map((g) => (
            <section key={g.title}>
              <SectionTitle aside={g.title === 'Spending' ? 'Monthly budget' : undefined}>{g.title}</SectionTitle>
              <ul className="divide-y divide-line">
                {g.items.map((c) => (
                  <li key={c.id}>
                    <button onClick={() => open(c)} className="grid w-full grid-cols-[2rem_1fr_auto] items-center gap-3 py-2.5 text-left hover:bg-surface/60">
                      <span className="grid size-8 place-items-center rounded-md" style={{ background: `${c.color}22`, color: c.color }}>
                        <CategoryIcon name={c.icon} className="size-4" />
                      </span>
                      <span>
                        <span className="font-medium">{c.name}</span>
                        <span className="ml-2 text-xs text-ink-faint">{c.transactionCount} transactions</span>
                      </span>
                      <span className={clsx('num text-sm', c.monthlyBudget ? 'text-ink' : 'text-ink-faint')}>
                        {c.monthlyBudget ? money(c.monthlyBudget, currency) : g.title === 'Spending' ? 'No budget' : ''}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
        <Rules />
      </div>

      <Dialog open={editing.open} onClose={() => setEditing((s) => ({ ...s, open: false }))} title={editing.category ? `Edit ${editing.category.name}` : 'New category'}>
        <CategoryForm key={editing.nonce} category={editing.category} onDone={() => setEditing((s) => ({ ...s, open: false }))} />
      </Dialog>
    </>
  );
}

function CategoryForm({ category, onDone }: { category?: Category; onDone: () => void }) {
  const toast = useToast();
  const invalidate = useInvalidateMoney();
  const currency = useCurrency();
  const [form, setForm] = useState<CategoryInput>({
    name: category?.name ?? '',
    color: category?.color ?? SWATCHES[0],
    icon: category?.icon ?? 'tag',
    kind: category?.kind ?? 'Expense',
    monthlyBudget: category?.monthlyBudget ?? null,
  });

  const save = useMutation({
    mutationFn: () => (category ? api.categories.update(category.id, form) : api.categories.create(form)),
    onSuccess: () => {
      invalidate();
      toast.show(category ? 'Category saved' : 'Category created');
      onDone();
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
    e.preventDefault();
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
          <Button type="button" variant="ghost" onClick={onDone}>
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

function Rules() {
  const invalidate = useInvalidateMoney();
  const rules = useQuery({ queryKey: keys.rules, queryFn: api.rules.list });
  const remove = useMutation({ mutationFn: api.rules.remove, onSuccess: invalidate });

  return (
    <section>
      <SectionTitle>Learned rules</SectionTitle>
      <p className="mb-4 text-sm text-ink-soft">
        When you change the type or category of an imported transaction, Spence remembers it for that merchant, separately for money in and money
        out, and applies it before the AI's guess next time. Overriding a rule on one row adds a more specific rule for that row, so the general rule
        keeps working.
      </p>
      <ErrorNote error={rules.error ?? remove.error} />
      {rules.data?.length === 0 && (
        <p className="flex items-center gap-2 text-sm text-ink-faint">
          <Wand2 className="size-4" aria-hidden /> No rules yet.
        </p>
      )}
      <ul className="divide-y divide-line">
        {rules.data?.map((r) => (
          <li key={r.id} className="flex items-center gap-3 py-2">
            <code className="truncate rounded bg-sunken px-1.5 py-0.5 text-[0.8rem]">{r.pattern}</code>
            {r.direction && <span className="text-xs text-ink-faint">{r.direction === 'Out' ? 'money out' : 'money in'}</span>}
            <span className="text-ink-faint" aria-hidden>
              →
            </span>
            {r.type && <span className="text-sm">{r.type}</span>}
            {r.categoryName && (
              <span className="flex min-w-0 items-center gap-1.5 truncate text-sm">
                <span className="size-2 shrink-0 rounded-full" style={{ background: r.categoryColor ?? undefined }} />
                {r.categoryName}
              </span>
            )}
            <button aria-label={`Delete rule ${r.pattern}`} onClick={() => remove.mutate(r.id)} className="ml-auto rounded p-1.5 text-ink-faint hover:bg-sunken hover:text-brick">
              <Trash2 className="size-3.5" />
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}
