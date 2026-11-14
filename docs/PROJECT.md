# Spence: Project Context & Decisions

Living document. Read it before making product or architecture changes, and append to the **Decision Log** when a decision is made. `CLAUDE.md` covers commands and code layout; this file covers *what we're building, where it stands, and why*.

## 1. Vision

A personal expense tracker that takes no effort to fill and gives insights you'd actually act on.

- **Ingest anything:** PDF statements, CSV exports, receipt photos, manual entry.
- **AI parsing and categorization**, with a review step before anything is saved, and rules that learn from your corrections.
- **Visual dashboard:** where the money went, trends, budgets, recurring charges.
- **AI insights:** a period summary now, "ask your expenses" later.
- **Private by default:** local database, local AI optional.

**Product shape (decided 2026-11-14):** local-first, single user. Grow from there once it works well.

## 2. Current State (2026-11-14, branch `feat/local-first-rewrite`)

Rewritten from the earlier prototype. Everything below works end to end and was verified against a real Ollama model (llama3.2).

| Area | Status |
| --- | --- |
| Import: PDF (text), CSV, images (vision models) | Done. Background queue, chunking, structured output |
| Review screen: edit, untick, duplicates flagged, new categories | Done. Amounts not printed on the statement are flagged and unticked |
| Undo a committed import | Done |
| Learned merchant rules | Done. Created automatically from review corrections (category, or "this is a transfer"), or "always use this category" in the editor |
| Refunds and card payments | Done. `Refund` type nets against spending; card bill payments are transfers |
| Password-protected PDFs | Done. Password asked at upload, used once, never stored |
| Manual add/edit/delete, bulk recategorize/delete, search, filters, CSV export | Done |
| Categories: templates seeded, CRUD, icon, color, monthly budget | Done |
| Dashboard: category ribbon, daily/monthly chart, 12-month trend, budgets, recurring, top merchants, largest | Done |
| AI period summary (aggregates only) | Done |
| Settings: Ollama / Gemini / OpenAI, model, key (encrypted), test connection, currency | Done |
| Tests | 38 xUnit tests: text helpers, date resolution, recurring detection, full import flow with a fake LLM |
| CI | GitHub Actions: build, test, lint, web build |

**Known limits**
- llama3.2 (3B) is fast but makes category and type mistakes. `qwen2.5:7b` or a cloud model is noticeably better. The review step is the safety net.
- Scanned (image-only) PDFs aren't OCR'd. The user is told to upload a photo or screenshot instead.
- Single currency (from Settings). Transactions don't store their own currency.
- The previous prototype's DB (`SpenceAI.WebApi/spence.db`, 87 test rows from the old pipeline) was not migrated. It's left in place, git-ignored.

## 3. Architecture (as built)

- **Ingestion:** upload → `ImportBatch` (Queued) → `ImportWorker` → extract (PdfPig text / CSV text / image) → LLM structured output → resolve dates → match categories → apply rules → fingerprint dedup → staged rows → user review → commit (+ learn rules).
- **AI:** Microsoft.Extensions.AI `IChatClient` for every provider. Insights use computed aggregates only.
- **Storage:** SQLite in `%LOCALAPPDATA%/SpenceAI`; API key encrypted with ASP.NET Data Protection (keys in the same folder).
- **API:** minimal APIs, ProblemDetails errors, enums as strings.
- **Web:** React 19 + TS, TanStack Query, Tailwind 4 design tokens, Recharts, URL-held period state.

## 4. Roadmap (suggested order)

**Next: make it trustworthy for daily use**
1. Golden-file accuracy tests: a folder of anonymized real statements plus expected rows, with a score per model. Run it when prompts or models change.
2. OCR fallback for scanned PDFs: render pages to images and use a vision model.
3. Deterministic CSV import: detect columns (date/description/debit/credit) and skip the LLM for parsing; use AI only for categorization. Faster and exact.
4. Accounts: tag transactions with the card or bank account they came from; filter by account.
5. Backup & restore: one-click export/import of the whole DB file; optional scheduled copy to a synced folder (OneDrive or Dropbox).

**Then: make it insightful**
6. Ask your expenses: chat with tool-calling over safe query functions ("how much on food delivery since June?").
7. Budget alerts and month-end forecast ("at this pace you'll spend $X").
8. Subscription manager: confirm or dismiss detected recurring charges, show yearly cost, flag price increases.
9. Anomaly flags: unusual amount for a merchant, duplicate charges, new merchants.
10. Split transactions (one receipt across categories) and tags (trips, projects).

**Later: grow beyond one device**
11. Desktop packaging (Tauri or a self-contained .NET + static web bundle) so it runs as one app.
12. Optional encrypted cloud sync or a multi-user hosted mode (decision needed; see the log).
13. Multi-currency with stored FX rates.
14. Bank email/SMS parsing (forwarded alerts) for near-real-time entries.

## 5. Decision Log

Append-only. Format: `YYYY-MM-DD: Decision. Why. (Alternatives rejected.)`

- 2026-05-30: .NET Clean Architecture backend + React/Vite frontend in one repo.
- 2026-05-30: PdfPig for PDF text extraction.
- 2026-06: Added SQLite local mode and Ollama alongside Gemini.
- 2026-11-14: **Local-first, single user.** Ship something reliable for one person first. (Multi-user SaaS deferred.)
- 2026-11-14: **SQLite only; Postgres/Neon and the sync stub removed.** One provider means one migration set and no half-built sync. (Runtime-switchable DB and two-way sync rejected as too costly for v1.)
- 2026-11-14: **Microsoft.Extensions.AI** for all providers, with structured output instead of hand-parsed JSON. (Per-provider HTTP clients rejected.) Gemini is used through its OpenAI-compatible endpoint.
- 2026-11-14: **Review before save** for every import, plus batch undo. LLM extraction is never 100% right.
- 2026-11-14: **Learned merchant rules** override the AI. Deterministic, cheaper, and they improve with use.
- 2026-11-14: **Dates resolved server-side** from the printed text (statement-level day/month order). LLMs guess ambiguous dates inconsistently.
- 2026-11-14: **Insights from aggregates, not raw rows.** Accurate numbers, small prompts, works on local models.
- 2026-11-14: **Analytics computed in memory.** SQLite can't aggregate `decimal`; single-user volumes are small.
- 2026-11-14: **API key encrypted with ASP.NET Data Protection** (keys in the data folder). Replaced the hand-rolled AES key derived from the machine name.
- 2026-11-14: Upgraded to **.NET 11 / EF Core 11**. Clears the vulnerable OpenApi and SQLitePCL package warnings.
- 2026-11-14: **Frontend rewritten in TypeScript** with TanStack Query and Tailwind 4 tokens. Hand-built components rather than shadcn, to keep a distinct look and few dependencies. Self-hosted fonts, so it works offline.
- 2026-11-14: Minimal APIs only (MVC controllers removed); repositories removed in favor of `IAppDbContext`.
- 2026-11-14: **`Refund` transaction type.** Credits on a card statement (refunds, reversals, cashback, fee/surcharge waivers) are not income. A refund keeps the spending category of what it reverses and is subtracted from spending in analytics (spending = expenses − refunds). The starter "Refunds" income category was dropped from new installs.
- 2026-11-14: **Card bill payments are transfers on both statements** (the bank debit and the card credit), so they're excluded from spending and income and nothing is double-counted. Card spending comes from the card statement's line items. Rules learn "this merchant is a transfer"; other type changes aren't learned because refunds and purchases share merchants.
- 2026-11-14: **Amounts are checked against the statement text.** If the model's amount isn't printed in the chunk it read (e.g. it merged a purchase and its waiver), the row is flagged and unticked. If every printed occurrence is marked as a credit (`+`, `Cr`), an "expense" becomes a refund. Ground truth beats the model.
- 2026-11-14: **PDFs are read to text at upload.** Lets a locked PDF ask for its password synchronously (error code `pdf_password_required`). The password is used once and never stored; only the extracted text is queued on disk, and it's deleted after processing.
- 2026-11-14: **Duplicate check matches date + amount + description, not type, one saved row per staged row**, and says what it matched ("Already saved from an import on 13 Nov"). Its job is catching re-imported or overlapping statements; a bank CSV and a card PDF don't overlap because the card payment is a transfer.
