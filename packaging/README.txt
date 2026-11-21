Spence: a private, local expense tracker
========================================

START
  Windows : double-click Spence.exe
            "Windows protected your PC"? Click "More info" -> "Run anyway".
            (The app isn't code-signed yet.)
            Spence opens in your browser and sits in the tray, near the clock
            (it may be under the ^ arrow). Click the icon to open Spence again.
            Right-click it to quit, open your data folder, or start Spence
            with Windows.
  macOS   : first time only, open Terminal in this folder and run
              xattr -dr com.apple.quarantine .
            then double-click Spence (or run ./Spence).
            Keep the Terminal window open while you use it. Close it to quit.
  Linux   : ./Spence      (Ctrl+C to quit)

Spence runs at http://127.0.0.1:5234 and only this computer can reach it.
Opening Spence again while it's running just opens the browser.

YOUR DATA
  Everything stays on this computer, in:
    Windows : %LOCALAPPDATA%\SpenceAI
    macOS   : ~/Library/Application Support/SpenceAI
    Linux   : ~/.local/share/SpenceAI
  Backups are in its "backups" folder and logs in "logs".
  Settings -> Backups lets you download a .spence backup. Keep one somewhere safe.

UPDATES
  Spence checks GitHub for a new version once a day (nothing about you is sent)
  and tells you in Settings and, on Windows, in the tray.
  To update, quit Spence, download the new version and replace this folder.
  Your data isn't in this folder, so it stays. A backup is made before any
  update that changes the database.

AI
  Importing statements and the AI features need an AI provider. In Settings,
  add a Gemini or OpenAI API key, or point it at Ollama on this computer.
  Your key is stored encrypted on this computer and is only sent to that provider.

OPTIONS
  Create appsettings.Local.json next to Spence to change things, for example:
    { "Urls": "http://127.0.0.1:5300",
      "Storage": { "DataDirectory": "D:/SpenceData" },
      "Desktop": { "CheckForUpdates": false } }

SOMETHING WRONG?
  If Spence can't start, it tells you why and saves the details to
  logs/crash.log in your data folder. Daily logs are in the same folder.
