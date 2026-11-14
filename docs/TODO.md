# Todo

Working list for Spence. Decisions go in `PROJECT.md` (Decision Log); this file is what's next and ideas still being shaped.

## To do

**Now: finish learning rules**

- [ ] Restart the API so the `DirectionalRules` migration runs
- [ ] Check the review screen in the browser: rule chip, "Remember next time" / "Just this once" toggle
- [ ] Re-import a real statement with an Amazon Pay bill payment and confirm the exception rule
- [ ] Commit the learning change (keep the separate Ollama-context change as its own commit)

**Next: trustworthy for daily use**

- [x] Backup & restore (design below)
- [x] Accounts: tag each transaction with its card or bank account; filter by account
- [ ] Exact CSV import: detect date/description/debit/credit columns; AI only for categories
- [ ] Accuracy tests: anonymized real statements + expected rows, scored per model and prompt
- [ ] Scanned PDFs: render pages to images, read with a vision model

**Then: insights and fun**

- [x] Quick add in plain text (design below)
- [x] Monthly Wrapped (design below)
- [x] Savings pots (brainstorm below)
- [x] Nudges with a pet (brainstorm below)
- [ ] Budget alerts and month-end forecast
- [ ] Subscription manager: yearly cost, price-increase alerts
- [ ] Unusual-charge alerts: odd amounts, double charges, new merchants
- [ ] Ask your expenses: chat over safe query functions
- [ ] Split transactions and tags (trips, projects)

**Later**

- [ ] Desktop app packaging (Tauri, or self-contained .NET + static web)
- [ ] Multi-currency with stored FX rates

## Backup & restore

One file holds everything, restore is one click, and a backup is taken automatically before anything risky.

**What goes in a backup** (a `.spence` file = zip)

- `spence.db`, copied with SQLite `VACUUM INTO`, so it's consistent while the API is running
- `keys/` (Data Protection keys). Without them the saved AI API key can't be decrypted. Restore still works, you just re-enter the key.
- `manifest.json`: app version, last migration id, created at, row counts (shown before restoring)
- Not included: `imports/` (temporary files, deleted after processing)

**API**

- `GET /api/backup`: download a backup now
- `POST /api/backup/restore`: upload a `.spence` file
  1. Validate: real zip, manifest present, DB opens, migration not newer than this app
  2. Refuse while an import is Queued or Processing
  3. Auto-backup the current data first (`backups/before-restore-<time>.spence`)
  4. Swap: clear SQLite connection pools, replace `spence.db` (+ keys), run migrations
  5. Return the manifest summary; the web app reloads

**Automatic backups**

- [x] Hourly check; if the last backup is over 24 h old, write one to `<data dir>/backups/`, keeping the last 7 of each kind
- [x] Before every restore and every schema update
- [x] Optional extra folder (e.g. OneDrive/Dropbox) in Settings

**Settings UI**

- [x] "Back up now", "Restore from a file", last backup time, auto-backup on/off, extra folder path, saved backups list with download/restore

**Open questions**

- Encrypt backups with a passphrase? Recommended when the extra folder is cloud-synced, because then the data leaves the device.
- Restore on a different machine: test whether the Data Protection keys decrypt there; if not, ask for the API key again.

## Quick add in plain text

Type one line, Spence shows a confirm card, Enter saves it. Works offline without the AI for most input.

**Input** (the **N** key or a command bar)

- `chai 40 upi` → Expense, 40, Chai, Dining, today
- `uber 250 yesterday` → Expense, 250, Uber, Transport, yesterday
- `got 80k salary` → Income, 80,000, Salary
- `amazon refund 499` → Refund, 499, Shopping
- `chai 40, auto 120` → two rows on one card

**Parsing order**

1. Rules first: amount (`40`, `1.2k`, `₹499`), date words (today, yesterday, mon, 12 nov), type words (got, received → Income; refund, cashback → Refund; paid card bill → Transfer), account words (upi, card, cash)
2. Merchant → learned rule → category (same `RuleBook` as imports)
3. AI only for what's still unknown (category of a new merchant), with the line as the only input

**Confirm popup**

- One card per row: amount, merchant, category, date, type and account as chips you click to change
- Enter = save all, Esc = cancel, Tab moves between chips
- Changing a category shows the same "Remember next time / Just this once" toggle as import review
- Low-confidence chips are highlighted, so you know what to check

## Monthly Wrapped

A short, swipeable recap at month end (and a yearly one), made locally from aggregates.

**Cards**

- Total spent vs last month (up/down %)
- Top category and top merchant ("Swiggy: 23 orders, ₹6,840")
- Biggest spending day, and the number of no-spend days
- Budgets kept vs blown
- Savings pot progress
- A "spending personality" from simple rules (e.g. "Weekend spender": over 50% of spending falls on Sat/Sun)

**Sharing**

- [ ] Export a card as PNG
- [ ] "Hide amounts" toggle that shows % and ranks only, for sharing on social apps
- [ ] Optional one-line AI caption from aggregates only (never rows)

Note: transactions only have dates, not times, so no "late-night orders" cards for now.

## Savings pots

Pots are named goals you fill. Spence can't move money, so a pot is an earmark plus a tracker.

**Basics**

- Name, emoji, goal amount, optional deadline: "Goa trip · ₹20,000 · by 1 Mar"
- Progress ring, plus the pace needed: "₹1,250/week to make it", "At this pace: 12 Feb"

**Ways money gets into a pot**

- Manual: "Add ₹500 to Goa trip"
- Linked transfer: a transfer to your savings account counts toward a pot (needs Accounts)
- Round-ups: each expense rounded up to ₹10/₹50; the difference is a virtual contribution, which you move for real when you like
- Leftover budget: unspent category budget at month end goes into a pot, with one tap to confirm
- No-spend day bonus: a fixed amount you set, added for each no-spend day

**Fun layer**

- Challenges: 52-week challenge, "no Swiggy week", "save ₹100 a day for 30 days"
- Milestones at 25/50/75/100% unlock pet accessories (see nudges)
- Completed pots move to a "Done" shelf, with the date and how long they took

**Open questions**

- Virtual vs real money: show both "earmarked" and "actually moved"?
- Should pot contributions count as spending? Proposal: no, they're transfers.

## Nudges with a pet

A small, original mascot on the dashboard whose mood shows how the month is going. It nudges, never nags.

**Mood** (rule-based, from aggregates)

- Happy: under budget pace
- Curious: a new merchant, or an unusual charge to check
- Worried: a category is at 80%+ of its budget before the 20th
- Sleepy: nothing logged or imported for 7+ days
- Party: a pot milestone or a no-spend streak

**Nudges**

- One line, at most one a day, specific and kind: "Swiggy's at ₹3.2k, your usual is ₹2k by now"
- Rules and templates make them work without AI. A local model can add flavor to the wording if one is set up.
- Tap the pet for today's nudge and a quick action (open the category, add to a pot, quick add)

**Growth**

- Streaks (logging streak, no-spend days) and pot milestones unlock accessories and small animations
- No punishment: the pet never "dies" or gets sad for long; worry clears once you've looked at the issue

**Settings**

- Pet on/off, nudge frequency, tone (chill / direct)
- Desktop notifications later (with the desktop app)

**Open question**

- Character design: needs our own original look, name and art (SVG + a few CSS animations).

## Ideas

- Screenshot import: share a GPay/PhonePe payment screenshot to Spence (installable PWA with a share target)
- Splits with friends: a simple "who owes whom", settled by a transfer
- Privacy badge: "Your data never leaves this device" when using a local model
- Receipt gallery: photos attached to transactions
- Themes and sticker packs for categories
- Bank SMS/email alerts as near-real-time entries
- Optional encrypted sync between your own devices
