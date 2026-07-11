# Whisper — Feature Roadmap (สร้างจากการส่อง Wispr Flow)

> เป้าหมาย: ทำฟีเจอร์ของ Wispr Flow ให้ครบใน Whisper (Mac) + WhisperWin (Windows) — **ใช้ส่วนตัว ไม่แจกจ่าย**
> ที่มา: ส่อง DB schema ของ Wispr Flow v1.5.1095 (`/Applications/Wispr Flow.app/.../migrations/`) เมื่อ 2026-07-06
> วิธีทำ: **clean-room** — ดูว่ามีฟีเจอร์อะไร แล้วเขียนเวอร์ชันเราเอง ไม่ลอกโค้ดเขา
> Dictionary/prompt/สมอง แชร์ระหว่าง Mac+Windows ผ่าน `shared/` (dictionary.json, correction-prompt.md)

## Legend
- ✅ = เสร็จแล้ว (Mac) · 🟡 = เขียนโค้ดแล้วรอ build/test · ⬜ = ยังไม่ทำ · 🪟 = สถานะฝั่ง Windows

---

## Tier 1 — แกน Dictation (สิ่งที่คุณนกใช้จริงทุกวัน)

### 1.1 Dictation loop (กดพูด→ถอดเสียง→เกลา→paste)
- ✅ Mac: hold-to-talk / double-tap toggle, Groq STT + LLM correction, auto-paste
- 🪟 Windows: เขียนเสร็จ Phase 1 (ยังไม่ publish/test จริง)

### 1.2 History (ประวัติคำพูด)
- ✅ Mac: `Sources/HistoryView.swift` — เก็บ ~/.whisperapp/history.json (cap 500), ค้นหา, คลิก copy, Clear All — **build + test ผ่านแล้ว 2026-07-06**
- ⬜ เพิ่มสถิติแบบ Wispr: latency, speech-duration, word-count, correction-count ต่อ entry
- 🪟 Windows: ✅ `UI/HistoryWindow.xaml` + `Core/HistoryStore.cs` (test 108 ผ่าน 2026-07-10, เหลือทดสอบพูดจริง)

### 1.3 Custom Dictionary
- ✅ Mac: `Sources/DictionaryStore.swift` + UI ใน Settings (เพิ่ม/ลบคำเอง) เก็บ `shared/dictionary.json`
- ⬜ **อัปเกรดเป็นคู่ "คำผิด→คำถูก"** (Wispr: `to_replace_dictionary`) — ตอนนี้เป็นแค่ list คำ
      เช่น "คลอดโค้ด"→"Claude Code" เป๊ะ แทนการบอก AI กว้างๆ · เก็บ source (manual/learned) + starred
- 🪟 Windows: อ่าน `shared/dictionary.json` อัตโนมัติ (list mode พร้อม); ต้องทำ UI แก้ + รองรับ pair mode

### 1.4 Language detection
- ✅ Mac: langHint th/en/auto ส่งเข้า prompt แล้ว
- ⬜ เก็บ language + confidence ลง history (Wispr: `add-language-and-logprobs`)

---

## Tier 2 — ความฉลาดเรื่องสไตล์ (ของเด็ดของ Wispr)

### 2.1 เรียนรู้สไตล์จากการแก้ของผู้ใช้ ⭐ (ฟีเจอร์เรือธง)
- ⬜ Wispr: `add-pasted-text-and-divergence-score` + `save-tone-match-pairs` + `add-personalization-style`
- แนวทาง lite ของเรา: History เก็บข้อความที่ AI ออกอยู่แล้ว → เก็บเพิ่มว่า "สุดท้ายผู้ใช้แก้เป็นอะไร"
      (วัด divergence) → เอา 2-3 คู่ล่าสุดใส่ prompt เป็นตัวอย่างสไตล์ → AI เลียนสไตล์คุณนกอัตโนมัติ
- ต้องมี: ช่องทางรู้ว่าผู้ใช้แก้ข้อความ (ยากบน Mac — อาจใช้ปุ่ม "แก้ล่าสุด" ใน History ให้ผู้ใช้พิมพ์เวอร์ชันถูก)

### 2.2 User voice preferences / context
- ⬜ Wispr: `create-user-voice-preferences-table`, `create-user-context-table`
- โปรไฟล์: อาชีพ/บริบทงานของคุณนก (ร้านกาแฟ/มาร์เก็ตติ้ง) ฝังใน prompt ให้เดาศัพท์ถูกขึ้น

### 2.3 Snippets (ข้อความสำเร็จรูปเรียกด้วยเสียง)
- ⬜ Wispr: `snippets` — พูดคีย์เวิร์ด → ขยายเป็นข้อความยาว (เช่น "ที่อยู่ร้าน" → ที่อยู่เต็ม)

---

## Tier 3 — Context Awareness (Phase 3 เดิม)

### 3.1 ปรับโทนตามแอปที่โฟกัส
- ⬜ Wispr: `add-per-app-stats`, `add-textbox-contents`, `add-ax-html`, `add-axtext`
- อ่านชื่อแอป/URL/เนื้อหาในช่องพิมพ์ (ผ่าน Accessibility) → ปรับโทน:
      Slack/LINE=โทนแชท, Mail=ทางการ, Code editor=technical
- Mac: ใช้ NSWorkspace.frontmostApplication + AX API · Windows: GetForegroundWindow + UIAutomation

---

## Tier 4 — เกินขอบเขต dictation (Wispr มี แต่คุณนกอาจไม่ต้องทำ)

> ส่วนนี้ทำให้ Wispr เป็นแอปใหญ่ระดับ "ผู้ช่วยประชุม" — ประเมินก่อนว่าคุ้มจะทำไหม

- ⬜ **Meetings** — ถอดเสียงประชุม + แยกผู้พูด (diarization) + สรุป + sync ปฏิทิน (`meetings-*`, เยอะสุดใน schema)
- ⬜ **Notes** — จดโน้ตจากเสียง + version history + แนบรูป (`notes-*`, `note-images`, `note-versions`)
- ⬜ **Todos** — ดึง to-do จากสิ่งที่พูด/ประชุม (`create-todos-table`)
- ⬜ **Calendar** — เชื่อมปฏิทิน + preread (`calendar-events`, rsvp, color)
- ⬜ **Links** — เก็บ+enrich ลิงก์ที่พูดถึง (`links-table`, title/pinned/enrichment)
- ⬜ **Automations** — automation เรียกด้วยเสียง (`automations-table`)
- ⬜ **Instruct/commands** — สั่งงานด้วยเสียง + tool calls (`instruct-history`, `transcript-command`)

---

## ลำดับที่แนะนำให้ทำต่อ (คุยกับคุณนกไว้)
1. **Dictionary คู่คำ (1.3 อัปเกรด)** — แก้ปัญหาคำสะกดผิดรายวันทันที คุ้มสุด
2. **เรียนรู้สไตล์ (2.1)** — ของเด็ด Wispr, ทำ lite ได้
3. **Context-aware tone (3.1)** — Phase 3 เดิม
4. Windows ตามให้ทัน Mac (publish + test + History/Dictionary UI)
5. Tier 4 ค่อยประเมินทีหลังว่าจะเอาอันไหน (Meetings/Notes น่าจะคุ้มสุดถ้าจะทำ)

## หลักการ
- ทุกอย่างที่เป็น "สมอง" (dictionary, prompt, style, snippets) → เก็บใน `shared/` ให้ Mac+Windows ใช้ร่วม
- Local-first: ประวัติ/dictionary อยู่ในเครื่อง ไม่ต้องมี server (ต่างจาก Wispr ที่ sync cloud)
- clean-room เสมอ — ไม่แตะ/ลอกโค้ด Wispr
