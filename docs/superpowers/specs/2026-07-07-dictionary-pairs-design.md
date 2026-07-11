# Phase 1 — Dictionary คู่คำผิด→คำถูก + จูน prompt code-switching

> Spec สำหรับ pipeline p1-implementer → p2-tester → p3-reviewer
> วันที่: 2026-07-07 · Platform: Mac (build+test) + Windows (เขียนโค้ด, test ทีหลัง)
> อ้างอิง roadmap: `docs/ROADMAP.md` Phase 1

## เป้าหมาย (ภาษาคน)

แก้ปัญหา correction 2 แบบที่คุณนกเจอจริง:
1. คำเฉพาะที่ STT ถอดเพี้ยนซ้ำๆ จนเดาไม่ได้ → ให้ผู้ใช้ตั้ง "คู่คำ" (คำผิด → คำถูก) เองได้
2. คำอังกฤษทั่วไปที่พูดปนไทยแล้วไม่ถูกแปลงกลับเป็นอังกฤษ → จูนคำสั่งที่ส่งให้ LLM

## เคสจริงจากคุณนก (ใช้เป็นชุดทดสอบ QA)

| ที่พูด (STT ถอดเพี้ยน) | ผลที่ถูกต้อง | แก้ด้วย |
|---|---|---|
| "ทำไมด็สบอร์ตของแอปในหน้าเด็สต์ทอปถึงมี 2 อัน" | "ทำไม dashboard ของแอปในหน้า desktop ถึงมี 2 อัน" | Part B (code-switch เดาออก) |
| "แล้วก็อีกเซ็กชั่นหนึ่ง ... เชี่ยน prompt ให้ด้วยนะ" | "แล้วก็อีก section หนึ่ง ... เขียน prompt ให้ด้วยนะ" | Part B |
| "ทำติมสแกนทีหนึ่งถึงทีสิบห้าฟอลเดชั่น" | "ทำ Theme Scan T1–T15 foundation" | Part A (คู่คำ — เดาไม่ได้) |

---

## Part A — Dictionary pairs

### A1. Schema `shared/dictionary.json`

เพิ่ม key `pairs` ควบคู่ `entries` เดิม:

```json
{
  "entries": ["Tar Sawang", "Coffee for Worker", "Claude Code", "Wispr Flow", "Take Home", "Micro Level", "Content Direction"],
  "pairs": [
    { "to_replace": "ติมสแกน", "replace_with": "Theme Scan" }
  ]
}
```

- แต่ละคู่มีแค่ 2 field: `to_replace`, `replace_with` (ตัด `source`/`starred` ที่ roadmap เสนอออก — YAGNI)
- **Backward-compat:** ไฟล์เดิมที่มีแค่ `entries` ต้องโหลดได้ → `pairs` = `[]`. ไฟล์ 7 คำปัจจุบันใช้ได้ทันทีไม่ต้องแก้มือ
- **Seed pairs:** เริ่มเป็น `[]` (ผู้ใช้เพิ่มเอง) — อย่า hardcode คู่ตัวอย่างลงไฟล์จริง

### A2. Mac (Swift) — `Sources/DictionaryStore.swift`

- เพิ่ม `struct Pair: Codable, Equatable { var toReplace: String; var replaceWith: String }` — CodingKeys map `to_replace`/`replace_with`
- `FileFormat` เพิ่ม `var pairs: [Pair]?` (optional เพื่อ decode ไฟล์เก่าที่ไม่มี key นี้)
- API เดิม `load() -> [String]` **คงไว้ไม่แตะ** (caller เดิมไม่ต้องแก้)
- เพิ่ม `loadPairs() -> [Pair]`
- **การเขียนต้องเป็น read-modify-write** เพื่อไม่ให้ฝั่งหนึ่งลบอีกฝั่ง:
  - `saveEntries(_ entries: [String])` = อ่านไฟล์เต็ม → แทน entries → เขียนกลับทั้ง entries+pairs
  - `savePairs(_ pairs: [Pair])` = อ่านไฟล์เต็ม → แทน pairs → เขียนกลับทั้งคู่
  - (เดิม `save(_:)` เขียนเฉพาะ entries จะลบ pairs — ต้องเลิกใช้/แทนที่)
- เขียน JSON ด้วย `.prettyPrinted, .withoutEscapingSlashes` เหมือนเดิม

### A3. Mac (Swift) — `Sources/SettingsView.swift`

- เพิ่ม state `@State private var dictPairs: [Pair]`, `@State private var newWrong`, `@State private var newRight`
- `onAppear` โหลด `DictionaryStore.loadPairs()`
- เพิ่ม UI sub-section ใต้ "Custom Dictionary" เดิม ชื่อ **"Word replacements"** (label ไทย/อังกฤษ):
  - 2 ช่อง TextField: "คำที่ฟังผิด" + "คำที่ถูก" + ปุ่ม Add (disabled ถ้าช่องใดว่าง)
  - list คู่ที่มี พร้อมปุ่มลบต่อรายการ (รูปแบบ UI ตามของ entries เดิม บรรทัด 97-110)
  - add/remove → `DictionaryStore.savePairs(dictPairs)`
- caller เดิมที่เรียก `DictionaryStore.save(dictEntries)` (บรรทัด 129, 135) เปลี่ยนเป็น `saveEntries`

### A4. Mac (Swift) — `Sources/TextCorrectionService.swift`

- บรรทัด ~32 หลังประกอบ `dictionaryList` เดิม เพิ่มการประกอบ **replacement block** จาก `DictionaryStore.loadPairs()`
- แทนลง placeholder ใหม่ `{{REPLACEMENTS}}` (ดู A6 รูปแบบ block)
- ถ้า pairs ว่าง → แทนด้วย `""` (string ว่าง ไม่มี header ค้าง)

### A5. Windows (C#) — เขียนโค้ด, test ทีหลังบนเครื่อง Windows

- `windows/WhisperWin/Core/DictionaryFile.cs`:
  - เพิ่ม `public List<DictionaryPair> Pairs { get; set; } = new();` มี `[JsonPropertyName("pairs")]`
  - เพิ่ม class `DictionaryPair` มี property `ToReplace` (`[JsonPropertyName("to_replace")]`) + `ReplaceWith` (`[JsonPropertyName("replace_with")]`)
  - คง `Entries` เดิม
- `windows/WhisperWin/Core/PromptBuilder.cs`:
  - เพิ่ม const `ReplacementsPlaceholder = "{{REPLACEMENTS}}"`
  - เพิ่ม `RenderReplacements(IEnumerable<DictionaryPair> pairs)` → คืน block **byte-identical กับฝั่ง Mac** (ดู A6)
  - `BuildSystemPrompt` เพิ่ม param pairs + substitute `{{REPLACEMENTS}}`
- แก้ caller ของ `BuildSystemPrompt` (หา call site — น่าจะใน `DictationController.cs` หรือ correction service ฝั่ง Win) ให้ส่ง `dictFile.Pairs` เข้าไป

### A6. รูปแบบ replacement block (ต้องตรงเป๊ะทั้ง 2 แพลตฟอร์ม)

- **pairs ว่าง** → block = `""` (empty string)
- **มี pairs** → block =
  ```
  Word replacements — apply these exact substitutions when the left-hand phrase appears (use context; do not replace inside unrelated words):
  - "ติมสแกน" → "Theme Scan"
  - "ฟอลเดชั่น" → "foundation"
  ```
  (header 1 บรรทัด + `- "{to_replace}" → "{replace_with}"` บรรทัดละคู่, คั่นด้วย `\n`, ใช้ลูกศร U+2192 " → ")

⚠️ **p1 ต้องเขียน renderer ฝั่ง Swift และ C# ให้ผลลัพธ์ string เท่ากันทุก byte** — p2 ควรมี test ยืนยัน (เทียบกับ string ตายตัวใน test)

---

## Part B — จูน prompt code-switching (`shared/correction-prompt.md` เท่านั้น)

แก้ไฟล์ prompt กลางอย่างเดียว มีผลทั้ง 2 แพลตฟอร์ม (ไม่แตะโค้ด Swift/C# ใน part นี้ นอกจากใส่ placeholder `{{REPLACEMENTS}}`)

### B1. เสริมกฎ code-switching (ข้อ 3 เดิม)
เพิ่มใจความ: "แม้ transcriber ถอดคำอังกฤษเพี้ยนหนัก (สะกดไทยมั่ว) ถ้าเดาได้จากบริบทว่าเป็นศัพท์อังกฤษ/เทคนิค ให้กู้เป็นสะกดอังกฤษที่ถูก — เช่น 'ด็สบอร์ต'→'dashboard', 'เด็สต์ทอป'→'desktop', 'เซ็กชั่น'→'section'"

### B2. เพิ่ม few-shot ตัวอย่างจริง
เพิ่มใน examples เดิม (ต่อจาก 3 ตัวอย่างที่มี):
- `"ทำไมด็สบอร์ตของแอปในหน้าเด็สต์ทอปถึงมี 2 อัน"` → `"ทำไม dashboard ของแอปในหน้า desktop ถึงมี 2 อัน"`

### B3. วาง placeholder `{{REPLACEMENTS}}`
แทรกในไฟล์ prompt ระหว่างส่วน dictionary กับ Examples — บรรทัดว่างคั่นบน-ล่าง เพื่อเวลา block ว่าง (`""`) จะไม่เหลือ header/บรรทัดค้างที่ทำให้ LLM งง

### B4. ระวัง over-correction
คำแนวเทคนิคที่ใส่เป็น hint ให้เป็น *guidance* ("ถ้าเข้าบริบท") ไม่ใช่คำสั่งแทนที่ตายตัว — กันแปลงคำไทยแท้ที่บังเอิญเสียงคล้าย

---

## Testing / Definition of Done

### Mac (ต้องผ่านบนเครื่องนี้ก่อนถือว่าเสร็จ)
- [ ] `swift build` ผ่าน (ผ่าน `./run.sh` เปิดแอปได้)
- [ ] Unit test (ถ้ามี test target) — decode ไฟล์เก่า (ไม่มี pairs) ได้, decode ไฟล์ใหม่ได้, read-modify-write ไม่ลบอีกฝั่ง, renderer block ตรง string ที่คาด
- [ ] Manual QA: เพิ่มคู่ใน Settings → พูดจริง → คำถูกแก้ตรง; ของเดิม 7 คำยังทำงาน
- [ ] Manual QA Part B: พูด 3 ประโยคเคสจริงข้างบน → เช็คว่า section/dashboard/desktop ถูกแปลง

### Windows (test ทีหลังบนเครื่อง Windows — ไม่บล็อก Mac)
- [ ] `dotnet build` + `dotnet test` ผ่าน
- [ ] Test: `RenderReplacements` ตรง string ที่คาด (เทียบ byte กับ Mac), `BuildSystemPrompt` มี pairs, deserialize `DictionaryFile` ทั้งแบบมี/ไม่มี `pairs`
- [ ] อ่านไฟล์ที่ Mac เขียน (มี pairs) แล้ว correction behavior ตรงกัน

### ร่วม
- [ ] ไฟล์ dictionary เดิม (7 คำ ไม่มี pairs) ยังใช้ได้ทั้ง 2 ฝั่ง ไม่พัง

---

## ไฟล์ที่แตะ

**Mac:** `Sources/DictionaryStore.swift`, `Sources/SettingsView.swift`, `Sources/TextCorrectionService.swift`
**Windows:** `windows/WhisperWin/Core/DictionaryFile.cs`, `windows/WhisperWin/Core/PromptBuilder.cs`, + call site ของ `BuildSystemPrompt`
**Shared:** `shared/correction-prompt.md` (Part B + placeholder), `shared/dictionary.json` (schema — เพิ่ม `pairs` key ตอนมีการเขียนครั้งแรก, ไม่ต้อง seed)

## นอกขอบเขต (ไม่ทำใน Phase นี้)
- field `source`/`starred` บน pair (เผื่อ Phase 3)
- Windows Dictionary editor UI (Phase 2)
- auto-learn คู่คำจากการแก้ของผู้ใช้ (Phase 3)
