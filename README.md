# Spence

A private, local-first expense tracker. Drop in a bank or card statement (PDF or CSV) or a receipt photo; an AI model pulls out each transaction and sorts it into your categories; you check the result and save. The dashboard shows where the month went, tracks budgets and recurring charges, and writes a short summary of what changed.

Your data stays in a SQLite file on your machine. The AI can run locally through [Ollama](https://ollama.com), or you can use Gemini or OpenAI with your own API key.

## Download

**[Download the latest release](https://github.com/bilal-py/spence-ai/releases/latest)** for Windows, macOS (Apple Silicon or Intel) or Linux. Or use the download buttons on the [website](https://bilal-py.github.io/spence-ai/#start), which pick your system for you.

| System | File | First run |
| --- | --- | --- |
| Windows 10/11 | `Spence-win-x64.zip` | Unzip and open `Spence.exe`. If you see "Windows protected your PC", click **More info → Run anyway**. |
| macOS | `Spence-osx-arm64.tar.gz` (Apple Silicon) or `Spence-osx-x64.tar.gz` (Intel) | Unpack it, then in Terminal inside the `Spence` folder run `xattr -dr com.apple.quarantine .` once and open `Spence`. |
| Linux x64 | `Spence-linux-x64.tar.gz` | `tar -xzf Spence-linux-x64.tar.gz && cd Spence && ./Spence` |

The file names carry no version, so `https://github.com/bilal-py/spence-ai/releases/latest/download/Spence-win-x64.zip` always gets the newest release. (v0.1.0 used versioned names.)

Spence opens in your browser at `http://127.0.0.1:5234`, and only this computer can reach it. **On Windows it lives in the tray**, with no console window: click the icon to open Spence, or right-click to quit, open the data folder, start Spence with Windows, or download an update. On macOS and Linux, keep its terminal window open and close it to quit. Opening Spence again while it's running just opens the browser. Then pick an AI provider and currency in **Settings**, go to **Import** and drop a statement.

The apps aren't code-signed yet, which is why Windows and macOS warn you. GitHub Actions builds every release from this repository (`.github/workflows/release.yml`), and each release lists `SHA256SUMS.txt`.

**Updating:** Spence checks GitHub for a new release once a day (a plain request; nothing about you is sent; turn it off with `"Desktop": { "CheckForUpdates": false }`) and shows it in Settings → About and in the Windows tray. Quit Spence, download the new version and replace the folder. Your data isn't in the app folder, so it stays, and Spence backs up the database before an update changes it.

**Logs:** daily logs are in the `logs` folder inside the data folder. If Spence can't start (for example, another program has port 5234), it says why and writes `logs/crash.log`.

**Your data** lives in `%LOCALAPPDATA%\SpenceAI` (Windows), `~/Library/Application Support/SpenceAI` (macOS) or `~/.local/share/SpenceAI` (Linux). To use another folder or port, put an `appsettings.Local.json` next to the executable:

```json
{ "Urls": "http://127.0.0.1:5300", "Storage": { "DataDirectory": "D:/SpenceData" } }
```

For a local AI, install [Ollama](https://ollama.com) and run `ollama pull qwen2.5:7b` (or `llama3.2` for a smaller model). Otherwise use your own Gemini or OpenAI key.

## Build from source

Requirements: .NET 11 SDK and Node 24+.

```bash
# API
cd SpenceAI.WebApi
dotnet run                      # http://localhost:5234

# Web app (second terminal)
cd spence-ai-web
npm install
npm run dev                     # http://localhost:5173
```

In development, data lives in the same folder as above. To use another one, set `Storage:DataDirectory` in `SpenceAI.WebApi/appsettings.Local.json`.

**Package the app yourself:** `bash packaging/publish.sh win-x64 0.2.0` (or `linux-x64`, `osx-arm64`, `osx-x64`; macOS builds must run on a Mac) writes `artifacts/Spence-<rid>.zip` or `.tar.gz`. The app icon comes from `packaging/make-icon.py`. **Release:** push a tag such as `v0.2.0`, and CI builds all four and publishes a GitHub Release.

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

The website lives in `site/index.html`, a single self-contained file (only Google Fonts load from outside). GitHub Pages deploys it from `main` (`.github/workflows/pages.yml`). Its download buttons link to the GitHub release files and switch to the newest release through the GitHub API.

## License

MIT
