import { useEffect, useMemo, useState } from 'react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { api } from '../services/api';

const now = new Date();

function formatCurrency(value) {
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: 'USD',
  }).format(value ?? 0);
}

function normalizeSummary(data) {
  const byCategory = Array.isArray(data?.byCategory)
    ? data.byCategory
    : Array.isArray(data?.categoryBreakdown)
      ? data.categoryBreakdown
      : [];

  return {
    totalSpent: data?.totalSpent ?? data?.total ?? 0,
    topCategory:
      data?.topCategory ??
      (byCategory.length
        ? byCategory.reduce((best, item) =>
            (item.total ?? item.amount ?? 0) > (best.total ?? best.amount ?? 0)
              ? item
              : best
          )
        : null),
    uploadCount: data?.uploadCount ?? data?.pdfUploadCount ?? 0,
    byCategory: byCategory.map((item) => ({
      name: item.categoryName ?? item.name ?? 'Uncategorized',
      total: item.total ?? item.amount ?? 0,
      color: item.colorCode ?? item.color ?? '#64748b',
      count: item.count ?? item.expenseCount ?? 0,
    })),
  };
}

function MetricCard({ label, value, hint }) {
  return (
    <div className="metric-card">
      <p className="text-sm font-medium text-slate-500">{label}</p>
      <p className="mt-2 text-2xl font-bold text-slate-900">{value}</p>
      {hint ? <p className="mt-1 text-xs text-slate-400">{hint}</p> : null}
    </div>
  );
}

export default function Dashboard() {
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [selectedCategoryIds, setSelectedCategoryIds] = useState([]);
  const [summary, setSummary] = useState(null);
  const [expenses, setExpenses] = useState([]);
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [years, setYears] = useState([]);
  const [months, setMonths] = useState(Array.from({ length: 12 }, (_, i) => i + 1));

  // Load years (current year and 5 previous years)
  useEffect(() => {
    const currentYear = now.getFullYear();
    const yearRange = Array.from({ length: 6 }, (_, i) => currentYear - i);
    setYears(yearRange);
  }, []);

  // Load categories
  useEffect(() => {
    let cancelled = false;

    async function loadCategories() {
      try {
        const data = await api.expenses.getCategories();
        if (!cancelled) {
          setCategories(data);
        }
      } catch (err) {
        console.error('Failed to load categories:', err);
      }
    }

    loadCategories();

    return () => {
      cancelled = true;
    };
  }, []);

  // Load summary and expenses when filters change
  useEffect(() => {
    let cancelled = false;

    async function loadData() {
      setLoading(true);
      setError(null);

      try {
        // Load summary
        const summaryData = await api.expenses.getSummary(year, month);
        if (!cancelled) {
          setSummary(normalizeSummary(summaryData));
        }

        // Load filtered expenses
        const expensesData = await api.expenses.getFiltered(year, month, selectedCategoryIds);
        if (!cancelled) {
          setExpenses(expensesData);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load dashboard data.');
          setSummary(null);
          setExpenses([]);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    loadData();

    return () => {
      cancelled = true;
    };
  }, [year, month, selectedCategoryIds]);

  const chartData = useMemo(
    () => summary?.byCategory?.filter((item) => item.total > 0) ?? [],
    [summary]
  );

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="h-8 w-48 animate-pulse rounded-lg bg-slate-200" />
        <div className="grid gap-4 sm:grid-cols-3">
          {[0, 1, 2].map((key) => (
            <div key={key} className="h-28 animate-pulse rounded-xl bg-slate-200" />
          ))}
        </div>
        <div className="grid gap-6 lg:grid-cols-2">
          <div className="h-80 animate-pulse rounded-xl bg-slate-200" />
          <div className="h-80 animate-pulse rounded-xl bg-slate-200" />
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="dashboard-panel border-red-200 bg-red-50 p-6 text-red-800">
        <h2 className="text-lg font-semibold">Unable to load dashboard</h2>
        <p className="mt-2 text-sm">{error}</p>
      </div>
    );
  }

  const isEmpty = !chartData.length && (summary?.totalSpent ?? 0) === 0;
  const selectedCategories = categories.filter(cat => selectedCategoryIds.includes(cat.id));

  return (
    <div className="space-y-6">
      <div className="flex flex-col lg:flex-row lg:items-start lg:justify-between lg:mb-4">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Dashboard</h2>
          <p className="mt-1 text-sm text-slate-500">
            Overview for {new Date(year, month - 1).toLocaleString(undefined, { month: 'long', year: 'numeric' })}
          </p>
        </div>

        <div className="flex flex-wrap gap-3 items-end lg:space-x-4">
          <div className="flex items-center space-x-2">
            <label className="text-sm font-medium text-slate-700">Year:</label>
            <select
              value={year}
              onChange={(e) => setYear(parseInt(e.target.value))}
              className="border rounded px-3 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              {years.map(y => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
          </div>

          <div className="flex items-center space-x-2">
            <label className="text-sm font-medium text-slate-700">Month:</label>
            <select
              value={month}
              onChange={(e) => setMonth(parseInt(e.target.value))}
              className="border rounded px-3 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              {months.map(m => (
                <option key={m} value={m}>
                  {new Date(0, m - 1).toLocaleString(undefined, { month: 'long' })}
                </option>
              ))}
            </select>
          </div>

          <div className="flex items-center space-x-2">
            <label className="text-sm font-medium text-slate-700">Categories:</label>
            <select
              multiple
              value={selectedCategoryIds}
              onChange={(e) => {
                const ids = Array.from(e.target.selectedOptions).map(option => parseInt(option.value));
                setSelectedCategoryIds(ids);
              }}
              className="border rounded px-3 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              size={Math.min(4, categories.length)}
            >
              {categories.map(cat => (
                <option key={cat.id} value={cat.id}>
                  {cat.name}
                </option>
              ))}
            </select>
          </div>

          <button
            onClick={() => {
              setYear(now.getFullYear());
              setMonth(now.getMonth() + 1);
              setSelectedCategoryIds([]);
            }}
            className="btn btn-lg ml-4 bg-blue-600 hover:bg-blue-700 text-white font-bold py-2 px-4 rounded"
          >
            Reset Filters
          </button>
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <MetricCard
          label="Total Spent"
          value={formatCurrency(summary?.totalSpent)}
          hint="Current filter period"
        />
        <MetricCard
          label="Top Category"
          value={summary?.topCategory?.name ?? summary?.topCategory?.categoryName ?? '—'}
          hint={
            summary?.topCategory
              ? formatCurrency(summary.topCategory.total ?? summary.topCategory.amount)
              : 'No spending recorded'
          }
        />
        <MetricCard
          label="Upload Count"
          value={summary?.uploadCount ?? 0}
          hint="PDF statements processed"
        />
      </div>

      {isEmpty ? (
        <div className="dashboard-panel flex flex-col items-center justify-center p-12 text-center">
          <p className="text-lg font-semibold text-slate-800">No expenses yet</p>
          <p className="mt-2 max-w-md text-sm text-slate-500">
            Upload a credit card statement PDF or add expenses to see category breakdowns here.
          </p>
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          <section className="dashboard-panel p-5">
            <h3 className="mb-4 text-lg font-semibold text-slate-900">Spending by category</h3>
            <div className="h-80 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie
                    data={chartData}
                    dataKey="total"
                    nameKey="name"
                    cx="50%"
                    cy="50%"
                    innerRadius={55}
                    outerRadius={95}
                    paddingAngle={2}
                  >
                    {chartData.map((entry) => (
                      <Cell key={entry.name} fill={entry.color} />
                    ))}
                  </Pie>
                  <Tooltip formatter={(value) => formatCurrency(value)} />
                  <Legend />
                </PieChart>
              </ResponsiveContainer>
            </div>
          </section>

          <section className="dashboard-panel p-5">
            <h3 className="mb-4 text-lg font-semibold text-slate-900">Category totals</h3>
            <div className="h-80 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={chartData} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="name" tick={{ fontSize: 12 }} />
                  <YAxis tickFormatter={(value) => `$${value}`} tick={{ fontSize: 12 }} />
                  <Tooltip formatter={(value) => formatCurrency(value)} />
                  <Bar dataKey="total" radius={[6, 6, 0, 0]}>
                    {chartData.map((entry) => (
                      <Cell key={entry.name} fill={entry.color} />
                    ))}
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>
          </section>
        </div>
      )}

      {/* Expenses list section */}
      {!isEmpty && expenses.length > 0 && (
        <section className="dashboard-panel p-5">
          <h3 className="mb-4 text-lg font-semibold text-slate-900">Recent Expenses</h3>
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200">
              <thead className="bg-slate-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase tracking-wider">
                    Date
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase tracking-wider">
                    Description
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase tracking-wider">
                    Amount
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase tracking-wider">
                    Category
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-slate-200">
                {expenses.map((expense) => (
                  <tr key={expense.id} className="hover:bg-slate-50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-900">
                      {new Date(expense.date).toLocaleDateString()}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-800">
                      {expense.description}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-900 font-medium">
                      {formatCurrency(expense.amount)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-600">
                      {expense.category?.name ?? 'Uncategorized'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {expenses.length === 0 && (
            <p className="mt-4 text-center text-slate-500">
              No expenses match the current filters
            </p>
          )}
        </section>
      )}
    </div>
  );
}
