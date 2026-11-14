# CLAUDE.md

Spence is a **local-first, single-user** expense tracker. You import statements (PDF, CSV, receipt photos) or add expenses by hand; an LLM extracts and categorizes them; you review before saving; the dashboard and AI summaries show where the money went.

**Read `docs/PROJECT.md` first.** It holds the vision, current state, roadmap and the decision log. Append to the Decision Log whenever a product or architecture decision is made.

## Commands

```bash
# Backend (.NET 11), from repo root
dotnet build SpenceAI.slnx
dotnet test SpenceAI.slnx
cd SpenceAI.WebApi && dotnet run          # http://localhost:5234

# Migrations (DbContext in Infrastructure, startup project WebApi)
cd SpenceAI.WebApi
dotnet ef migrations add <Name> --project ../SpenceAI.Infrastructure --startup-project . --output-dir Data/Migrations

# Frontend (React 19 + TS + Vite + Tailwind 4), from spence-ai-web/
npm install
npm run dev          # http://localhost:5173
npm run lint
npm run build        # tsc -b && vite build
```

Migrations run automatically on API startup. Starter categories are seeded when the DB has none.

## Architecture

`WebApi → Infrastructure → Application → Domain`, plus `SpenceAI.Tests` (xUnit).

- **Domain** (`Entities.cs`): `Transaction` (amount always positive; `Type` = Expense/Income/Transfer/Refund; spending = expenses − refunds, transfers count as neither), `Category` (with `Kind`, `Icon`, optional `MonthlyBudget`), `Account` (Bank/Card/Cash/Wallet; optional `Transaction.AccountId`), `SavingsPot` (+ `PotContribution`, `PotChallenge`; linked transfers are computed live, round-ups/leftover/no-spend are claimed with "claimed through" markers), `MerchantRule` (pattern + money `Direction` → category and/or `Type`; longest pattern wins, see `Imports/RuleBook.cs`), `ImportBatch`, `AppSettings` (single row).
- **Application**: `Abstractions.cs` (interfaces + `AppException`), `Text.cs` (`MerchantKey` normalizer, `TransactionFingerprint`), and feature folders: `Imports/` (pipeline), `QuickAdd/` (one-line parser + AI category suggestions), `Savings/` (pots, pace, claims, challenges), `Pet/` (mood + one nudge a day, rules only), `Analytics/` (overview, Wrapped, AI insights), `Settings/`, `Categories/` (templates/seed).
- **Infrastructure**: `AppDbContext` (SQLite), `Services.cs` (Data Protection secret protector, `ChatClientFactory`, PdfPig extractor, import file store), `ImportWorker.cs` (channel queue + `BackgroundService`), `BackupService.cs` (backup/restore + daily `BackupScheduler`), `DependencyInjection.cs` (`AddSpenceAi`).
- **WebApi**: minimal APIs only, in `Endpoints/`. `Program.cs` maps `AppException` → ProblemDetails 4xx; everything else → generic 500 (no stack traces to clients).

### Storage

Everything lives in one data directory: `%LOCALAPPDATA%/SpenceAI` by default, or override it with `Storage:DataDirectory`. That directory holds `spence.db` (SQLite), `keys/` (Data Protection keys, which encrypt the stored AI API key), `imports/` (uploaded files, deleted once processed; a PDF is stored as its extracted text, never the PDF itself) and `backups/` (`.spence` zips from `BackupService`: DB snapshot via `VACUUM INTO` + keys + manifest). Machine-specific config goes in `SpenceAI.WebApi/appsettings.Local.json` (git-ignored).

SQLite can't aggregate or ORDER BY `decimal`, so analytics and amount sorting are done **in memory** after filtering by date. That's fine for one user's data. Don't "optimize" it back into SQL.

### Import pipeline (`Imports/ImportService.cs`)

`POST /api/imports` saves the file and an `ImportBatch` (Queued), then enqueues it. `ImportWorker` processes one batch at a time:
1. Extract: PDFs are read to page text (PdfPig) at upload, so a locked PDF returns a 400 with `code: pdf_password_required` and the client resends with a `password` form field (used once, never stored). PDF pages or CSV text are chunked to ~9k chars (the CSV header repeats in each chunk). Images go to the model as `DataContent` (this needs a vision model).
2. `TransactionExtractor` calls `IChatClient.GetResponseAsync<ExtractionResult>` (structured output). **Every property on the extraction DTOs must be `required`**, or the JSON schema has no `required` list and Ollama returns `{}`.
3. `ResolveDates` picks day-first or month-first per statement from the printed dates (a part >12, then the tightest date span, then machine culture). Don't trust the model's own `Date` for numeric formats.
4. `AmountCheck` compares each amount with the chunk text the model read: not printed → `Warning` and unticked (catches merged lines); printed only as a credit (`+`, `Cr`) → an expense becomes a `Refund`.
5. Categories are matched by name. Unknown names become `NewCategoryName` and are created only on commit. A `MerchantRule` (matched on `MerchantKey`) overrides the AI's category and, for transfers, its type. Transfers get no category.
6. Fingerprints are compared with existing transactions in the date range, so duplicates are flagged and unticked.
7. Rows are staged as JSON on the batch (`ReadyForReview`). `POST /api/imports/{id}/commit` creates the transactions and **learns rules** wherever the user changed the suggested category or marked a row as a transfer. `DELETE /api/imports/{id}` discards a pending batch or undoes a committed one.

### AI providers

`ChatClientFactory` maps the `AppSettings.AiProvider` value: Ollama (OllamaSharp, 10 min timeout), OpenAI, or Gemini (through Gemini's OpenAI-compatible endpoint). All use Microsoft.Extensions.AI `IChatClient`. To add a provider, add an enum value and a switch arm, and add it to `PROVIDERS` in `spence-ai-web/src/pages/Settings.tsx`. Insights (`Analytics/InsightService.cs`) send **pre-computed aggregates** to the model, never raw rows.

### Frontend (`spence-ai-web/src`)

- `lib/api.ts` is the typed client. Its shapes mirror the C# records, and enums are serialized as strings (`"Expense"`). Keep them in sync.
- `lib/hooks.ts` holds the TanStack Query keys. `useInvalidateMoney()` after any write. The selected period lives in the URL (`?p=2026-11` or `?p=2026`).
- `components/QuickAdd.tsx` is the one-line add dialog (the **N** key and the Add button open it); `components/TransactionEditor.tsx` is the full add/edit form (`useTransactionEditor().open(tx?, draft?)`).
- `pages/Pots.tsx` (savings pots), `pages/Wrapped.tsx` + `lib/wrapped.ts` (one card model renders the page and the canvas PNG), `components/Pet.tsx` (Sprig SVG, Overview strip, Settings section).
- `components/CategorySelect.tsx` ends with "+ New category…", which opens `CategoryCreator` (shared `CategoryForm`) and selects the result.
- The design system lives in `index.css`: CSS variables for light and dark under Tailwind 4 `@theme inline` (paper, surface, ink, pine, ochre, brick). Fonts are Bricolage Grotesque (display) and Instrument Sans (UI), self-hosted with `@fontsource`. Use `.num` for money (tabular figures), `.field` and `.label` for inputs. Don't introduce cards everywhere or all-caps labels.
- Only `vite.config.ts` should exist. A stray `vite.config.js` silently takes precedence and drops the Tailwind plugin.
