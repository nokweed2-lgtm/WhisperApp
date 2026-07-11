# Learn-from-Edits (Phase 3) — Design Spec

> Date: 2026-07-11 · Platform: **Mac first** (Windows = fast-follow) · Owner: คุณนก
> Roadmap: `docs/ROADMAP.md` Phase 3 ("เรียนรู้สไตล์จากการแก้ของผู้ใช้")

## Goal

Let the user fix a wrong dictation **in the History window** and choose to have the app
**learn** from that fix, so future corrections improve. Two learning paths plus a plain edit.

## Key insight (from debugging 2026-07-11)

The "รายงาน → แรงงาน" error was an **intermittent Whisper STT mishearing between two valid Thai
words**. Learning must NOT try to hard-fix that class (both words are legitimate). Learning is for
**systematic / stylistic** corrections. The `raw STT` value is now captured (see `DebugLog.swift`),
so we can store it per history entry and build clean few-shot examples.

## Three actions when editing a History entry

| Action | Backing store | Risk | Notes |
|---|---|---|---|
| **แก้เฉยๆ** (plain edit) | history.json `text` only | none | just updates the stored text, teaches nothing |
| **บันทึกเป็นกฎ** (save as rule) | `shared/dictionary.json` `pairs` (reuse Phase 1) | high | exact word→word replacement, applied always |
| **สอนเป็นตัวอย่าง** (teach as example) | `shared/learned-examples.json` (NEW) | low | opt-in few-shot, fed into correction prompt |

Both learning paths are **opt-in per edit** (user taps a button; edits are never auto-learned).

## Data model

### NEW: `shared/learned-examples.json` (shared brain, syncs Mac↔Windows)
```json
{ "examples": [
  { "raw": "<text the STT/app produced, before this fix>",
    "corrected": "<the user's fixed version>",
    "created": "2026-07-11T15:49:53.482Z" }
] }
```
- Lives in `shared/` (like `dictionary.json` / `correction-prompt.md`) — it is "brain", not a
  per-machine log. Walk-up path resolution + `~/.whisperapp/` fallback, identical to
  `DictionaryStore.swift`.
- **Store cap:** keep at most **200** examples on disk (drop oldest). Bounds file size.
- **Injection cap:** correction prompt uses only the **most recent 5** examples (newest wins).
  No divergence scoring in v1 (YAGNI — roadmap says "lite").

### CHANGE: History entry gains `raw` (backward-compatible)
`HistoryEntry` adds `var raw: String?` (nullable → old files without it decode fine).
`raw` = the STT text *before* LLM correction (the effective input to the correction step).
When teaching an example, `raw` is the "before". If `raw` is nil (pre-existing entries), fall
back to the entry's `text` as the "before" (best effort).

## Components (Mac v1)

1. **`Sources/LearnedExamplesStore.swift`** (NEW) — mirror `DictionaryStore.swift`:
   - `struct Example: Codable, Equatable { var raw: String; var corrected: String; var created: String }`
   - `static func load() -> [Example]` (all, capped read)
   - `static func recent(_ n: Int = 5) -> [Example]` (suffix for injection)
   - `static func add(raw:corrected:)` — appends with ISO8601 `created`, enforces 200 cap
   - `static func remove(_ example: Example)` — for the Settings management UI
   - **Write safety identical to DictionaryStore:** file exists-but-unreadable ⇒ write is a no-op
     (never clobber real data with a seed); missing ⇒ seed `{ "examples": [] }`.
   - ISO8601 timestamp via `ISO8601DateFormatter` (fixed, locale-independent — avoid the Buddhist-year
     issue seen in `DebugLog`; see Fix-along below).

2. **`Sources/HistoryView.swift`** — `HistoryStore` + `HistoryView`:
   - `HistoryEntry`: add `raw: String?`.
   - `HistoryStore.append(text:raw:)` — add `raw` param (default nil for call sites that lack it).
   - `HistoryStore.updateText(id:newText:)` — for the plain-edit path.
   - UI: selecting/expanding a row reveals an **editable `TextField`/`TextEditor`** prefilled with
     the entry text, plus three buttons:
     - **แก้เฉยๆ** → `updateText`
     - **บันทึกเป็นกฎ** → opens a tiny two-field sheet (คำผิด / คำถูก) writing a
       `dictionary.json` pair via `DictionaryStore.savePairs` (do NOT auto-diff the sentence —
       fragile for Thai; user types the exact word pair). Pre-fill nothing (or leave optional).
     - **สอนเป็นตัวอย่าง** → `LearnedExamplesStore.add(raw: entry.raw ?? entry.text, corrected: editedText)`;
       show a one-line hint: "ใช้กับการแก้ที่ควรเป็นแบบนี้ทุกครั้ง — อย่าสอนคำเสียงพ้องที่เพี้ยนแบบสุ่ม (เช่น แรงงาน/รายงาน)".

3. **`Sources/TextCorrectionService.swift`** — inject learned examples:
   - Add `static func renderLearnedExamples(_ examples: [Example]) -> String` (empty → "").
     Format (target for byte-identical Windows port later):
     ```
     Learned corrections — the user previously fixed outputs like these; prefer the corrected form (use judgement, do not force it onto unrelated text):
     - "<raw>" → "<corrected>"
     ```
   - Call `renderLearnedExamples(LearnedExamplesStore.recent(5))` and splice into `systemPrompt`
     right after the existing `\(replacementsBlock)` slot (keeps dictionary + pairs + learned
     together as the "user-specific" block).

4. **`Sources/SettingsView.swift`** — new section "บทเรียนที่สอน (Learned examples)":
   list each `raw → corrected` with a delete button (mirror the existing dictionary section).
   Lets the user remove a bad lesson.

5. **`Sources/DictationController.swift`** — update the `HistoryStore.append` call site in
   `finishOnMain` to pass `raw:` (the `text` value from `afterSTT`, i.e. post-strip raw STT).
   Thread `raw` through `handleAudio` → `finishOnMain`.

## Fix-along (small, in scope)

- **`Sources/DebugLog.swift`**: timestamp shows Buddhist year (`2569`) under Thai locale. Set the
  `DateFormatter.locale = Locale(identifier: "en_US_POSIX")` and `.calendar = Calendar(identifier: .gregorian)`
  so logs read `2026`. (Same fix pattern LearnedExamplesStore must use for `created`.)

## Out of scope (v1)

- Windows port (reading `learned-examples.json` + `PromptBuilder` injection) — next session.
- Divergence scoring / auto-selection of examples — recency only.
- Auto-diffing a sentence edit into a dictionary pair — user types the pair explicitly.

## Verification (Mac reality: no XCTest target)

- `swift build` clean.
- Launch via `./run.sh`; manual QA by คุณนก:
  1. Dictate → edit an entry in History → "สอนเป็นตัวอย่าง" → confirm `shared/learned-examples.json`
     gains the pair.
  2. Dictate a similar phrase → confirm the correction leans toward the taught form.
  3. "บันทึกเป็นกฎ" writes a `dictionary.json` pair; Settings shows it.
  4. Delete a learned example in Settings → gone from file + no longer injected.
  5. Old history.json (no `raw`) still loads; teaching falls back to `text`.
- Backward-compat: existing `dictionary.json` (entries + pairs) and `history.json` unaffected.

## Cross-platform contract (for the Windows fast-follow)

- `shared/learned-examples.json` schema above is the shared contract.
- `renderLearnedExamples` output format must be reproduced byte-identically in
  `windows/WhisperWin/Core/PromptBuilder.cs` when Windows is ported.
- **`created` MUST be ISO8601 with fractional seconds** (millisecond precision, e.g.
  `2026-07-11T15:49:53.482Z`). Mac keys the Settings `ForEach` on `created`; whole-second
  timestamps from Windows can collide → duplicate SwiftUI ID when the Mac views them. The Windows
  writer must include fractional seconds too. *(P3 nit #1)*
- **Prompt splice order** must match Mac: dictionary list → replacements (pairs) → learned examples
  → hardcoded few-shot examples. Windows `PromptBuilder` must assemble in the same order so the
  final prompt is equivalent. *(P3 parity note)*

## Constraints

- Do NOT commit until คุณนก orders (per CLAUDE.md).
- Implementation via P1→P2→P3 subagent pipeline (per CLAUDE.md).
