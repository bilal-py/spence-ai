Spence: a private, local expense tracker
========================================

START
  Windows : double-click Spence.exe
            "Windows protected your PC"? Click "More info" -> "Run anyway".
            (The app isn't code-signed yet.)
  macOS   : first time only, open Terminal in this folder and run
              xattr -dr com.apple.quarantine .
            then double-click Spence (or run ./Spence).
  Linux   : ./Spence

A small window opens and your browser goes to http://127.0.0.1:5234.
Keep the window open while you use Spence. Close it to quit.

YOUR DATA
  Everything stays on this computer, in:
    Windows : %LOCALAPPDATA%\SpenceAI
    macOS   : ~/Library/Application Support/SpenceAI
    Linux   : ~/.local/share/SpenceAI
  Updating Spence never touches it: replace this folder with the new version.
  A backup is made automatically before any update that changes the database.
  Settings -> Backups lets you download a .spence backup. Keep one somewhere safe.

AI
  Importing statements and the AI features need an AI provider. In Settings,
  add a Gemini or OpenAI API key, or point it at Ollama on this computer.
  Your key is stored encrypted on this computer and is only sent to that provider.

OPTIONS
  To change the port or data folder, create appsettings.Local.json next to Spence:
    { "Urls": "http://127.0.0.1:5300", "Storage": { "DataDirectory": "D:\\SpenceData" } }
