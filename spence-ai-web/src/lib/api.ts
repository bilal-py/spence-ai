// Typed client for the Spence API. Shapes mirror the C# records in SpenceAI.WebApi/Endpoints and SpenceAI.Application.

export const API_URL: string = import.meta.env.VITE_API_URL ?? 'http://localhost:5234/api';

export type TransactionType = 'Expense' | 'Income' | 'Transfer';
export type TransactionSource = 'Manual' | 'Pdf' | 'Image' | 'Csv';
export type ImportStatus = 'Queued' | 'Processing' | 'ReadyForReview' | 'Committed' | 'Failed' | 'Discarded';
export type AiProvider = 'Ollama' | 'Gemini' | 'OpenAI';

export interface Transaction {
  id: number;
  date: string;
  amount: number;
  type: TransactionType;
  description: string;
  merchant: string | null;
  notes: string | null;
  categoryId: number | null;
  categoryName: string | null;
  categoryColor: string | null;
  categoryIcon: string | null;
  source: TransactionSource;
  importBatchId: string | null;
}

export interface TransactionPage {
  items: Transaction[];
  total: number;
  page: number;
  pageSize: number;
  totalExpense: number;
  totalIncome: number;
}

export interface TransactionInput {
  date: string;
  amount: number;
  type: TransactionType;
  description: string;
  merchant: string | null;
  notes: string | null;
  categoryId: number | null;
  rememberCategory?: boolean;
}

export interface TransactionQuery {
  from?: string;
  to?: string;
  categoryId?: number;
  type?: TransactionType;
  search?: string;
  sort?: 'date' | 'amount';
  dir?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}

export interface Category {
  id: number;
  name: string;
  color: string;
  icon: string;
  kind: TransactionType;
  monthlyBudget: number | null;
  transactionCount: number;
}

export type CategoryInput = Pick<Category, 'name' | 'color' | 'icon' | 'kind' | 'monthlyBudget'>;

export interface Rule {
  id: number;
  pattern: string;
  categoryId: number;
  categoryName: string;
  categoryColor: string;
}

export interface StagedRow {
  index: number;
  date: string;
  amount: number;
  type: TransactionType;
  description: string;
  merchant: string | null;
  categoryId: number | null;
  newCategoryName: string | null;
  isDuplicate: boolean;
  include: boolean;
  matchedRule: string | null;
}

export interface ImportBatch {
  id: string;
  fileName: string;
  source: TransactionSource;
  status: ImportStatus;
  error: string | null;
  transactionCount: number;
  createdAt: string;
  completedAt: string | null;
  rows: StagedRow[] | null;
}

export interface CommitRow {
  date: string;
  amount: number;
  type: TransactionType;
  description: string;
  merchant: string | null;
  categoryId: number | null;
  newCategoryName: string | null;
}

export interface CategoryTotal {
  categoryId: number | null;
  name: string;
  color: string;
  icon: string;
  total: number;
  count: number;
  previousTotal: number;
  monthlyBudget: number | null;
}

export interface Overview {
  from: string;
  to: string;
  spent: number;
  income: number;
  net: number;
  count: number;
  previousSpent: number;
  previousIncome: number;
  dailyAverage: number;
  byCategory: CategoryTotal[];
  daily: { date: string; spent: number }[];
  monthly: { month: string; spent: number; income: number }[];
  topMerchants: { name: string; total: number; count: number }[];
  largest: { id: number; date: string; description: string; amount: number; category: string | null; color: string | null }[];
  recurring: { merchant: string; typicalAmount: number; months: number; lastDate: string; category: string | null }[];
}

export interface Insight {
  headline: string;
  items: { title: string; detail: string; tone: 'good' | 'neutral' | 'watch' }[];
}

export interface Settings {
  aiProvider: AiProvider;
  aiModel: string;
  ollamaUrl: string;
  hasApiKey: boolean;
  currency: string;
}

export interface SettingsInput {
  aiProvider: AiProvider;
  aiModel: string;
  ollamaUrl: string;
  apiKey: string | null;
  currency: string;
}

export class ApiError extends Error {
  readonly status: number;
  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}`, init);
  } catch {
    throw new ApiError("Can't reach the Spence API. Start it with `dotnet run` in SpenceAI.WebApi.", 0);
  }

  if (!response.ok) {
    let message = `Request failed (${response.status}).`;
    try {
      const body = await response.json();
      message = body.detail ?? body.title ?? message;
    } catch {
      /* non-JSON error body: keep the generic message */
    }
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) return undefined as T;
  const type = response.headers.get('content-type') ?? '';
  return (type.includes('json') ? response.json() : undefined) as Promise<T>;
}

const json = (method: string, body: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});

function qs(params: object): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value));
  }
  const s = search.toString();
  return s ? `?${s}` : '';
}

export const api = {
  transactions: {
    list: (q: TransactionQuery) => request<TransactionPage>(`/transactions${qs(q)}`),
    exportUrl: (q: TransactionQuery) => `${API_URL}/transactions/export${qs({ ...q, page: undefined, pageSize: undefined })}`,
    create: (input: TransactionInput) => request<Transaction>('/transactions', json('POST', input)),
    update: (id: number, input: TransactionInput) => request<Transaction>(`/transactions/${id}`, json('PUT', input)),
    remove: (id: number) => request<void>(`/transactions/${id}`, { method: 'DELETE' }),
    bulkCategorize: (ids: number[], categoryId: number | null) =>
      request<{ updated: number }>('/transactions/bulk/categorize', json('POST', { ids, categoryId })),
    bulkDelete: (ids: number[]) => request<{ deleted: number }>('/transactions/bulk/delete', json('POST', { ids })),
  },
  categories: {
    list: () => request<Category[]>('/categories'),
    create: (input: CategoryInput) => request<Category>('/categories', json('POST', input)),
    update: (id: number, input: CategoryInput) => request<Category>(`/categories/${id}`, json('PUT', input)),
    remove: (id: number) => request<void>(`/categories/${id}`, { method: 'DELETE' }),
  },
  rules: {
    list: () => request<Rule[]>('/rules'),
    remove: (id: number) => request<void>(`/rules/${id}`, { method: 'DELETE' }),
  },
  imports: {
    list: () => request<ImportBatch[]>('/imports'),
    get: (id: string) => request<ImportBatch>(`/imports/${id}`),
    upload: (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return request<ImportBatch>('/imports', { method: 'POST', body: form });
    },
    commit: (id: string, rows: CommitRow[]) => request<ImportBatch>(`/imports/${id}/commit`, json('POST', { rows })),
    discard: (id: string) => request<void>(`/imports/${id}`, { method: 'DELETE' }),
  },
  analytics: {
    overview: (from: string, to: string) => request<Overview>(`/analytics/overview${qs({ from, to })}`),
    insights: (from: string, to: string) => request<Insight>('/insights', json('POST', { from, to })),
  },
  settings: {
    get: () => request<Settings>('/settings'),
    update: (input: SettingsInput) => request<Settings>('/settings', json('PUT', input)),
    test: (input: SettingsInput) => request<{ reply: string }>('/settings/test', json('POST', input)),
    ollamaModels: (url: string) => request<string[]>(`/settings/ollama-models${qs({ url })}`),
  },
};
