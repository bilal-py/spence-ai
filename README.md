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

- Import PDF statements, CSV exports and receipt photos (photos need a vision model). Every import is reviewed before saving, and it can be undone later.
- Possible duplicates are flagged when you import the same statement twice.
- Spence learns your categories: change one during review and it's remembered for that merchant.
- Add and edit transactions by hand (press **N** anywhere). Bulk recategorize or delete, search, filter, and export to CSV.
- Dashboard: monthly ribbon of spending by category, daily and 12-month trends, budgets per category, recurring charges, top merchants.
- AI summary of any month or year, built from computed totals so the numbers are exact.
- Light and dark themes.

## Stack

.NET 11 minimal APIs with EF Core and SQLite, Microsoft.Extensions.AI (Ollama, Gemini, OpenAI), and PdfPig. React 19, TypeScript, Vite, Tailwind 4, TanStack Query, and Recharts. Tests use xUnit.

See `docs/PROJECT.md` for the roadmap and design decisions.

## License

MIT
