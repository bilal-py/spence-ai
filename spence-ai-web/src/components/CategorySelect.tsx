import type { Category, TransactionType } from '../lib/api';

/** Native select grouped by kind; the transaction's own kind is listed first. */
export function CategorySelect({
  categories,
  value,
  onChange,
  type = 'Expense',
  id,
  className = 'field',
  extraOption,
  extraSelected,
  onPickNew,
  'aria-label': ariaLabel,
}: {
  categories: Category[];
  value: number | null;
  onChange: (id: number | null) => void;
  type?: TransactionType;
  id?: string;
  className?: string;
  /** A not-yet-created category suggested by the AI, shown as "New: …" and selected when `extraSelected`. */
  extraOption?: string;
  extraSelected?: boolean;
  onPickNew?: () => void;
  'aria-label'?: string;
}) {
  const primary = type === 'Income' ? 'Income' : 'Expense';
  const first = categories.filter((c) => c.kind === primary);
  const rest = categories.filter((c) => c.kind !== primary);

  return (
    <select
      id={id}
      aria-label={ariaLabel}
      className={className}
      value={value ?? (extraOption && extraSelected ? '__new' : '')}
      onChange={(e) => {
        if (e.target.value === '__new') onPickNew?.();
        else onChange(e.target.value === '' ? null : Number(e.target.value));
      }}
    >
      {extraOption ? <option value="__new">New: {extraOption}</option> : null}
      <option value="">Uncategorized</option>
      <optgroup label={primary === 'Income' ? 'Income' : 'Spending'}>
        {first.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name}
          </option>
        ))}
      </optgroup>
      {rest.length > 0 && (
        <optgroup label={primary === 'Income' ? 'Spending' : 'Income'}>
          {rest.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </optgroup>
      )}
    </select>
  );
}
