import type { Account } from '../lib/api';

/** Native select of active accounts (plus the current one, even if archived). Renders nothing when there are no accounts. */
export function AccountSelect({
  accounts,
  value,
  onChange,
  id,
  className = 'field',
  emptyLabel = 'No account',
  'aria-label': ariaLabel,
}: {
  accounts: Account[];
  value: number | null;
  onChange: (id: number | null) => void;
  id?: string;
  className?: string;
  emptyLabel?: string;
  'aria-label'?: string;
}) {
  const shown = accounts.filter((a) => !a.isArchived || a.id === value);
  return (
    <select
      id={id}
      aria-label={ariaLabel}
      className={className}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))}
    >
      <option value="">{emptyLabel}</option>
      {shown.map((a) => (
        <option key={a.id} value={a.id}>
          {accountLabel(a)}
        </option>
      ))}
    </select>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function accountLabel(a: Pick<Account, 'name' | 'last4'>): string {
  return a.last4 ? `${a.name} ··${a.last4}` : a.name;
}
