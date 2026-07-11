# Daily Log — Whisper / WhisperWin

> บันทึกรายวัน: ทำอะไรเสร็จ เจอปัญหาอะไร ตัดสินใจอะไร ค้างอะไร — เขียนใหม่ล่าสุดไว้บนสุด
> ทุก session ต้องอัปเดตไฟล์นี้ก่อนจบงาน (กติกาอยู่ใน CLAUDE.md)

---

## 2026-07-11 — เครื่อง Mac · ปิดก้อน Mac ที่ค้าง (Phase 1 dict pairs + STT v3 + prompt hardening) — build ผ่าน

### เสร็จวันนี้ (ก้อน Mac ที่ค้างมาตั้งแต่ 2026-07-07)

- ✅ **STT v3 ยืนยันแล้ว**: `defaults read com.game.whisperapp stt.model.groq` = ไม่มี key → ใช้ code default; code default ใน `Sources/STTProvider.swift:39` = `whisper-large-v3` (ตัวเต็ม ไม่ใช่ turbo) → คุณนกไม่เคยพิมพ์ model override ค้าง เลยได้ v3 อัตโนมัติ
- ✅ **Phase 1 (Dictionary pairs) build ผ่านบน Mac ครั้งแรก**: `swift build` สะอาด 4.2s — ไฟล์ Swift ที่เขียนค้างจากเครื่อง Windows (DictionaryStore/TextCorrectionService/SettingsView) คอมไพล์ได้หมด
  - ตรวจ wiring: `Pair` schema (to_replace/replace_with), `loadPairs()`, `renderReplacements()` (byte-identical กับ Windows PromptBuilder), read-modify-write กัน clobber (unreadable→no-op, missing→seed, ok→merge) ครบ
  - path resolution: dev-run เดินขึ้นจาก `.build/debug` เจอ repo `shared/dictionary.json` (8 คำ มี "tier list"); ติดตั้งใน /Applications ค่อย fallback `~/.whisperapp/dictionary.json`
- ✅ **Prompt hardening ครบใน Swift** (`TextCorrectionService.swift`): Rule 1 (ร↔ล/ด↔ต + "เรียบล่อย"→"เรียบร้อย" + ห้ามเติมคำ), Rule 3 guard (ห้าม snap คำเพี้ยนเข้าชื่อ dictionary ถ้าเสียงไม่ตรง) + few-shot "เชียริต"→"tier list" (ไม่ใช่ "Wispr Flow")
- ✅ **build+sign+launch** ผ่าน `./run.sh` — เซ็น "WhisperApp Dev" (identity เดียวที่มีบนเครื่องนี้), bundle id `com.game.whisperapp` คงเดิม, แอปรันอยู่ (PID ยืนยันแล้ว) — พร้อมให้คุณนกทดสอบพูดจริง

### เสร็จเพิ่ม — debug เคส "รายงาน→แรงงาน" + เพิ่ม debug log ถาวรฝั่ง Mac

- 🔍 **คุณนกพูด "รายงาน" แต่แอตพิมพ์ "แรงงาน"** (เสียง raai-ngaan/raeng-ngaan ใกล้กัน) — debug อย่างเป็นระบบ:
  - **Isolation test** (ยิง correction API ตรงด้วย prompt จริง): ป้อน "รายงาน" ถูก → LLM คงไว้ถูก (เคส A/B); ป้อน "แรงงาน" ผิด → LLM ไม่แก้กลับ (เคส C) เพราะ **"แรงงาน" เป็นคำไทยจริง ไม่ใช่คำเพี้ยน**
  - **สรุป root cause:** Whisper STT ถอดผิด**แบบสุ่ม (intermittent)** ตั้งแต่ raw ไม่ใช่ LLM — เป็นคำจริง 2 คำเสียงใกล้กัน **ไม่มีทางแก้ deterministic ที่ปลอดภัย** (dictionary pair "แรงงาน→รายงาน" จะพังวันพูด "แรงงาน" จริง) → รับเป็นข้อจำกัด STT
  - ทดสอบซ้ำ: รอบใหม่ Whisper ถอด "รายงาน" ถูก ยืนยัน intermittent
- ✅ **เพิ่ม `Sources/DebugLog.swift`** (ปิด parity gap กับ Windows `%LOCALAPPDATA%\...\debug.log`) — เขียน `~/.whisperapp/debug.log` บันทึก `raw STT:` + `corrected:` ทุกครั้ง (best-effort, cap 1MB, gregorian/en_US_POSIX กันปีพุทธ) + wire ใน `DictationController`

### เสร็จเพิ่ม — Phase 3 (lite) Learn-from-Edits ผ่าน P1→P2→P3 (Mac v1, build ผ่าน)

- ✅ ออกแบบผ่าน brainstorming → spec `docs/superpowers/specs/2026-07-11-learn-from-edits-design.md` (คุณนกอนุมัติ) — แก้ใน History แล้วสั่งให้แอปเรียนรู้ได้ 3 ทาง:
  - **บันทึกเป็นกฎ** → เขียน pair ลง `dictionary.json` (reuse Phase 1)
  - **สอนเป็นตัวอย่าง** (opt-in) → เก็บ (raw, corrected) ใน `shared/learned-examples.json` (ไฟล์ใหม่, shared brain) → ป้อน few-shot ล่าสุด 5 อันเข้า correction prompt
  - **แก้เฉยๆ** → อัปเดตแค่ text
- ✅ ไฟล์: ใหม่ `Sources/LearnedExamplesStore.swift` (mirror DictionaryStore: walk-up shared path, unreadable→no-op, cap 200, recent(5), ISO8601 fractional-sec) + `shared/learned-examples.json` · แก้ `HistoryView.swift` (raw field + edit UI + 3 ปุ่ม), `TextCorrectionService.swift` (renderLearnedExamples splice หลัง replacements), `SettingsView.swift` (section จัดการบทเรียน + ลบ), `DictationController.swift` (thread raw เข้า history)
- ✅ **P2** verified: backward-compat decode (raw→nil), write-safety, cap/order, empty-block, unicode ไทยไม่ escape, DebugLog ปี · เจอ 2 → P1 แก้ (empty-teach guard, fractional-sec id)
- ✅ **P3 approve-with-nits ไม่มี blocker** — raw ที่สอนถูก semantic (= input จริงของ correction), format เป็น contract ให้ Windows พอร์ต byte-identical · P1 เก็บ nit no-op-teach guard + dismiss-after-teach เพิ่ม · nit fractional-sec + splice order บันทึกใน spec ให้ Windows fast-follow
- **หมายเหตุ:** prompt ฝั่ง Mac ยัง hardcode (P3 Finding #2 เดิม) — learned-examples เป็น data inject เลยไม่ drift แต่ตัว prompt text ยังต้องทำ runtime-load ทีหลัง

### ค้าง (เหลือ manual QA ของคุณนก + งานรอง)

- ⬜ **[Phase 3 QA] พูดจริงทดสอบ learn-from-edits**: dictate → แก้ใน History → "สอนเป็นตัวอย่าง" → เช็ค `shared/learned-examples.json` เพิ่มคู่ + พูดประโยคคล้ายเดิมดูว่า correction เอนตามที่สอน · ลอง "บันทึกเป็นกฎ" + ลบบทเรียนใน Settings
- ⬜ **[Phase 3 Windows fast-follow]** พอร์ต learned-examples เข้า `PromptBuilder.cs` (อ่าน `shared/learned-examples.json` + inject ตาม splice order ใน spec, ใช้ fractional-sec)
- ⬜ **พูดจริงยืนยัน 3 เคส** (unit test พิสูจน์ LLM ไม่ได้ ต้องพูดเอง): (1) ตั้งคู่ผิด→ถูกใน Settings แล้วพูด → คำถูกแก้ตรง (2) พูดไทยยาว → v3 ถอดแม่นขึ้น (3) พูด "tier list" → ออก "tier list" ไม่เป็น "Wispr Flow"
- ⬜ **[roadmap] Mac โหลด `shared/correction-prompt.md` runtime** แทน hardcode ใน `TextCorrectionService.swift` — เลิก duplicate/drift (P3 Finding #2); ตอนนี้ prompt ฝั่ง Mac ยัง hardcode อยู่ (เนื้อหา sync กับ shared แล้วด้วยมือ แต่เสี่ยง drift)
- ⬜ แยก error "no API key" ออกจาก "no audio detected" (nice-to-have)
- ⬜ Developer ID cert หาย (0 identities ยกเว้น self-signed) — ต้องหา/สร้างก่อนออก v1.3 notarized
- ⬜ ยังไม่ commit (รอคุณนกสั่ง)

---

## 2026-07-10 — เครื่อง Windows · Phase 2 ปิด: History window ผ่าน P1→P2→P3

### เสร็จวันนี้

- ✅ **History window ฝั่ง Windows** (Phase 2 ที่เหลือ) — parity กับ Mac `HistoryView.swift` · ผ่าน pipeline P1→P2→P3 ครบ
  - ใหม่: `Core/HistoryStore.cs` (Load/Append/Clear/DefaultFilePath — mirror `DictionaryStore.cs` ReadOutcome: missing/locked/corrupt → empty, Append no-op กัน clobber, `UnsafeRelaxedJsonEscaping` กันไทยโดน escape) + `Core/HistoryEntry.cs` (Id/Date ISO8601/Text)
  - ใหม่: `UI/HistoryWindow.xaml`/`.cs` — ค้นหา (case-insensitive), newest-first, คลิก copy (retry COMException แบบ TextInjector), Clear All (disable ตอนว่าง), empty states
  - แก้ `App.xaml.cs`: append ตอน `DictationStage.Done` (ข้อความสุดท้าย, ข้าม blank) + เมนู tray "History" + `OpenHistory()` · เลือก append ที่ App layer (ไม่แตะ `DictationController.cs`) เพื่อไม่ผูก Core กับ history
  - path: `%APPDATA%\WhisperWin\history.json` — **per-machine ไม่อยู่ใน `shared/`** (history ไม่ sync ข้ามเครื่อง ต่างจาก dictionary/prompt) · P3 ยืนยันไม่แตะ shared brain
- ✅ **tests 82 → 108 ผ่าน** (P1 15 + P2 11 เคส: boundary cap 500/501, wrong-shape JSON `{}`/`[123]`, corrupt/locked/missing file, concurrent append ไม่ทำไฟล์พัง, ไทย/emoji/newline/long-text round-trip, date precision)

### P2 findings (2 ข้อ)

- **Finding 1 (แก้แล้ว):** `Load()` ไม่ได้บังคับ cap 500 (มีแต่ `Append()`) → ไฟล์ที่มี >500 entry ทำ HistoryWindow เรนเดอร์ไม่จำกัด · P1 แก้ให้ Load re-cap หลัง deserialize ใช้ `const MaxEntries` ตัวเดียวกับ Append (ไม่ drift) · test แดง→เขียว
- **Finding 2 (รับไว้ ไม่แก้):** concurrent `Append` หลาย thread โยน `IOException` ได้ (`File.WriteAllText` `FileShare.None`, ไม่ retry) — **inert วันนี้** เพราะ caller เดียว (`OnStageChanged`) วิ่งใน `Dispatcher.InvokeAsync` = serialize บน UI thread เดียว · P2 ยืนยันไฟล์ไม่เคยพังจาก race · รับแบบเดียวกับ nit Phase 2

### P3 review: approve-with-nits (ไม่มี blocker)

- ยืนยัน: Done ยิงครั้งเดียว/รอบ (ไม่ double-append/append-on-error/append-empty), Load re-cap ถูก, parity ครบ (cap/newest-first/search/empty states/click-copy), history.json per-machine ไม่แตะ shared, DispatcherTimer flash self-stop ไม่ leak
- **nit #2 (เก็บแล้ว):** ย้าย append ขึ้นก่อน `if (_pill == null) return;` — decouple การบันทึก history ออกจาก UI pill (กันอนาคต `_pill` null แล้ว history หยุดเงียบ)
- **nit ที่เลื่อน (deferred):** #1 search placeholder "Search history…" ฝั่ง Win ยังไม่มี (Mac มี) · #3 Windows trim ช่องค้นหา (whitespace = show all) ต่างจาก Mac เล็กน้อย — ทั้งคู่ cosmetic

### เสร็จเพิ่ม — แก้บั๊ก over-correction ("tier list" → "Wispr Flow")

- 🔍 **เจอจาก debug log** (คุณนกพูด "tier list" แต่แอปพิมพ์ "Wispr Flow"): พลาด 2 ชั้น — (1) Whisper ถอด "tier list" → "เชียริต" (คำเพี้ยน) (2) LLM correction **จับคำเพี้ยนยัดเข้าชื่อใน dictionary ที่ใกล้ที่สุด** → "Wispr Flow" ทั้งที่เสียงไม่ตรง (เดาจากบริบท/dictionary ไม่ใช่เสียง = ผิดหลัก "ห้ามเติมคำที่ไม่ได้พูด")
- ✅ **แก้ผ่าน P1→P2→P3 (tests 108 → 113 ผ่าน):**
  - `shared/correction-prompt.md` + `Sources/TextCorrectionService.swift` (sync กัน) — เพิ่ม guard ใน Rule 3: จับคำเพี้ยนยัดเป็นชื่อใน dictionary **ได้เฉพาะเมื่อเสียงตรงจริง** ไม่ใช่แค่หัวข้อใกล้; ถ้าไม่ตรงชื่อไหนชัดให้เดาสะกดอังกฤษตามเสียง + few-shot "เชียริต" → "tier list" (พร้อมโน้ตว่าไม่ใช่ "Wispr Flow" เพราะเสียงไม่ตรง)
  - `shared/dictionary.json` + `Sources/DictionaryStore.swift` seed — เพิ่ม "tier list" เป็น anchor ที่ถูก (entries 7 → 8)
  - **ปิดช่อง drift ที่ P3 เตือนตั้งแต่ Phase 1**: เพิ่ม `PromptRealFileIntegrationTests.cs` (P2) — โหลด `shared/correction-prompt.md` + `dictionary.json` **ตัวจริง** ผ่าน `SharedPaths` มาเช็ค (placeholder ครบ, "tier list" ขึ้น, guard+few-shot ยังอยู่) แทนที่ของเดิมที่เทสต์แค่ template inline
- **P3 approve-with-nits** — เก็บ Finding #1 แล้ว (reword few-shot ให้สอนถูก: tier list ชนะเพราะเสียงตรง / Wispr Flow ตกเพราะแค่หัวข้อใกล้)
- **Finding #2 (deferred → roadmap):** prompt อยู่ 2 ที่ (Mac hardcode ใน `TextCorrectionService.swift` + Windows โหลด `shared/*.md`) เสี่ยง drift — เทสต์ใหม่คุมเฉพาะฝั่ง Windows ยังไม่คุม Swift copy · ทางแก้: Mac โหลด `shared/correction-prompt.md` runtime (ใช้ walk-up ของ `DictionaryStore.swift` ได้) เหลือ hardcode เป็น fallback → เลิก duplicate · ทำ session Mac
- ✅ publish exe ใหม่ (build 20:39) — เหลือคุณนกพูด "tier list" จริงว่าออกถูก (manual QA — unit test พิสูจน์พฤติกรรม LLM ไม่ได้)

### ค้าง

- ✅ **ทดสอบพูดจริงบน Windows (ผ่าน)**: (1) พูด "tier list" → ออก "tier list" ถูก ไม่เป็น "Wispr Flow" (log ยืนยัน raw STT ถอดถูกรอบนี้ + LLM คงคำ) · (2) เปิด "History" จาก tray → ประโยคที่พูดขึ้นครบ · หมายเหตุ: search/copy/Clear All ยังไม่ได้กดลองทีละปุ่ม (core ขึ้นรายการทำงานแล้ว)
- ⬜ **[roadmap] Mac โหลด `shared/correction-prompt.md` runtime** แทน hardcode ใน `TextCorrectionService.swift` (เลิก duplicate/drift — P3 Finding #2)
- ⬜ **บน Mac (session ถัดไป)**: build + test + พูดจริง — Phase 1 (dict pairs) + STT v3 + prompt hardening (ยังค้างจาก 2026-07-07) · เช็ค `defaults read` ว่าได้ v3
- ⬜ **ทดสอบ Dictionary editor จริง** บน Windows (ค้างจาก 2026-07-07)
- ⬜ parity เล็กๆ ที่เหลือฝั่ง Windows: About window, installer/sign, ยืนยัน language hint ใน `PromptBuilder.cs`
- ⬜ ยังไม่ commit ทั้งหมด (รอคุณนกสั่ง)

---

## 2026-07-07 — เครื่อง Windows · Phase 1 (Dictionary pairs) ผ่าน P3 review

### เสร็จวันนี้

- ✅ ตรวจสถานะ Phase 1 (Dictionary คู่คำผิด→คำถูก) — พบว่าโค้ดเขียนครบทั้งสองฝั่งจาก session ก่อนแล้ว:
  - shared: `dictionary.json` schema เพิ่ม `pairs` (optional, backward-compat), `correction-prompt.md` มี `{{REPLACEMENTS}}`
  - Mac: `DictionaryStore.swift` (Pair + กัน data-loss ตอนไฟล์ unreadable), `TextCorrectionService.swift` (renderReplacements), `SettingsView.swift` (UI เพิ่ม/ลบคู่)
  - Windows: `DictionaryFile.cs`, `PromptBuilder.cs`, wiring ใน `App.xaml.cs`
- ✅ รัน `dotnet test` — **ผ่าน 56/56** (เพิ่มจาก 34 — P2 เพิ่ม edge-case tests ของ pairs/PromptBuilder แล้ว)
- ✅ ส่ง P3 (Opus reviewer) รีวิวปิด phase — **approve-with-nits, ไม่มี blocker**:
  - ยืนยัน renderReplacements Mac/Windows byte-identical, schema round-trip ถูก, Windows read-only ตาม scope, ไม่มี key รั่วใน log
  - findings ที่เก็บไว้ทำก่อน/ระหว่าง Phase 2: prompt Mac hardcoded จะ drift จาก shared md · เรื่อง 2 writers ผ่าน OneDrive · `SharedPaths.cs` เลือกไฟล์ข้าง exe ก่อน repo (รายละเอียดใน ROADMAP.md ท้าย Phase 1)
- ✅ อัปเดต ROADMAP.md: ติ๊กงาน Phase 1 + บันทึกผล P3

### เสร็จเพิ่ม (บ่าย) — Phase 2: Dictionary editor ฝั่ง Windows

- ✅ ทำ Dictionary editor UI ใน WhisperWin Settings (P1→P2→P3) — คุณนกเพิ่ม/ลบคู่ "คำผิด→คำถูก" (และคำเดี่ยว) ได้จากแอปโดยตรง ไม่ต้องแก้ JSON เอง · **ไม่ต้องแตะ AI model** — การแก้คำทำผ่าน dictionary ที่ยัดเข้า prompt ของ LLM ตัวเดิม
  - ใหม่: `Core/DictionaryStore.cs` (read-modify-write, กัน clobber/torn-write, mirror ฝั่ง Mac)
  - แก้ `Core/SharedPaths.cs`: หา repo `shared/` ก่อน embedded (exe ที่ publish แล้วเห็นไฟล์เดียวกับ Mac) — แก้ finding P3 จาก Phase 1
  - แก้ `App.xaml.cs` `BuildSystemPrompt`: ใช้ `DictionaryStore.Load` แทน deserialize ตรงๆ (บั๊กที่ P2 เจอ — ไฟล์พังจาก OneDrive sync เคยทำ dictation แครช ตอนนี้ทำงานต่อได้)
- ✅ tests 56 → **82 ผ่าน** (P2 เพิ่มเคส: ไฟล์ Mac เขียนจริง, ไฟล์ล็อค OneDrive, JSON พัง, legacy 7 คำ, ไทย unicode ไม่โดน escape)
- ✅ P3 review: **approve-with-nits, ไม่มี blocker** — nits ตัดสินใจ "ยอมรับ ไม่แก้" (รายละเอียดใน ROADMAP Phase 2)
- ✅ `dotnet publish` — exe ใหม่ 69.1MB self-contained ที่ `windows\WhisperWin\bin\Release\net8.0-windows\win-x64\publish\WhisperWin.exe`

### เสร็จเพิ่ม (ค่ำ) — แก้ปัญหา dictation ถอดคำไทยเพี้ยน (เช่น "เรียบร้อย"→"เรียบล่อย")

- 🔍 **Debug อย่างเป็นระบบ**: เพิ่ม log ข้อความดิบ (`raw STT:`) ใน `DictationController.cs` (event `RawTranscribed` → `App.xaml.cs`) เพื่อแยกให้ออกว่าคำเพี้ยนมาจาก Whisper หรือ LLM · **หลักฐานชี้ชัด: raw STT == ผลสุดท้าย** → Whisper (turbo) ถอดเพี้ยนตั้งแต่ต้น ไม่ใช่ LLM แต่งเติม (สมมติฐานแรกตกไป)
- ✅ **แก้ 2 ชั้น (ผ่าน P1→P3, 82/82 tests):**
  1. **STT: `whisper-large-v3-turbo` → `whisper-large-v3` (ตัวเต็ม)** — แก้ต้นตอ, turbo ถูก prune เพื่อความเร็วเลยพลาดคลิปไทยสั้นบ่อย · แก้ 2 ฝั่ง: `windows/.../TranscriptionService.cs` (const), `Sources/STTProvider.swift` (Groq defaultModel)
  2. **Prompt hardening** (ตาข่ายชั้นสอง) — Rule 1 ใน `shared/correction-prompt.md` + `Sources/TextCorrectionService.swift` ให้ LLM กล้าแก้คำไทยเพี้ยน (ร↔ล, ด↔ต ฯลฯ) **แต่ย้ำห้ามเติมคำที่ไม่ได้พูด** + few-shot "เรียบล่อย"→"เรียบร้อย"
- ✅ **ทดสอบพูดจริงผ่าน**: ประโยคไทยยาว 12 วิถอดแม่นขึ้นชัด, "เรียบร้อย" ออกถูก, correction แก้ "Whisper 4"→"Wispr Flow" ให้ด้วย · publish exe ใหม่แล้ว
- ✅ **ราคา**: v3 ($0.111/ชม.เสียง) vs turbo ($0.04) — แต่คุณนกอยู่ Groq free tier = ฟรีทั้งคู่ (rate limit ลด 400→300 RPM ไม่มีผลกับ single user) · ยังถูกกว่า Wispr Flow ($12–15/ด.) มาก
- **P3 nits ที่รับทราบ**: (1) Mac ได้ v3 อัตโนมัติกรณีปกติ (`stt.model.groq` ว่าง → ใช้ default) ยกเว้นเคยพิมพ์ model เองในรีลีสแรก — เช็คด้วย `defaults read <bundleid> stt.model.groq` ตอน session Mac (2) ระวัง over-correct ชื่อเฉพาะ/สแลงที่ไม่อยู่ใน dictionary

### ค้าง

- ⬜ **บน Mac (session ถัดไป)**: build + test + ทดสอบพูดจริง ครอบคลุมทั้ง Phase 1 (dict pairs) + STT v3 + prompt hardening · เช็ค `defaults read` ว่าได้ v3 จริง
- ⬜ **ทดสอบ Dictionary editor จริง** บนเครื่อง Windows: เปิด Settings → เพิ่มคู่คำ → พูดคำนั้น → เช็คว่าแก้ตรง + Mac เห็นไฟล์เดียวกัน
- ⬜ Phase 2 ที่เหลือ: History window ฝั่ง Windows (ยังไม่เริ่ม)
- ⬜ ยังไม่ commit ทั้งหมด (รอคุณนกสั่ง)

---

## 2026-07-06 (ค่ำ) — เครื่อง Windows · 🎉 WhisperWin ใช้งานได้จริงครั้งแรก

**ผลลัพธ์ใหญ่:** WhisperWin ทำงานครบวงจรบน Windows แล้ว — กด Right Ctrl ค้างพูด → ข้อความ paste ลงแอปจริง (ทดสอบผ่านใน Notepad, Claude Desktop, Claude Code, VS Code)

### เสร็จวันนี้

- ✅ Build + test + publish WhisperWin ครั้งแรกบนเครื่อง Windows จริง (แต่ก่อนเขียนไว้เฉยๆ ไม่เคยรัน)
- ✅ แก้บั๊ก 6 ตัวจนใช้งานได้ (รายละเอียดเชิงเทคนิคใน HANDOFF.md):
  1. ขาด `using NAudio` → build พัง
  2. `InvariantGlobalization` → แอปแครชหลังเปิด ~2 นาที
  3. ไอคอน tray/exe ไม่มี → สร้าง app.ico จากโลโก้ Mac
  4. Ctrl ค้างหลังพูด → hook กลืน key-up (แก้ผ่าน pipeline P1→P2→P3)
  5. Deadlock แอปค้างทั้งตัว → ยิง event ใต้ lock ชน UI thread
  6. Paste ไม่ออกเลย → struct SendInput ขนาดผิดบน x64 (ตัวการหลักของ "พูดแล้วไม่มีตัวหนังสือ")
- ✅ เพิ่ม debug log ถาวร (`%LOCALAPPDATA%\WhisperWin\debug.log`) — วินิจฉัยปัญหาครั้งหน้าได้จาก log ตรงๆ
- ✅ สร้าง subagent pipeline ในโปรเจกต์ (`.claude/agents/`): p1-implementer / p2-tester / p3-reviewer — sync ผ่าน OneDrive ใช้ได้ทั้งสองเครื่อง
- ✅ Groq API key ใหม่สำหรับเครื่อง Windows (แยกจาก Mac, เก็บใน Credential Manager)
- ✅ Unit tests 29 → 34 ตัว (เพิ่ม edge cases ของ hotkey state machine)

### การตัดสินใจ

- บั๊ก/ฟีเจอร์ต่อจากนี้ให้ subagent (Sonnet ทำงาน, Opus รีวิว) เป็นหลัก — ประหยัดค่า model ตัวแพง
- คำที่ Whisper ฟังเพี้ยน (เช่น "ตีนไม้") จะแก้ด้วยฟีเจอร์ Dictionary คู่คำผิด→คำถูก (phase ถัดไป)

### เสร็จเพิ่ม (ปิดท้ายคืน)

- ✅ SendInput robustness (P1): ใส่ scan code ใน synthetic Ctrl+V (กันแอปที่อ่าน scancode มองไม่เห็น เช่นบาง Electron) + log ชื่อแอปปลายทางทุกครั้งที่ paste (`paste: sent 4/4 to Code.exe`) — VS Code ที่เคยรายงานว่าไม่ออก ใช้ได้แล้ว
- ✅ `docs/ROADMAP.md` — แผนฟีเจอร์แบ่ง 5 phases (Dictionary pairs → Windows parity → learn-from-edits → context-aware tone → ที่เหลือ)
- ✅ `docs/DAILY-LOG.md` (ไฟล์นี้) + กติกาใน CLAUDE.md: ทุก session ต้องอัปเดต daily log, ทำงานตาม roadmap, ใช้ subagent pipeline

### ค้าง

- ⬜ ยังไม่ commit ทั้งหมด (รอคุณนกสั่ง)
- ⬜ STT accuracy บางคำเพี้ยน — แก้ด้วย Phase 1 (Dictionary pairs) ใน roadmap

---

## 2026-07-06 (กลางวัน) — เครื่อง Mac

- จูน correction prompt จากประโยคจริง (few-shot 3 ประโยค + กติกาหน่วย) — ทดสอบผ่าน
- Dictionary UI ใน Settings (Mac) + `DictionaryStore.swift` อ่าน/เขียน `shared/dictionary.json`
- History window (⌘H) + HistoryStore — ทดสอบผ่าน
- Self-signed cert "WhisperApp Dev" ใช้ได้ สิทธิ์ Accessibility ไม่หลุดข้าม rebuild
- แก้บั๊ก modifier ซ้าย/ขวา + เปลี่ยน hotkey เป็น Right Control (Fn โดน Wispr Flow กินอยู่)
- แก้พิษ CRLF จาก OneDrive + เพิ่ม `.gitattributes`
- ตัดสินใจ: Windows app = C#/WPF (approach A), config ร่วมผ่าน `shared/`

(รายละเอียดเต็มดู HANDOFF.md)
