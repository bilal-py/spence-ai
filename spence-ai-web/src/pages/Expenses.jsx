import { useCallback, useEffect, useState } from 'react';
import { api } from '../services/api';

const currentYear = new Date().getFullYear();
const currentMonth = new Date().getMonth() + 1;

function formatCurrency(value) {
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: 'USD',
  }).format(value ?? 0);
}

function formatDate(value) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
      });
}

export default function Expenses() {
  const [year, setYear] = useState(String(currentYear));
  const [month, setMonth] = useState(String(currentMonth));
  const [categoryFilter, setCategoryFilter] = useState('');
  const [expenses, setExpenses] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const loadExpenses = useCallback(async () => {
    setLoading(true);
    setError(null);

    const parsedYear = year ? Number(year) : null;
    const parsedMonth = month ? Number(month) : null;
    const categoryIds = categoryFilter
      ? categoryFilter
          .split(',')
          .map((id) => id.trim())
          .filter(Boolean)
          .map(Number)
          .filter((id) => !Number.isNaN(id))
      : null;

    try {
      const data = await api.expenses.getFiltered(parsedYear, parsedMonth, categoryIds);
      setExpenses(Array.isArray(data) ? data : []);
    } catch (err) {
      setExpenses([]);
      setError(err instanceof Error ? err.message : 'Failed to load expenses.');
    } finally {
      setLoading(false);
    }
  }, [year, month, categoryFilter]);

  useEffect(() => {
    loadExpenses();
  }, [loadExpenses]);

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Expenses</h2>
        <p className="mt-1 text-sm text-slate-500">
          Filter and review raw transactions from your workspace.
        </p>
      </div>

      <section className="dashboard-panel p-4 sm:p-5">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Year</span>
            <input
              type="number"
              min="2000"
              max="2100"
              value={year}
              onChange={(event) => setYear(event.target.value)}
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-teal-500 focus:outline-none focus:ring-2 focus:ring-teal-500/20"
            />
          </label>
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Month</span>
            <select
              value={month}
              onChange={(event) => setMonth(event.target.value)}
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-teal-500 focus:outline-none focus:ring-2 focus:ring-teal-500/20"
            >
              <option value="">All months</option>
              {Array.from({ length: 12 }, (_, index) => (
                <option key={index + 1} value={String(index + 1)}>
                  {new Date(2000, index, 1).toLocaleString(undefined, { month: 'long' })}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-sm sm:col-span-2">
            <span className="mb-1 block font-medium text-slate-700">
              Category IDs (comma-separated)
            </span>
            <input
              type="text"
              value={categoryFilter}
              onChange={(event) => setCategoryFilter(event.target.value)}
              placeholder="e.g. 1, 3, 5"
              className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-teal-500 focus:outline-none focus:ring-2 focus:ring-teal-500/20"
            />
          </label>
        </div>
        <div className="mt-4 flex justify-end">
          <button
            type="button"
            onClick={loadExpenses}
            className="rounded-lg bg-teal-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-teal-800"
          >
            Apply filters
          </button>
        </div>
      </section>

      {loading ? (
        <div className="dashboard-panel p-8 text-center text-sm text-slate-500">
          Loading transactions…
        </div>
      ) : error ? (
        <div className="dashboard-panel border-red-200 bg-red-50 p-6 text-red-800">
          <p className="font-semibold">Unable to load expenses</p>
          <p className="mt-2 text-sm">{error}</p>
        </div>
      ) : expenses.length === 0 ? (
        <div className="dashboard-panel p-8 text-center">
          <p className="font-semibold text-slate-800">No transactions found</p>
          <p className="mt-2 text-sm text-slate-500">
            Adjust your filters or upload a PDF to populate this list.
          </p>
        </div>
      ) : (
        <div className="dashboard-panel overflow-hidden">
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
              <thead className="bg-slate-50">
                <tr>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Date</th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">
                    Description
                  </th>
                  <th className="px-4 py-3 text-left font-semibold text-slate-600">Category</th>
                  <th className="px-4 py-3 text-right font-semibold text-slate-600">Amount</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 bg-white">
                {expenses.map((expense) => (
                  <tr key={expense.id} className="hover:bg-slate-50/80">
                    <td className="whitespace-nowrap px-4 py-3 text-slate-700">
                      {formatDate(expense.date)}
                    </td>
                    <td className="px-4 py-3 text-slate-800">{expense.description}</td>
                    <td className="px-4 py-3">
                      <span
                        className="inline-flex items-center gap-2 rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-700"
                      >
                        <span
                          className="h-2 w-2 rounded-full"
                          style={{
                            backgroundColor:
                              expense.category?.colorCode ?? expense.colorCode ?? '#64748b',
                          }}
                        />
                        {expense.category?.name ?? expense.categoryName ?? 'Uncategorized'}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right font-medium text-slate-900">
                      {formatCurrency(expense.amount)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
