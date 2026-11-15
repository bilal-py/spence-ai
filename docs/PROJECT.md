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
| Learned merchant rules | Done. Learned from review corrections (any type or category change, per money direction), with "just this once" to skip; overriding a rule on one row adds a specific exception; rules for merchants in a chunk are sent to the model as hints. Also "always use this category" in the editor |
| Refunds and card payments | Done. `Refund` type nets against spending; card bill payments are transfers |
| Password-protected PDFs | Done. Password asked at upload, used once, never stored |
| Manual add/edit/delete, bulk recategorize/delete, search, filters, CSV export | Done |
| Categories: templates seeded, CRUD, icon, color, monthly budget | Done |
| Dashboard: category ribbon, daily/monthly chart, 12-month trend, budgets, recurring, top merchants, largest | Done |
| AI period summary (aggregates only) | Done |
| Settings: Ollama / Gemini / OpenAI, model, key (encrypted), test connection, currency | Done |
| Backup & restore | Done. One `.spence` file (DB + keys + manifest), daily automatic backups (7 kept per kind), a backup before every restore and schema update, optional copy folder, restore from file or from the saved list (Settings) |
| Accounts | Done. Bank / card / wallet / cash accounts (Settings), tag on manual add, quick add or a whole import at review; filter and export by account |
| Quick add | Done. One line ("chai 40 upi, uber 250 yesterday") → confirm cards → Enter. Rules first, AI only for unknown categories |
| Savings pots | Done. Goal, deadline, pace (per month/week, ahead/behind). Filled by hand, linked transfers (live), round-ups, leftover budget, no-spend bonuses and challenges (skip a category, N no-spend days) |
| Monthly Wrapped | Done. `/wrapped?m=2026-10`: recap cards, PNG per card or all-in-one, "Hide amounts" swaps money for counts and shares |
| Pet | Done. Sprig on the Overview: mood from the month's pace, at most one rule-based nudge a day, dismiss until tomorrow; rename or hide in Settings. Wears outfits unlocked by side-quest levels |
| Bills & subscriptions | Done. `/bills`: weekly/monthly/yearly bills (every N), end date for EMIs, expense or transfer. Paid status worked out live from matching transactions; "Paid" logs the transaction (undo deletes it); skip a date. Month strip, coming-up list, on-time streaks, price-change alerts, setup from spotted repeat charges, subscription swipe check (keep/cut) with yearly cost and "charged after you cancelled" alerts, `.ics` calendar file, browser reminders while open, Sprig nudges. Quick add: "netflix 649 monthly on 5th" makes a bill |
| Safe to spend | Done. Overview card: what's left in the budgeted categories (or the usual month) after spending and bills still due |
| Streaks & side quests | Done. Check-in, no-spend, light-week and bills-on-time streaks; three rule-based quests a week; XP levels (Seedling → Old oak) unlock pet outfits |
| Ask your expenses | Done. `/ask`: chat where the model calls read-only query tools (totals, ≤25 rows, categories, bills, pots) and the UI lists what it looked up |
| Celebrations | Done. Dependency-free confetti + haptic buzz for pot milestones and goals, challenge wins, bills paid (all of a month's bills: a big one), quests, level-ups and subscription cuts; skipped with reduced motion |
| Tests | xUnit tests: text helpers, date resolution, recurring detection, full import flow with a fake LLM |
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
4. ~~Accounts~~ (done 2026-11-14).
5. ~~Backup & restore~~ (done 2026-11-14).

**Then: make it insightful**
6. ~~Ask your expenses~~ (done 2026-11-15).
7. Budget alerts and month-end forecast ("at this pace you'll spend $X"). Safe-to-spend covers part of this.
8. ~~Subscription manager~~ (done 2026-11-15, as part of Bills).
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
- 2026-11-14: **Rules are scoped by direction and learn any type change.** Supersedes "only transfers are learned": refunds and purchases share merchants, but never a direction, so "Amazon credits are refunds" can't touch Amazon purchases. (Learning every type change on the merchant alone rejected: it broke purchases.)
- 2026-11-14: **Two keys per row, longest rule wins.** General key = 2 words of the merchant ("AMAZON PAY"); specific key = 4 words of the printed description ("AMAZON PAY AMAZONPAYBILL"). New rules go on the general key; overriding a rule on one row adds an exception on the specific key; if every row a rule decided gets the same fix, the rule itself is updated. The key drops ref numbers/hashes (3+ digits), rail words (UPI, NEFT, "Request fr") and bank names, but keeps a UPI handle's name, which identifies the payee best. (Always learning on the long key rejected: rules would rarely match again.)
- 2026-11-14: **Rule hints in the prompt only for merchants already in the chunk.** A rule's pattern is sent only if all its words appear in the text being sent, so hints add no data the statement wasn't already sending to a cloud provider. Rules still override the AI after extraction; hints help near-misses.
- 2026-11-14: **Ollama requests ask for a 16k context (`num_ctx`).** Ollama defaulted qwen2.5:7b to 4096 tokens and silently cut the start of any longer prompt (the instructions and most of the statement), so a bank statement came back with only its last 7 rows. A reply cut off for length is now an error, not a partial import.
- 2026-11-14: **Backups are a zip of a `VACUUM INTO` copy + Data Protection keys + manifest.** Consistent while the app runs, one file to move around. Keys are included so the saved API key survives a restore on the same Windows user (they're DPAPI-bound, so on another machine the key is asked for again). A restore validates the file (zip, manifest, integrity check, not from a newer schema), refuses during an import, backs up the current data first, then swaps the DB file and migrates. Backups aren't encrypted yet; a passphrase is an open question for synced folders. (Copying the live .db file rejected: can be torn mid-write.)
- 2026-11-14: **Accounts are optional tags, set per transaction or per import.** An import is tagged at review ("Statement from"), not at upload, so nothing in the queue/worker changes. Deleting an account untags its transactions; archiving hides it from pickers. Analytics don't split by account yet. (A required account per transaction rejected: friction for cash and old data.)
- 2026-11-14: **Quick add parses with rules on the server, AI only for unknown categories.** `QuickAddParser` reads amount (40, 1.2k, ₹499, 2.5L), date words, type words (got/refund/card bill) and account words (name, cash/card/upi); categories come from learned rules, then the line naming a category, then a small built-in merchant list, then `/quick-add/suggest`, which sends only merchant names and never fails the card. Saving uses `POST /transactions/batch` (all or nothing). The N key now opens quick add; the full form is one click away. (Client-side parsing rejected: untestable in xUnit and would duplicate `RuleBook`.)
- 2026-11-14: **Every category picker ends with "+ New category…"**, which opens the category form over the current dialog and selects the new category.
- 2026-11-15: **Savings pots are earmarks, not accounts.** Spence can't move money, so a pot tracks it. Linked transfers are computed live from Transfer rows matching the pot's text (deleting the transaction updates the pot). Round-ups, leftover budget and no-spend bonuses are *offered* and become stored contributions only when the user taps "Move in"; each source keeps a "claimed through" date so nothing counts twice. Round-ups and leftover budget belong to one pot at a time (they're the same money). No-spend days only count up to the latest transaction date, so a gap in imports isn't mistaken for saving. Pot money isn't spending. (Auto-crediting round-ups rejected: the pot would show money that never moved.)
- 2026-11-15: **Wrapped is computed facts drawn on a plain canvas.** No AI, no time-of-day cards (dates only). One card model drives the page and the PNG (`lib/wrapped.ts`), with a fixed dark palette so shared images look the same in either theme. "Hide amounts" removes every money figure, leaving counts, shares and names. (html2canvas rejected: a dependency and flaky font capture.)
- 2026-11-15: **The pet is rules only and never scolds.** Mood = this month's net spending vs a prorated baseline (total budgets, else the last 3 months' average): cheerful / content / watchful, plus proud (a pot done or a challenge won) and sleepy (nothing in 30 days). One nudge a day by priority: won challenge → money ready to move in → budget at 80–99% with 5+ days left → recurring charge due in 3 days → nothing logged for a week → no-spend streak → pot behind pace. Dismissing stores the day in settings.
- 2026-11-15: **Every dialog closes on Escape explicitly** (keydown + cancel handlers), instead of relying on the native `<dialog>` cancel, which Chrome can swallow.
- 2026-11-15: **Phone bottom bar keeps five items** (Overview, Transactions, Pots, Import, Settings). Wrapped is linked from the Overview of a finished month and Categories from Settings.
- 2026-11-15: **Bills are schedules; paid status is computed, not stored.** A due date counts as paid when an Expense or Transfer whose merchant/description contains all the bill's words lands within a window of it (weekly −2/+3 days, monthly ±10, yearly ±30; fixed amounts within 0.5×–2× so a price rise still matches, then gets flagged). The closest unused transaction wins, so one charge never pays two dates. Only what the user says by hand is stored (`BillMark`: paid with the transaction Spence logged, or skipped); deleting that transaction cascades the mark away, so the bill goes back to unpaid. A past date with no payment is "late" only once the data reaches 3 days past it, else "unconfirmed" (an import gap isn't a missed bill). Matching uses all letters-only words, bank names included, unlike merchant keys. (Storing a status per month rejected: it goes stale when imports arrive or transactions are deleted.)
- 2026-11-15: **Card bills, SIPs and EMIs can be Transfer bills.** They're tracked and reminded like any bill but never lower safe-to-spend, matching "card bill payments are transfers".
- 2026-11-15: **Safe to spend is budget-scoped when budgets exist.** Budgets − spending in budgeted categories − unpaid expense bills in those categories. Without budgets: the last three months' average − all spending − all unpaid expense bills. (Total budgets − all spending rejected: rent with no budget made it hugely negative.)
- 2026-11-15: **Bill reminders without a server push.** In-app (Overview "Coming up", Sprig's nudge, browser notifications at most once a day while a tab is open, opt-in) plus a downloadable `.ics` with one RRULE event and alarm per bill, so a phone calendar reminds even when Spence is closed. (Web Push rejected for now: needs a service worker and a push service, and the API is local.)
- 2026-11-15: **Subscription check is swipe keep/cut.** Cutting sets `CancelledOn` (Spence can't cancel anything); the bill stops reminding, its yearly cost counts toward "back in your pocket", and a matching charge after cancelling is flagged.
- 2026-11-15: **Side quests are rules over local data, three a week, stable all week.** Picked by a hash of the week from those that fit the data (check-ins, no-spend days, a frequent treat category, light days, feeding a pot, bills on time, everything categorized, subscription check). A finished quest is recorded in `QuestCompletion` the first time it's seen done (last week is settled too), so XP never goes backwards when data changes; the GET writes, which is fine for a single local user. Levels unlock pet outfits; the choice is stored in settings ("auto" = best unlocked).
- 2026-11-15: **Ask uses tool calling over read-only functions.** `FunctionInvokingChatClient` (max 6 rounds) with `spending_summary`, `list_transactions` (≤25 rows), `list_categories`, `bills_overview`, `savings_pots`. The model sees only what a question needs (rows only when it asks for a list), can't write, and every lookup is listed under the answer. The chat lives in the browser (sessionStorage); the server is stateless and gets the last 12 messages. Small local models may skip tools; a 7B+ model or a cloud model works best.
- 2026-11-15: **Celebrations are hand-rolled canvas confetti + `navigator.vibrate`.** Fired from the action's button where possible, once per event (quests via `JustCompleted`, levels via a stored last level, pots by watching progress cross 25/50/75/100%). Reduced motion skips the confetti. (canvas-confetti rejected: a dependency for ~150 lines.)
- 2026-11-15: **Phone bottom bar: Overview, Transactions, Bills, Pots, Import.** Ask and Settings moved to icons in the phone header. Supersedes the earlier five.
- 2026-11-15: **A level that unlocks an outfit puts it on the pet.** When new quest XP crosses an unlocking level, `PetAccessory` resets to auto (best unlocked) unless the user chose "none", and the client refetches the pet after quest XP changes. (Keeping a hand-picked older outfit rejected: the unlock went unnoticed.)
- 2026-11-15: **Dialogs focus their first text field after `showModal()`.** React's `autoFocus` runs while the `<dialog>` is still closed, so the browser focused the close button instead (N opened quick add without a cursor).
