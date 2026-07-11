# HANDOFF / Daily Log — 2026-07-06 (Mac session + Windows session)

> โน้ตส่งต่อ session ถัดไป — อ่านแล้วทำต่อได้เลย เรียกผู้ใช้ว่า **"คุณนก"**
> เป้าหมายใหญ่: แทนที่ Wispr Flow ด้วยแอป Whisper (Mac) + WhisperWin (Windows) — ใช้ส่วนตัว ไม่แจกจ่าย ใช้ชื่อ "Whisper" ต่อ

## 🪟 Windows session (2026-07-06 ค่ำ) — WhisperWin ใช้งานได้จริงแล้ว ✅

**คุณนกทดสอบพูดจริงผ่าน — ครบวงจร Right Ctrl → อัด → Groq STT → แก้คำ → paste** (ทดสอบใน Notepad, Claude Desktop, Claude Code)

บั๊กที่เจอ+แก้ (เรียงตามลำดับ ทุกตัว build/test ผ่าน 34/34 แล้ว ยังไม่ commit):

1. **Build พัง**: `AudioRecorder.cs` ใช้ `MmException` แต่ไม่มี `using NAudio;` — เพิ่มแล้ว
2. **แอปแครชหลังเปิด ~2 นาที**: `InvariantGlobalization=true` ใน csproj ใช้กับ WPF ไม่ได้ (XamlParseException "Cannot find non-neutral culture") — เอาออกแล้ว มีคอมเมนต์กันใส่กลับ
3. **ไม่มีไอคอน**: tray icon ไม่ได้ตั้งรูปเลย (ล่องหน) + exe ไม่มีไอคอน — สร้าง `windows/WhisperWin/Assets/app.ico` จาก `assets/WhisperApp.iconset/` (**ต้อง pack เป็น BMP entries ไม่ใช่ PNG** — GDI+ อ่าน PNG-in-ICO ไม่ได้; สคริปต์อยู่ใน scratchpad ถ้าต้องทำใหม่ใช้ C# Add-Type) + ตั้ง `ApplicationIcon` + โหลดเข้า tray จาก embedded resource
4. **Ctrl ค้าง**: hook ปล่อย key-down แรกผ่าน (hold promotion เป็น async) แต่กลืน key-up → OS เห็นกดแต่ไม่เห็นปล่อย — แก้เป็น **key-up ผ่านเสมอ** + กรอง `LLKHF_INJECTED` กัน Ctrl+V ของตัวเองย้อนเข้า hook (ทำผ่าน pipeline P1→P2→P3, เทสต์ 29→34)
5. **Deadlock แอปค้างทั้งตัว**: lock ที่ใส่ตอน hardening ยิง event ใต้ lock + `Dispatcher.Invoke` (sync) → timer thread ถือ lock รอ UI, UI (ที่รัน hook callback) รอ lock — แก้โดย**ยิง event นอก lock** (PendingEvent pattern ใน HotkeyStateMachine) + เปลี่ยนเป็น `Dispatcher.InvokeAsync` ทุกจุดใน App.xaml.cs · **บทเรียน: hook LL รันบน UI thread (message loop ของ thread ที่ติดตั้ง) ห้ามมีทางไหนที่ block UI while holding lock**
6. **Paste ไม่ออก (บั๊กสุดท้าย)**: struct `INPUT` ของ SendInput ขาด `MOUSEINPUT` ใน union → ขนาด 32 แทน 40 bytes บน x64 → SendInput คืน 0 เงียบๆ ทุกครั้ง — เพิ่ม MOUSEINPUT แล้ว + เช็คผลลัพธ์ลง log

ของใหม่ที่ควรรู้:

- **Debug log**: `%LOCALAPPDATA%\WhisperWin\debug.log` — ทุก stage + ขนาดเสียง + error (จงใจใส่ถาวร เพราะ pipeline มี silent path และ balloon อาจถูก Windows ซ่อน)
- **Subagents สร้างใหม่แล้วใน repo**: `.claude/agents/p1-implementer.md` (Sonnet เขียน), `p2-tester.md` (Sonnet เทสต์), `p3-reviewer.md` (Opus รีวิว) — sync ผ่าน OneDrive ใช้ได้สองเครื่อง (ตัวเดิมของคุณนกไม่เจอบนเครื่อง Windows อาจอยู่ ~/.claude/agents/ บน Mac — เทียบ/ทับได้)
- **Groq key ฝั่ง Windows**: สร้าง key ใหม่แยกจาก Mac เก็บใน Credential Manager (`WhisperWin:GroqApiKey`)
- exe ที่ใช้จริง: `windows\WhisperWin\bin\Release\net8.0-windows\win-x64\publish\WhisperWin.exe` (68.6 MB self-contained) — ก่อน publish ใหม่ต้อง `Stop-Process -Name WhisperWin` ก่อน ไม่งั้นไฟล์ล็อค

คิวถัดไปฝั่ง Windows: Phase 2 (Dictionary UI + History) หรือฟีเจอร์ (a) Dictionary คู่คำผิด→คำถูก (ทำ shared/ + สองฝั่งพร้อมกัน — Swift เขียนได้จากเครื่องนี้แต่ต้อง build/test บน Mac)

## 📋 ฟีเจอร์ทั้งหมดที่จะทำ

ดู **`docs/WISPR-FEATURE-ROADMAP.md`** — บันทึกฟีเจอร์ครบทุกอย่างของ Wispr Flow (ส่องจาก DB schema)
พร้อมสถานะ done/todo + ลำดับที่แนะนำ · **คุณนกยืนยันว่าจะทำหมดทุกฟีเจอร์**

## ⚠️ งานที่ค้างกลางทาง (ทำต่อจากตรงนี้)

1. **ฟีเจอร์ถัดไปที่ตกลงกัน (เรียงความคุ้ม):**
   - (a) **Dictionary คู่ "คำผิด→คำถูก"** — อัปเกรดจาก list คำเป็นคู่แทนที่ (Wispr: to_replace_dictionary) แก้คำสะกดผิดรายวันได้ตรงจุด **← แนะนำเริ่มอันนี้**
   - (b) **เรียนรู้สไตล์จากการแก้ของผู้ใช้** (ของเด็ด Wispr — divergence score)
   - (c) Context-aware tone (Phase 3)
   - รายละเอียดทั้งหมดใน roadmap
2. **แยก error "no API key" ออกจาก "no audio detected"** (nice-to-have, ยังไม่ทำ)

## เสร็จแล้ววันนี้ (ยังไม่ commit ทั้งหมด — คุณนกยังไม่สั่ง commit)

- **จูน prompt จากประโยคจริงของคุณนก**: ฝัง few-shot 3 ประโยค (การ์ด Take Home / เนย 50 g / สรุป section ใน Claude Code) + กติกาหน่วย ("ห้าสิบกรัม"→"50 g") ทั้งใน `Sources/TextCorrectionService.swift` และ `shared/correction-prompt.md` — **ทดสอบแล้วผ่านสวย** (Take Home, Micro Level, Option A ออกถูกหมด)
- **Dictionary UI ใน Settings** (build แล้ว ใช้ได้): `Sources/DictionaryStore.swift` อ่าน/เขียน `shared/dictionary.json` (ไฟล์เดียวกับ WhisperWin; fallback ~/.whisperapp/dictionary.json ถ้าแอปอยู่นอก repo) + section ใหม่ใน `SettingsView.swift` (เพิ่ม/ลบคำเอง) — dictionary ตอนนี้ 7 คำ (เพิ่ม Take Home, Micro Level, Content Direction)
- **History (build + test ผ่านแล้ว ✅)**: `Sources/HistoryView.swift` (HistoryStore + HistoryView, ~/.whisperapp/history.json cap 500, ค้นหา/คลิก copy/Clear All) + เมนู "History…" (⌘H) ใน AppDelegate + `HistoryStore.append` ใน DictationController — **คุณนกทดสอบแล้ว History ขึ้น + paste ลงช่องได้ปกติ**
- **Self-signed cert ใช้งานได้จริง**: build ด้วย cert "WhisperApp Dev" สำเร็จ ลายเซ็นคงที่แล้ว — สิทธิ์ Accessibility ไม่หลุดข้าม rebuild อีก ✅
- **แก้บั๊ก modifier ซ้าย/ขวา**: `HotkeyManager.swift` เช็ค keyCode แล้ว (⌃ ขวา = 62 ไม่ติด ⌃ ซ้าย = 59) + แสดง "(Right)" ใน Settings — **hotkey ปัจจุบันของคุณนก = Right Control** (Fn ใช้ไม่ได้เพราะ Wispr Flow ที่ยังรันอยู่กิน Fn ไป — ถ้าเลิก Wispr ค่อยตั้งกลับ)
- **Self-signed cert "WhisperApp Dev"** import เข้า login keychain แล้ว (สร้างจาก scratchpad, valid) + `make_app.sh` มี fallback chain: Developer ID → WhisperApp Dev → ad-hoc. หมายเหตุ: เครื่องนี้**ไม่มี** Developer ID cert (0 identities — v1.2 เคย notarize ได้ แสดงว่า cert หาย/อยู่เครื่องอื่น ต้องเช็คก่อนออก v1.3)
- **แก้พิษ CRLF จาก OneDrive/Windows**: ไฟล์ Mac ทั้งหมดโดนแปลง CRLF (ทำ run.sh พัง "bad interpreter ^M") — แปลงกลับ LF หมดแล้ว + เพิ่ม `.gitattributes` บังคับ LF (*.sh, *.swift, shared/) กันซ้ำ · codesign เจอ "detritus" ให้ `xattr -cr WhisperApp.app` (make_app.sh ทำให้แล้วใน branch cert)
- **Groq API key**: คุณนกสร้างและใส่ใน Settings แล้ว (เก็บที่ ~/.whisperapp/stt_groq.key) — ฟรี tier พอใช้สบาย (~2,000 req/วัน)
- ตรวจ Wispr Flow ให้: ของแท้ notarized, dialog ขอ keychain คือ "Safe Storage" ของ Electron เอง ไม่อันตราย

## บทเรียน debugging วันนี้ (กันเสียเวลาซ้ำ)

- "No audio detected" = STT ตอบว่าง ซึ่งรวมกรณี**ไม่มี API key** (ควรแยก error message — ยังไม่ได้ทำ, nice-to-have)
- แตะสิทธิ์ TCC เมื่อไหร่ → **restart แอปเสมอ** (NSEvent global monitor ตายเงียบ)
- สิทธิ์ค้างกับลายเซ็นเก่า → `tccutil reset Accessibility com.game.whisperapp` แล้วให้ macOS ถามใหม่
- p12 จาก OpenSSL 3.x ต้อง export แบบ `-legacy` ไม่งั้น Keychain import ไม่ได้ (MAC verification failed)

## คิวงานถัดไป (ตามที่คุณนกอยากได้ = Wispr parity)

1. Build + ทดสอบ History (ค้างอยู่ข้อ 1 ด้านบน)
2. แยก error "no API key" ออกจาก "no audio"
3. Phase 3: context-aware tone (ปรับโทนตามแอปที่โฟกัส)
4. ฝั่ง Windows: WhisperWin เขียนเสร็จ Phase 1 แล้ว (ดู `windows/README.md`) ยังไม่ได้ `dotnet publish`/ทดสอบจริงบนเครื่อง Windows · Dictionary จะ sync อัตโนมัติผ่าน `shared/dictionary.json`
5. Subagent 3 ตัวที่คุณนกเคยสร้าง: **ไม่อยู่ในโปรเจกต์** — น่าจะอยู่ `C:\Users\<ชื่อ>\.claude\agents\` บนเครื่อง Windows (user-level ไม่ sync) ถ้าจะใช้สองเครื่องให้ copy มาไว้ `.claude/agents/` ใน repo
6. เรื่อง license: repo เป็น fork ของ Gamezxz ไม่มี LICENSE — ใช้ส่วนตัวโอเค ไม่แจกจ่าย (คุณนกรับทราบแล้ว)

## ข้อควรระวัง (เหมือนเดิม)

- อย่า rename repo/bundle id · อย่า commit/push จนกว่าคุณนกสั่ง · ระวังเครื่อง Windows แปลง CRLF (มี .gitattributes กันแล้วแต่ยังไม่ commit)
