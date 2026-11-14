# Spence

A private, local-first expense tracker. Drop in a bank or card statement (PDF or CSV) or a receipt photo; an AI model pulls out each transaction and sorts it into your categories; you check the result and save. The dashboard shows where the month went, tracks budgets and recurring charges, and writes a short summary of what changed.

Your data stays in a SQLite file on your machine. The AI can run locally through [Ollama](https://ollama.com), or you can use Gemini or OpenAI with your own API key.

## Run it

Requirements: .NET 11 SDK, Node 24+, and optionally Ollama (`ollama pull llama3.2`, or `qwen2.5:7b` for better accuracy).

```bash
# API
cd SpenceAI.WebApi
dotnet run                      # http://localhost:5234

# Web app (second terminal)
cd spence-ai-web
npm install
npm run dev                     # http://localhost:5173
```

Open the web app and pick your AI provider and currency in **Settings**. Then go to **Import** and drop a statement.

Data lives in `%LOCALAPPDATA%/SpenceAI`. To use another folder, set `Storage:DataDirectory` in `SpenceAI.WebApi/appsettings.Local.json`.

## Features

**Getting money in**
- **Import statements**: PDF (including password-protected ones), CSV exports and receipt photos (photos need a vision model). Every import is reviewed before saving and can be undone later. Duplicates are flagged when you import the same statement twice.
- **Quick add**: press **N** anywhere and type `chai 40 upi, uber 250 yesterday, got 80k salary`. Each entry becomes a card with amount, merchant, category, date and account; press Enter to save them all. Parsing is plain rules, so it works offline; the AI is only asked about categories it can't work out.
- **It learns**: change a category once (in review or quick add) and it's remembered for that merchant. "Just this once" skips learning.
- **Accounts**: tag transactions with the bank account, card, wallet or cash they went through, then filter and export by account.

**Understanding it**
- **Dashboard**: a ribbon of the month by category, daily and 12-month trends, budgets, recurring charges, top merchants and the biggest purchases.
- **AI summary** of any month or year, built from computed totals so the numbers are exact.
- **Monthly Wrapped**: recap cards for any month (top category, your regular spot, no-spend streaks, biggest day, new places, budgets kept). Save one card or all of them as a PNG, with a **Hide amounts** switch so you can share it safely.

**Saving**
- **Savings pots**: a goal, an optional deadline and the pace you need (per month and per week, with ahead/behind). Fill a pot by hand, from linked transfers (like a recurring deposit), round-ups on your spending, leftover budget, a bonus per no-spend day, or challenges like "skip Dining for two weeks".
- **Sprig**, a small pet on the dashboard. Its mood follows how the month is pacing, and it offers at most one short nudge a day: a reward waiting, a budget getting close, a subscription renewing tomorrow. Rule-based, never guilt-trips, and can be renamed or hidden.

**Yours**
- Everything is stored in one SQLite file on your machine. Daily backups, one-click backup and restore, and an optional copy to a folder such as OneDrive.
- Search, filter, bulk recategorize or delete, and export to CSV.
- Light and dark themes, keyboard friendly, works on a phone-sized screen.

## Stack

.NET 11 minimal APIs with EF Core and SQLite, Microsoft.Extensions.AI (Ollama, Gemini, OpenAI), and PdfPig. React 19, TypeScript, Vite, Tailwind 4, TanStack Query, and Recharts. Tests use xUnit.

See `docs/PROJECT.md` for the roadmap and design decisions.

A one-page product overview for a website lives in `site/index.html`. It's a single self-contained file (only Google Fonts load from outside), so you can upload it as-is.

## License

MIT
