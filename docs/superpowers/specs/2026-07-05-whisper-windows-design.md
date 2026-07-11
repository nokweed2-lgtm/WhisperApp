# Whisper for Windows — Design Spec

**Date:** 2026-07-05
**Status:** Approved by คุณนก (sections 1–2 explicitly; data flow / error handling / testing approved in condensed form)
**Positioning:** Personal-use app (no distribution/billing concerns for now). Goal: replace Wispr Flow on both Windows and Mac.

## Context

WhisperApp (this repo) is a mature macOS menu-bar dictation app (Swift, v1.2, notarized):
hold Fn → speak → Groq STT → LLM correction → paste into active app. คุณนก wants the same
capability on Windows, eventually with Wispr Flow-parity features (dictionary UI, history,
context-aware tone) on both platforms.

**Chosen approach (A):** Keep the mature Mac Swift app untouched. Build a native Windows
sibling app in C# / .NET 8 / WPF. Share the "brain" (correction prompt + custom dictionary)
via config files in `shared/` so both apps behave identically.

## Hard constraints

- **Do NOT touch `Sources/`** (Mac app) in this phase. Do NOT rename the repo or existing files (breaks TCC + links).
- **Do NOT commit/push** until คุณนก says so.
- Windows app lives entirely under `windows/`; shared config under `shared/`.

## Repo structure (additions)

```
WhisperApp/
├── Sources/          ← Mac app (Swift) — UNTOUCHED
├── shared/
│   ├── correction-prompt.md   ← system prompt rules (code-switching etc.)
│   └── dictionary.json        ← custom dictionary entries
└── windows/
    └── WhisperWin/            ← C# / .NET 8 / WPF app (see architecture)
```

## Phases

1. **Phase 1 (this spec):** Windows dictation MVP — parity with today's Mac app.
2. Phase 2: Main window with Dictionary editor UI + local History.
3. Phase 3: Context-aware tone (detect focused app → adjust tone).
4. Phase 4: Mac app catches up (reads `shared/`, gains phase 2–3 features).

## Phase 1 functional requirements

- Tray icon (Windows notification area) with context menu: Settings, Quit. App has no taskbar window.
- **Hotkey:** Right Ctrl (VK_RCONTROL), configurable later.
  - Hold ≥ 150 ms → start recording; release → stop and process.
  - Double-tap (2 presses < 400 ms apart) → toggle mode: recording stays on; single tap stops.
  - While recording, swallow the Right Ctrl key events (low-level hook returns 1) so the
    focused app doesn't see Ctrl and trigger its own shortcuts.
- **Floating pill** (borderless, always-on-top, click-through, bottom-center of active monitor)
  appears while recording, shows live waveform (mic level bars) and state (recording / processing).
- **Audio:** record mic → 16 kHz, 16-bit, mono WAV in memory (NAudio).
  Discard recordings shorter than 300 ms.
- **STT:** POST WAV to Groq `https://api.groq.com/openai/v1/audio/transcriptions`,
  model `whisper-large-v3-turbo`, multipart/form-data. Same key as correction.
- **Correction:** POST to Groq `https://api.groq.com/openai/v1/chat/completions`,
  model `llama-3.3-70b-versatile`, temperature 0.2.
  System prompt = contents of `shared/correction-prompt.md` with `{{DICTIONARY}}` placeholder
  replaced by the entries of `shared/dictionary.json`, plus a language hint line.
  (Prompt rules mirror `Sources/TextCorrectionService.swift` — the Thai/English
  code-switching rules tuned earlier today.)
- **Paste:** save current clipboard → set corrected text → SendInput Ctrl+V → restore previous
  clipboard after ~300 ms. Clipboard access on STA thread.
- **Settings window (WPF):** Groq API key (stored in Windows Credential Manager, never plain
  text), enable/disable LLM correction, launch-at-login checkbox (HKCU Run key), test-key button.
- **Config:** non-secret settings as JSON in `%APPDATA%\WhisperWin\config.json`.
- `shared/` path resolution: walk up from exe location to find repo `shared/`; fallback to a
  copy embedded next to the exe (build copies them). App works even if repo files move.

## Architecture (windows/WhisperWin/)

```
App.xaml(.cs)              — entry, tray icon (Hardcodet.NotifyIcon.Wpf or WinForms NotifyIcon), lifecycle
Core/HotkeyManager.cs      — WH_KEYBOARD_LL hook; hold/double-tap state machine; events: Started, Stopped, Toggled
Core/AudioRecorder.cs      — NAudio WaveInEvent 16kHz mono; level events for waveform; in-memory WAV
Core/DictationController.cs— state machine idle→recording→processing→pasting; wires everything
Core/TranscriptionService.cs— Groq Whisper call
Core/CorrectionService.cs  — Groq LLM call; prompt assembly from shared/
Core/TextInjector.cs       — clipboard save/set/restore + SendInput Ctrl+V
Core/Config.cs             — JSON config + Credential Manager for API key
UI/FloatingPill.xaml(.cs)  — pill + waveform
UI/SettingsWindow.xaml(.cs)— settings
```

**C# pitfalls that MUST be handled** (reviewer: check these):
- Keep a strong reference to the LowLevelKeyboardProc delegate (GC will collect it otherwise → hook dies randomly).
- Hook callback must return fast; never do I/O in it — raise events onto other threads.
- Clipboard operations require STA; WPF UI thread is STA — marshal via Dispatcher.
- NAudio WaveInEvent must be disposed properly between recordings; handle device-not-found.
- async/await all HTTP; no `.Result`/`.Wait()` (deadlocks).
- Single-instance guard (named mutex) — two instances = two hooks = chaos.

## Data flow

1. RightCtrl down ≥150 ms → HotkeyManager.Started → pill shows, recorder starts.
2. RightCtrl up → recorder stops → WAV bytes.
3. < 300 ms audio → discard silently. Else pill switches to "processing".
4. TranscriptionService → raw text. Empty/whitespace → discard, hide pill.
5. If correction enabled and key valid → CorrectionService → corrected text.
   **On correction failure → use raw text (graceful degradation), log to debug output.**
6. TextInjector pastes into the focused app. Pill hides.

## Error handling

| Failure | Behavior |
|---|---|
| No API key | Pill shows error briefly + tray balloon → opens Settings |
| STT network/API error | Tray balloon "Transcription failed"; audio discarded |
| Correction error | Paste raw transcription instead (never lose the user's words) |
| Recording < 300 ms | Ignore silently |
| Empty transcription | Do nothing |
| Elevated (admin) focused window | Paste can't reach it; text remains in clipboard; balloon informs user |
| Mic missing/busy | Balloon error; back to idle |

## shared/ file contents

`correction-prompt.md`: the transcript-cleanup prompt tuned 2026-07-05 (Thai/English
code-switching rules 1–6, `{{DICTIONARY}}` placeholder, "return only corrected text").
Must match the rules currently hardcoded in `Sources/TextCorrectionService.swift`.

`dictionary.json`:
```json
{ "entries": ["Tar Sawang", "Coffee for Worker", "Claude Code", "Wispr Flow"] }
```

## Testing (Phase 1)

- `dotnet build` succeeds (Release).
- Unit tests (xUnit, `windows/WhisperWin.Tests/`): hotkey state machine (hold vs double-tap
  vs short tap), prompt assembly (dictionary injection, language hint), config round-trip.
  Design Core classes so logic is testable without Win32 (inject clock/key events).
- Smoke test: launch exe, verify tray icon appears, verify single-instance mutex, exit cleanly.
- Manual acceptance (คุณนก): dictate the two benchmark sentences, compare against Wispr Flow.

## Benchmark sentences (acceptance)

1. "Bottom line ตรงๆ เดือนนี้ไม่มี patch ไหนที่ conversion บวกคุ้ม margin เลย อาจจะต้องทำการ relate file เข้าไปในส่วนต่างๆ"
2. "รบกวนสแกนหุ้นตัวนี้ใน Claude Code ให้หน่อยครับ เดี๋ยวผมจะเข้าไปดู score ของหุ้นตัวนี้ให้"
