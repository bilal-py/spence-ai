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
  const [year] = useState(now.getFullYear());
  const [month] = useState(now.getMonth() + 1);
  const [summary, setSummary] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    let cancelled = false;

    async function loadSummary() {
      setLoading(true);
      setError(null);

      try {
        const data = await api.expenses.getSummary(year, month);
        if (!cancelled) {
          setSummary(normalizeSummary(data));
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load dashboard data.');
          setSummary(null);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    loadSummary();

    return () => {
      cancelled = true;
    };
  }, [year, month]);

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

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-slate-900">Dashboard</h2>
        <p className="mt-1 text-sm text-slate-500">
          Overview for {now.toLocaleString(undefined, { month: 'long', year: 'numeric' })}
        </p>
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
    </div>
  );
}
