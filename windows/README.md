# Whisper for Windows (WhisperWin)

C# / .NET 8 / WPF sibling of the macOS Whisper dictation app. Hold **Right Ctrl** → speak →
Groq STT → LLM correction → pastes into whatever app has focus. Shares its correction prompt
and custom dictionary with the Mac app via `../shared/`.

## Prerequisites

- .NET 8 SDK (`dotnet --version` should print `8.x`)
- Windows 10/11
- A Groq API key (https://console.groq.com) — same key is used for transcription and correction

## Build

```powershell
cd windows
dotnet build WhisperWin.sln -c Release
```

## Run (dev loop)

```powershell
cd windows\WhisperWin
dotnet run
```

On first launch, right-click the tray icon → **Settings** → paste your Groq API key → **Test Key**
→ **Save**. Then hold Right Ctrl anywhere to dictate.

## Test

```powershell
cd windows
dotnet test WhisperWin.sln
```

Unit tests cover the hotkey hold/double-tap/toggle state machine, correction-prompt assembly
(dictionary + language-hint injection), `shared/` path resolution, config round-trip, and
transcript sound-annotation stripping — all without touching Win32, so they run on any machine
with the .NET 8 SDK.

## Publish a self-contained single-file exe

```powershell
cd windows\WhisperWin
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `windows\WhisperWin\bin\Release\net8.0-windows\win-x64\publish\WhisperWin.exe`.
This is a single file with no .NET runtime dependency on the target machine; `shared/` is
copied alongside it automatically (see `WhisperWin.csproj`).

## Notes / current limitations (Phase 1)

- Settings are stored at `%APPDATA%\WhisperWin\config.json`; the API key itself lives in
  Windows Credential Manager (target name `WhisperWin:GroqApiKey`), never on disk as plain text.
- No Dictionary editor UI or History yet — those are Phase 2. For now, edit
  `../shared/dictionary.json` directly (shared with the Mac app).
- No installer yet; run the published exe directly, or check "Launch at login" in Settings to
  add it to the current user's Run registry key.
