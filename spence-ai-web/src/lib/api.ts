// Typed client for the Spence API. Shapes mirror the C# records in SpenceAI.WebApi/Endpoints and SpenceAI.Application.

export const API_URL: string = import.meta.env.VITE_API_URL ?? 'http://localhost:5234/api';

/** Refund = money back for earlier spending (waiver, cashback, return); it reduces spending in its category. */
export type TransactionType = 'Expense' | 'Income' | 'Transfer' | 'Refund';
export type TransactionSource = 'Manual' | 'Pdf' | 'Image' | 'Csv';
export type ImportStatus = 'Queued' | 'Processing' | 'ReadyForReview' | 'Committed' | 'Failed' | 'Discarded';
export type AiProvider = 'Ollama' | 'Gemini' | 'OpenAI';
/** Which way money moved. Rules are learned per direction, so Amazon refunds and Amazon purchases get separate rules. */
export type MoneyDirection = 'Out' | 'In';
export type AccountKind = 'Bank' | 'Card' | 'Cash' | 'Wallet';

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
  accountId: number | null;
  accountName: string | null;
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
  accountId?: number | null;
}

export interface TransactionQuery {
  from?: string;
  to?: string;
  categoryId?: number;
  type?: TransactionType;
  search?: string;
  /** 0 = no account. */
  accountId?: number;
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

export interface Account {
  id: number;
  name: string;
  kind: AccountKind;
  last4: string | null;
  color: string;
  isArchived: boolean;
  transactionCount: number;
}

export type AccountInput = Pick<Account, 'name' | 'kind' | 'last4' | 'color' | 'isArchived'>;

/** One entry read from a quick-add line. Shapes mirror SpenceAI.Application.QuickAdd. */
export interface QuickAddRow {
  text: string;
  date: string;
  amount: number | null;
  type: TransactionType;
  merchant: string;
  categoryId: number | null;
  /** How the category was chosen: a rule you taught, the line naming a category, or a common merchant. */
  categorySource: 'rule' | 'name' | 'keyword' | null;
  accountId: number | null;
  matchedRule: string | null;
  /** Fields worth a second look: 'amount' | 'merchant' | 'account'. */
  unsure: string[];
}

export interface QuickAddSuggestion {
  categoryIds: (number | null)[];
  error: string | null;
}

export type CategoryInput = Pick<Category, 'name' | 'color' | 'icon' | 'kind' | 'monthlyBudget'>;

export interface Rule {
  id: number;
  pattern: string;
  /** null = matches both directions (rules learned before directions existed). */
  direction: MoneyDirection | null;
  categoryId: number | null;
  categoryName: string | null;
  categoryColor: string | null;
  /** The type the rule forces, e.g. 'Transfer' for a card bill payment. */
  type: TransactionType | null;
  updatedAt: string;
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
  /** Set when the row looks wrong, e.g. its amount isn't printed on the statement. */
  warning: string | null;
  /** Why it looks like a duplicate, e.g. "Already saved from an import on 13 Nov". */
  duplicateOf: string | null;
  direction: MoneyDirection | null;
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

/** Manual = "Back up now"; Daily = automatic; BeforeRestore/BeforeUpdate = taken just before data was replaced or migrated. */
export type BackupKind = 'Manual' | 'Daily' | 'BeforeRestore' | 'BeforeUpdate';

export interface BackupFile {
  fileName: string;
  kind: BackupKind;
  createdAt: string;
  sizeBytes: number;
}

export interface BackupStatus {
  directory: string;
  autoBackup: boolean;
  copyFolder: string | null;
  lastBackupAt: string | null;
  backups: BackupFile[];
}

export interface BackupSettingsInput {
  autoBackup: boolean;
  copyFolder: string | null;
}

export interface BackupManifest {
  format: number;
  appVersion: string;
  lastMigration: string | null;
  createdAt: string;
  transactions: number;
  categories: number;
  rules: number;
  firstDate: string | null;
  lastDate: string | null;
}

export interface RestoreResult {
  restored: BackupManifest;
  safetyBackup: string;
}

export interface CommitRow {
  date: string;
  amount: number;
  type: TransactionType;
  description: string;
  merchant: string | null;
  categoryId: number | null;
  newCategoryName: string | null;
  /** The staged row's index, so the server compares the edit with the right suggestion. */
  index: number;
  /** false = "just this once": save the edit but don't learn a rule from it. */
  remember: boolean;
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
  /** Machine-readable reason from ProblemDetails, e.g. 'pdf_password_required'. */
  readonly code: string | null;
  constructor(message: string, status: number, code: string | null = null) {
    super(message);
    this.status = status;
    this.code = code;
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
    let code: string | null = null;
    try {
      const body = await response.json();
      message = body.detail ?? body.title ?? message;
      code = body.code ?? null;
    } catch {
      /* non-JSON error body: keep the generic message */
    }
    throw new ApiError(message, response.status, code);
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
    /** Saves several at once, all or nothing (quick add). */
    createMany: (inputs: TransactionInput[]) => request<Transaction[]>('/transactions/batch', json('POST', inputs)),
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
  accounts: {
    list: () => request<Account[]>('/accounts'),
    create: (input: AccountInput) => request<Account>('/accounts', json('POST', input)),
    update: (id: number, input: AccountInput) => request<Account>(`/accounts/${id}`, json('PUT', input)),
    remove: (id: number) => request<void>(`/accounts/${id}`, { method: 'DELETE' }),
  },
  quickAdd: {
    parse: (text: string, today: string) => request<QuickAddRow[]>('/quick-add/parse', json('POST', { text, today })),
    suggest: (items: { merchant: string; type: TransactionType }[]) => request<QuickAddSuggestion>('/quick-add/suggest', json('POST', { items })),
  },
  rules: {
    list: () => request<Rule[]>('/rules'),
    remove: (id: number) => request<void>(`/rules/${id}`, { method: 'DELETE' }),
  },
  imports: {
    list: () => request<ImportBatch[]>('/imports'),
    get: (id: string) => request<ImportBatch>(`/imports/${id}`),
    /** `password` is only for locked PDFs; the API uses it once to read the file and never stores it. */
    upload: (file: File, password?: string) => {
      const form = new FormData();
      form.append('file', file);
      if (password) form.append('password', password);
      return request<ImportBatch>('/imports', { method: 'POST', body: form });
    },
    /** `accountId` tags every saved row with the account or card the statement is from. */
    commit: (id: string, rows: CommitRow[], accountId: number | null = null) =>
      request<ImportBatch>(`/imports/${id}/commit`, json('POST', { rows, accountId })),
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
  backup: {
    status: () => request<BackupStatus>('/backup'),
    create: () => request<BackupFile>('/backup', { method: 'POST' }),
    updateSettings: (input: BackupSettingsInput) => request<BackupStatus>('/backup/settings', json('PUT', input)),
    downloadUrl: (fileName: string) => `${API_URL}/backup/files/${encodeURIComponent(fileName)}`,
    inspect: (file: File) => request<BackupManifest>('/backup/inspect', { method: 'POST', body: fileForm(file) }),
    restore: (file: File) => request<RestoreResult>('/backup/restore', { method: 'POST', body: fileForm(file) }),
    restoreSaved: (fileName: string) => request<RestoreResult>(`/backup/files/${encodeURIComponent(fileName)}/restore`, { method: 'POST' }),
  },
};

function fileForm(file: File): FormData {
  const form = new FormData();
  form.append('file', file);
  return form;
}
