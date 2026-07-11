# Whisper (WhisperApp)

macOS menu-bar dictation app (Swift) — กด Fn ค้างแล้วพูด → Groq STT → LLM แก้คำ → paste ลงแอปที่ใช้อยู่
มี sibling ฝั่ง Windows: **WhisperWin** (C#/WPF, `windows/`) hotkey = Right Ctrl — ใช้ prompt + dictionary ร่วมกันผ่าน `shared/`

## กติกาประจำโปรเจกต์ (ทุก session)

- **Daily log**: ก่อนจบงานทุกครั้ง อัปเดต `docs/DAILY-LOG.md` (สรุปว่าทำอะไรเสร็จ ตัดสินใจอะไร ค้างอะไร — วันใหม่เพิ่ม section บนสุด วันเดิมแก้ section เดิม)
- **Roadmap**: งานฟีเจอร์ให้ทำตาม phase ใน `docs/ROADMAP.md` แล้วติ๊กสถานะเมื่อเสร็จ
- **Subagents**: งาน implement/แก้บั๊กให้ใช้ pipeline `.claude/agents/` — p1-implementer (Sonnet) → p2-tester (Sonnet) → p3-reviewer (Opus) — model หลักทำหน้าที่ orchestrate เท่านั้น (คุณนกขอ ประหยัดค่า model แพง)
- อย่า commit/push จนกว่าคุณนกสั่ง

## สถานะปัจจุบัน (v1.2 — released 2026-07-05)

- **ชื่อแอป:** "Whisper" (bundle/repo ยังชื่อ WhisperApp — อย่า rename ไฟล์/repo เพราะกระทบ TCC + ลิงก์)
- **Hotkey default:** Fn, hold-to-talk · toggle mode = เคาะ 2 ครั้งเริ่ม เคาะ 1 ครั้งหยุด (`HotkeyManager.swift`)
- **Provider:** Groq เจ้าเดียว — key เดียวใช้ทั้ง STT (`whisper-large-v3-turbo`) + correction (`llama-3.3-70b-versatile`); Settings เหลือช่อง key ช่องเดียว
- **Logo:** Claude-style cream/clay paper-cut mic — mask ด้วย superellipse (n=5) เขียนด้วย Python/PIL, อย่าใช้ขอบที่ AI gen มาตรงๆ (มันเบี้ยว)
- **About window:** มีแล้ว (`AboutView.swift`) — เครดิต Gamezxz + ลิงก์

## Build & Release

- `./run.sh` — build + เปิดแอป (dev loop)
- `./make_dmg.sh` — build → sign → **notarize + staple อัตโนมัติ** (ต้องมี keychain profile `whisperapp-notary`, มีแล้วในเครื่องนี้)
- ออกเวอร์ชันใหม่: bump `Info.plist` → `./make_dmg.sh` → `gh release create vX.Y *.dmg` → แก้ลิงก์ดาวน์โหลด + badge เวอร์ชันใน `docs/index.html` (ลิงก์ตรงไปไฟล์ DMG ไม่ใช่ releases/latest)

## เว็บโปรโมต (GitHub Pages)

- https://gamezxz.github.io/WhisperApp/ — source ที่ `docs/`, ภาษาอังกฤษ, ธีม cream/clay แบบ cointh.com (Fraunces + Hanken Grotesk, palette `#f4f2ea`/`#cd6f4d` + dark mode)
- SEO ครบแล้ว: OG/Twitter card + `og.png`, JSON-LD SoftwareApplication, canonical, `robots.txt`, `sitemap.xml`
- Pages build fail เป็นครั้งคราว (transient) → retrigger: `gh api repos/Gamezxz/WhisperApp/pages/builds -X POST`

## ค้าง / ทำต่อได้

- Submit sitemap ใน Google Search Console (user ต้องทำเอง)
- JSON-LD `softwareVersion` + `downloadUrl` ใน `docs/index.html` ต้องอัปเดตทุกครั้งที่ออกเวอร์ชันใหม่
