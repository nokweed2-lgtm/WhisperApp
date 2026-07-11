# Whisper — Development Roadmap (Mac + Windows)

> อ่านคู่กับ **`docs/WISPR-FEATURE-ROADMAP.md`** (แคตตาล็อกฟีเจอร์ทั้งหมดของ Wispr Flow ที่จะทำตาม
> และ **`HANDOFF.md`** (log งานล่าสุด/บั๊ก/บทเรียน — คนละหน้าที่กับไฟล์นี้: ที่นี่คือ "จะทำอะไรต่อ เรียงเป็น phase")
>
> Legend: ✅ เสร็จแล้ว · 🔄 กำลังทำ/ทำบางส่วน · ⬜ ยังไม่เริ่ม · ❓ สถานะไม่ชัวร์ ต้องเช็คก่อนเริ่ม phase นั้น

---

## Constraint ที่ต้องจำไว้ทุก phase

**Swift (Mac) เขียนโค้ดได้จากเครื่อง Windows แต่ build/test ได้เฉพาะบน Mac เท่านั้น**
→ phase ไหนแตะ `Sources/*.swift` ให้ทำเป็นก้อน แล้ว batch ไป build+test บน Mac ทีเดียว
อย่าสลับ Mac↔Windows ถี่ๆ ในหนึ่ง phase เพราะจะมี "เขียนเสร็จรอ build" ค้างนาน (แบบที่เคยเกิดกับ WhisperWin คืนก่อน)

C# (Windows) เขียน+build+test ได้ในเครื่องเดียวจบ (dotnet ครบ, unit tests ไม่แตะ Win32 จริง)

---

## สถานะปัจจุบัน (baseline ก่อนเริ่ม roadmap นี้)

### Parity table: Mac (Whisper v1.2) vs Windows (WhisperWin Phase 1)

| Feature | Mac | Windows | หมายเหตุ |
|---|---|---|---|
| Dictation loop (hold/toggle → Groq STT → LLM correction → paste) | ✅ | ✅ | Win ทดสอบพูดจริงผ่านแล้ว 2026-07-06 (Notepad, Claude Desktop, Claude Code) |
| Hotkey | ✅ Fn (ค้าง Wispr Flow กันไว้ ตอนนี้ใช้ Right Control แทน) | ✅ Right Ctrl | คนละปุ่ม default แต่ state machine (hold/double-tap toggle) เทียบเคียงกัน |
| Custom Dictionary (list mode) | ✅ UI ใน Settings (`DictionaryStore.swift`) | 🔄 อ่านอย่างเดียว (`DictionaryFile.cs`), ไม่มี UI แก้ | schema ปัจจุบัน: `shared/dictionary.json` = `{"entries": [string]}` |
| History | ✅ `HistoryView.swift`, build+test ผ่าน 2026-07-06 | ✅ `UI/HistoryWindow.xaml` + `Core/HistoryStore.cs`, test 108 ผ่าน 2026-07-10 (เหลือทดสอบพูดจริง) | Mac: ~/.whisperapp/history.json · Win: %APPDATA%\WhisperWin\history.json — ทั้งคู่ cap 500, ค้นหา/copy/Clear All |
| Language hint (th/en/auto) | ✅ ส่งเข้า prompt | ❓ ไม่ชัวร์ว่า `PromptBuilder.cs` รองรับเทียบเท่าไหม — เช็คก่อนใช้อ้างอิง | |
| Settings window | ✅ | ✅ (`SettingsWindow.xaml.cs`) | |
| Floating status indicator | ✅ `FloatingStatusView.swift` | ✅ `FloatingPill.xaml.cs` | |
| About window | ✅ `AboutView.swift` | ⬜ | ไม่ critical |
| Notarize/sign + installer | ✅ `make_dmg.sh` (notarize+staple) | ⬜ ไม่มี installer, รัน exe ตรง | |
| Debug log ถาวร | ❓ ไม่แน่ใจว่า Mac มีเทียบเท่า `%LOCALAPPDATA%\WhisperWin\debug.log` ไหม | ✅ | |

### Shared brain

- `shared/dictionary.json` — ใช้ร่วมกัน 2 แพลตฟอร์ม, schema ปัจจุบันเป็น **list คำ** (ไม่ใช่คู่ผิด→ถูก)
- `shared/correction-prompt.md` — prompt กลาง มี few-shot 3 ประโยคจากคุณนกแล้ว (Take Home, เนย 50g, สรุป section)

---

## Phase 1 — Dictionary คู่ "คำผิด→คำถูก" (shared schema upgrade) 🔄 (โค้ดเสร็จ + P3 approve แล้ว — เหลือ build+test บน Mac)

**เป้าหมาย:** อัปเกรด `shared/dictionary.json` จาก list คำ → คู่ replace (`to_replace` → `replace_with`)
พร้อม backward-compat กับของเดิม ทั้งสองแพลตฟอร์มอ่าน/เขียนคู่ใหม่ได้

**ทำไมก่อน:** ตกลงกันไว้แล้วใน HANDOFF ว่าคุ้มสุด แก้ปัญหาคำสะกดผิดรายวันตรงจุด (เช่น "คลอดโค้ด" → "Claude Code" เป๊ะ แทนบอก AI กว้างๆ)

**แพลตฟอร์ม:** shared (schema) + Mac + Windows

**งาน:**
- [x] ออกแบบ schema ใหม่ใน `shared/dictionary.json` — แนะนำ:
  ```json
  {
    "entries": ["..."],
    "pairs": [
      { "to_replace": "คลอดโค้ด", "replace_with": "Claude Code", "source": "manual", "starred": false }
    ]
  }
  ```
  เก็บ `entries` (list เดิม) คู่กับ `pairs` (ใหม่) เพื่อ backward-compat — ของเก่าที่ยังอ่าน `entries` อย่างเดียวไม่พัง
  (ของจริงตัด `source`/`starred` ออก — เหลือแค่ `to_replace`/`replace_with` พอใช้ก่อน)
- [x] Mac: แก้ `Sources/DictionaryStore.swift` ให้อ่าน/เขียนทั้ง `entries` + `pairs`, ส่ง `pairs` เข้า correction prompt เป็นกฎ "แทนที่ X ด้วย Y" ที่ชัดกว่าการยัด list คำ (เขียนเสร็จ — **ยังไม่ได้ build/test บน Mac**)
- [x] Mac: แก้ UI ใน `SettingsView.swift` ให้มีโหมดคู่ (ช่อง "คำผิด" + "คำถูก") ควบคู่โหมด list เดิม (เขียนเสร็จ — **ยังไม่ได้ build/test บน Mac**)
- [x] Windows: แก้ `windows/WhisperWin/Core/DictionaryFile.cs` ให้ deserialize `pairs` ด้วย (เพิ่ม property, ไม่ลบ `Entries`)
- [x] Windows: แก้ `PromptBuilder.cs` ให้ประกอบกฎจาก `pairs` เข้า prompt เหมือน Mac (P3 ยืนยัน byte-identical แล้ว)
- [x] อัปเดต `shared/correction-prompt.md` ให้อธิบายรูปแบบกฎ pairs (มี `{{REPLACEMENTS}}` แล้ว)
- [x] Migration: dictionary.json ปัจจุบัน (7 คำใน `entries`) ต้องยังใช้ได้ทันทีหลังอัปเกรด ไม่ต้องแก้ไฟล์มือ (pairs เป็น optional ทั้งสองฝั่ง มี unit test คุม)
- [ ] **ปิด phase บน Mac**: build + test + ทดสอบพูดจริง (ตั้งคู่ใน Settings แล้วคำถูกแก้ตรง) — ก้อนเดียวจบตาม constraint

**ผล P3 review (2026-07-07): approve-with-nits** — เทสต์ Windows 56/56 ผ่าน · findings ที่ต้องเก็บไปทำ:
- ⚠️ ก่อน/ระหว่าง Phase 2: prompt ฝั่ง Mac เป็น hardcoded ใน `TextCorrectionService.swift` ส่วน Windows โหลด `shared/correction-prompt.md` → จะ drift ในอนาคต ควรให้ Swift โหลดไฟล์เดียวกัน หรือมีเทสต์เทียบ
- ⚠️ ก่อน Phase 2 (Windows dictionary editor): ตอนนี้ Windows อ่านอย่างเดียวเลยไม่มี race — พอมี 2 writers ผ่าน OneDrive ต้องคิดเรื่อง last-writer-wins ก่อน
- ⚠️ `SharedPaths.cs` ฝั่ง Windows เลือกไฟล์ข้าง exe ก่อน repo `shared/` → build ที่ publish แล้วจะไม่เห็นไฟล์เดียวกับ Mac — ต้องตัดสินใจ design ก่อน Phase 2
- nit: `removeDictPair` ใน SettingsView ลบคู่ซ้ำทั้งหมด + `ForEach(id: \.self)` ชนกันถ้ามีคู่ซ้ำ (โอกาสเกิดต่ำ ไม่บล็อก)

**Dependency:** ไม่มี (เริ่มได้เลย) — แต่เป็น **schema กลาง** ที่ Phase 2 (Windows UI) ต้องใช้ต่อ ทำ phase นี้ให้เสร็จก่อน

**Definition of done:**
- Mac: เพิ่ม/ลบคู่ผิด→ถูกใน Settings ได้, บันทึกลง `shared/dictionary.json`, ทดสอบพูดจริงแล้วคำที่ตั้งคู่ไว้ถูกแก้ตรง (ไม่ใช่แค่ hint กว้างๆ) — **ต้อง build+test บน Mac**
- Windows: อ่านคู่จาก `shared/dictionary.json` ที่ Mac เขียน (หรือกลับกัน) แล้วพฤติกรรม correction ตรงกัน, unit test ผ่าน (`dotnet test`)
- ของเดิม (dictionary list 7 คำ) ยังทำงานเหมือนเดิมทั้งสองฝั่ง

---

## Phase 2 — Windows parity: Dictionary editor UI + History window ✅ (Dictionary editor + History window เสร็จ+P3 approve · เหลือทดสอบพูดจริง)

**เป้าหมาย:** WhisperWin มี Dictionary editor (list + pairs) และ History window เทียบเท่า Mac

**แพลตฟอร์ม:** Windows เท่านั้น (ใช้ schema จาก Phase 1)

**งาน:**
- [x] Dictionary editor UI ใน `SettingsWindow.xaml`/`.cs` — เพิ่ม/ลบคำเดี่ยว + คู่ผิด→ถูก, เขียนกลับ `shared/dictionary.json` ทันที (ผ่าน P1→P2→P3 · 2026-07-07)
  - เพิ่ม `Core/DictionaryStore.cs` (read-modify-write, Ok/Missing/Unreadable, mirror ฝั่ง Mac), แก้ `SharedPaths.cs` (repo shared/ ก่อน embedded), แก้ `App.xaml.cs` BuildSystemPrompt ใช้ DictionaryStore.Load กัน torn-write crash · tests 56→82 ผ่าน · publish แล้ว
  - P3 nit ที่ตัดสินใจ "ยอมรับ ไม่แก้": missing-file seed ต่าง (Mac seed 7 คำ / Windows entries ว่าง — เกิดเมื่อไฟล์หายทั้งไฟล์เท่านั้น), JSON escaping ต่างเล็กน้อยแต่ interop ปลอดภัย, ไม่มี OneDrive file-lock (single-user ทีละเครื่อง รับได้)
- [x] History window (`UI/HistoryWindow.xaml`/`.cs`) — เก็บ `%APPDATA%\WhisperWin\history.json`, cap 500 (บังคับทั้ง Load+Append), ค้นหา (case-insensitive), คลิก copy (retry COMException), Clear All (disable ตอนว่าง), empty states (ผ่าน P1→P2→P3 · 2026-07-10)
  - `Core/HistoryStore.cs` (Load/Append/Clear/DefaultFilePath, mirror DictionaryStore ReadOutcome: missing/locked/corrupt → empty, Append no-op กัน clobber, UnsafeRelaxedJsonEscaping กันไทยโดน \uXXXX) + `Core/HistoryEntry.cs` (Id/Date ISO8601/Text)
- [x] เรียก `HistoryStore.Append` จาก `App.xaml.cs` `OnStageChanged` ตอน `DictationStage.Done` (ข้อความสุดท้าย, ข้าม blank) — วางไว้ก่อน pill-null guard เพื่อ decouple การบันทึกออกจาก UI (nit #2 ของ P3) · เลือก append ที่ App layer แทน DictationController เพื่อไม่ผูก Core กับฟีเจอร์ history
- [x] เพิ่มเมนู tray "History" + `OpenHistory()` คู่กับ "Settings"
- [x] Unit tests: history read/write/cap/search — 26 เคส (P1 15 + P2 11: boundary cap 500/501, wrong-shape JSON, corrupt/locked/missing, concurrent append ไม่พังไฟล์, ไทย/emoji/newline round-trip, date precision) · tests 82 → **108 ผ่าน**

**Dependency:** Dictionary UI ต้องรอ Phase 1 (schema คู่) เสร็จก่อน — History ไม่ผูกกับ Phase 1 ทำคู่ขนานได้

**Definition of done:**
- `dotnet build` + `dotnet test` ผ่านทั้งหมด
- ทดสอบจริงบนเครื่อง Windows: เปิด History เห็นรายการล่าสุด, ค้นหาเจอ, คลิก copy ได้, Clear All ล้างจริง
- Dictionary UI แก้แล้ว Mac อ่านไฟล์เดียวกันเห็นการเปลี่ยนแปลง (ทดสอบ cross-platform sync ผ่าน shared/)

---

## Phase 3 — เรียนรู้สไตล์จากการแก้ของผู้ใช้ (divergence score) ⬜

**เป้าหมาย:** ฟีเจอร์เรือธงของ Wispr แบบ lite — ให้ AI เรียนรู้ว่าคุณนกมักแก้ผลลัพธ์ยังไง แล้วเลียนสไตล์อัตโนมัติ

**แพลตฟอร์ม:** Mac ก่อน (มี History ที่ build+test แล้ว) → ตาม Windows หลัง Phase 2

**งาน:**
- [ ] ออกแบบช่องทางให้ผู้ใช้บอกว่า "ข้อความที่ AI ออกมา" vs "เวอร์ชันที่ถูกจริง" — เสนอ: ปุ่ม "แก้ล่าสุด" ใน History ให้พิมพ์ทับ เก็บคู่ (original, corrected) ต่อ entry
- [ ] เพิ่ม field ใน `HistoryView.swift`/store: `userEditedText` (nullable) ต่อรายการ
- [ ] คำนวณ divergence แบบง่าย (เช่น edit distance หรือ diff คำ) ระหว่าง AI output กับ user-edited — ไม่ต้องซับซ้อนเท่า Wispr
- [ ] หยิบ 2-3 คู่ล่าสุดที่ divergence สูงสุด ใส่เป็น few-shot ตัวอย่างเพิ่มใน correction prompt (ต่อยอดจาก few-shot ที่มีอยู่แล้วใน `TextCorrectionService.swift`)
- [ ] ทดสอบว่าพฤติกรรมการแก้เปลี่ยนไปตามสไตล์จริง (manual QA ด้วยประโยคที่คุณนกเคยแก้)
- [ ] Windows: พอร์ต logic เดียวกันหลัง Mac ยืนยันว่าใช้ได้จริง (กัน rework)

**Dependency:** ควรทำหลัง Phase 1 (dictionary pairs อาจ overlap กับ divergence — คำที่ถูกแก้บ่อยอาจกลายเป็น candidate เข้า dictionary อัตโนมัติในอนาคต แต่ไม่ใช่ scope นี้)

**Definition of done:**
- Mac: มีทางพิมพ์ "เวอร์ชันถูก" ทับใน History ได้จริง, เก็บลง history.json, prompt หยิบตัวอย่างล่าสุดไปใช้จริงตอน correction ครั้งถัดไป — **build+test บน Mac**
- อย่างน้อย 1 เคสสาธิตได้ว่า AI เริ่มออกสไตล์ที่คุณนกแก้บ่อยโดยไม่ต้องพิมพ์ทับซ้ำ

---

## Phase 4 — Context-aware tone (ปรับโทนตามแอปที่โฟกัส) ⬜

**เป้าหมาย:** อ่านบริบทแอปที่ใช้อยู่ (Slack/LINE=โทนแชท, Mail=ทางการ, code editor=technical) แล้วปรับ prompt ให้เหมาะ

**แพลตฟอร์ม:** Mac + Windows (คนละ API แต่ concept เดียวกัน)

**งาน:**
- [ ] Mac: อ่านชื่อแอป frontmost ผ่าน `NSWorkspace.frontmostApplication`, ส่งเข้า prompt เป็น context hint
- [ ] Mac (ขั้นสูงกว่า, ทำทีหลังได้): อ่านเนื้อหาช่องพิมพ์ผ่าน Accessibility API (`AXUIElement`) — เสี่ยง permission/perf มากกว่า เริ่มจากแค่ชื่อแอปก่อน
- [ ] Windows: อ่านชื่อโปรเซส/หน้าต่าง frontmost ผ่าน `GetForegroundWindow` + `GetWindowThreadProcessId`
- [ ] Windows (ขั้นสูง): UIAutomation อ่านเนื้อหาช่องพิมพ์ — เทียบเคียง AX API ฝั่ง Mac
- [ ] กำหนด mapping แอป→โทนเริ่มต้น (เช่น config เล็กๆ ใน `shared/` หรือ hardcode รายชื่อแอปที่ใช้บ่อย) ให้แก้ได้ง่ายภายหลัง
- [ ] ทดสอบ manual: พูดประโยคเดียวกันในแอปต่างกัน เช็คว่าโทนเปลี่ยนสมเหตุสมผล

**Dependency:** อิสระจาก Phase 1-3 แต่แนะนำทำหลัง เพราะ effort สูงกว่า (Accessibility/UIAutomation) และผลตอบแทนไม่เร่งด่วนเท่า dictionary/history ที่ใช้ทุกวัน

**Definition of done:**
- ทั้งสองแพลตฟอร์มส่ง context (อย่างน้อยชื่อแอป) เข้า correction prompt ได้จริง
- ทดสอบแล้วโทนเปลี่ยนตามแอปอย่างสังเกตได้ (ไม่ต้อง perfect แค่ direction ถูก)
- Mac build+test ผ่าน, Windows `dotnet test` ผ่าน

---

## Phase 5+ — ฟีเจอร์ที่เหลือจาก Wispr catalog ⬜

รายการเต็มพร้อมรายละเอียดอยู่ใน **`docs/WISPR-FEATURE-ROADMAP.md`** — ไม่ duplicate ที่นี่ จัดกลุ่มคร่าวๆ ตามคุณค่า/ความคุ้มสำหรับ phase ถัดไปหลัง Phase 1-4:

### Phase 5 — เก็บตกงาน Tier 1/2 ที่ยังไม่ทำ
- [ ] History: เก็บสถิติ latency/speech-duration/word-count/correction-count ต่อ entry (Tier 1.2)
- [ ] History: เก็บ language + confidence ต่อ entry (Tier 1.4)
- [ ] User voice preferences / context profile (อาชีพ/บริบทงาน ฝังใน prompt) (Tier 2.2)
- [ ] Snippets — พูดคีย์เวิร์ดสั้นๆ ขยายเป็นข้อความยาว (Tier 2.3)

### Phase 6 — ประเมิน Tier 4 (เกินขอบเขต dictation)
ทำให้แอปกลายเป็น "ผู้ช่วยประชุม" ระดับใหญ่ — **ต้องคุยกับคุณนกก่อนเริ่มแต่ละอันว่าคุ้มไหม** ไม่ใช่ default ทำหมด:
- [ ] ❓ Meetings (transcribe + diarization + summary + calendar sync) — effort สูงสุดในลิสต์
- [ ] ❓ Notes (จดจากเสียง + version history + แนบรูป)
- [ ] ❓ Todos (ดึง to-do จากคำพูด/ประชุม)
- [ ] ❓ Calendar integration
- [ ] ❓ Links (เก็บ+enrich ลิงก์ที่พูดถึง)
- [ ] ❓ Automations เรียกด้วยเสียง
- [ ] ❓ Instruct/commands (สั่งงานด้วยเสียง + tool calls)

Wispr มี Meetings/Notes/Todos มากที่สุดในเชิง schema — ถ้าจะทำ Tier 4 แนะนำเริ่ม Meetings หรือ Notes ก่อนตามที่ระบุไว้ใน WISPR-FEATURE-ROADMAP.md แต่ควรประเมินใหม่ตอนนั้นว่ายังตรงกับที่คุณนกใช้จริงไหม

---

## หลักการที่ยึดตลอด roadmap

- ทุกอย่างที่เป็น "สมอง" (dictionary, prompt, style, snippets) → เก็บใน `shared/` ให้ Mac+Windows ใช้ร่วม
- Local-first: ประวัติ/dictionary อยู่ในเครื่อง ไม่มี server/cloud sync (ต่างจาก Wispr)
- clean-room เสมอ — ดูฟีเจอร์จาก schema ของ Wispr แต่เขียนโค้ดเอง ไม่ลอก
- Phase ที่แตะ Swift ให้ batch งานก่อนข้ามไป build/test บน Mac (ดู constraint ด้านบน)
- อย่า commit/push จนกว่าคุณนกสั่ง (ตาม HANDOFF.md)

---

## จุดที่ไม่ชัวร์ ณ ตอนเขียน roadmap นี้ (2026-07-06)

- ❓ `windows/WhisperWin/Core/PromptBuilder.cs` รองรับ language hint (th/en/auto) เทียบเท่า Mac หรือไม่ — ไม่ได้เปิดไฟล์อ่านละเอียด ต้องเช็คตอนเริ่ม Phase 1/3 ถ้าจะอ้างอิง behavior parity
- ❓ Mac มี debug log ถาวรเทียบเท่า `%LOCALAPPDATA%\WhisperWin\debug.log` ของ Windows หรือไม่ — ถ้าไม่มีอาจเป็นช่องว่าง parity เล็กๆ ที่ควรทำคู่กับ Phase ไหนก็ได้ที่สะดวก
- ❓ Windows ยังไม่เคย `dotnet publish` + แจกจริงนอกเครื่อง dev (README บอกว่า "ยังไม่ publish/test จริง" ในความหมาย distribution แม้ manual test ผ่านแล้ว) — ถ้าต้องการ installer/auto-update ในอนาคต ต้องเพิ่มเป็น phase แยก ยังไม่ได้ใส่ไว้ในนี้เพราะไม่ใช่ feature parity แต่เป็น packaging
