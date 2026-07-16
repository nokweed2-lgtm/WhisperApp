import Foundation
import AppKit
import Combine
import Carbon.HIToolbox
import ApplicationServices

/// Visual processing stage — drives the floating status overlay
enum Stage: Equatable {
    case idle
    case recording
    case transcribing
    case correcting
    case done(String)
    case error(String)
}

/// Orchestrates everything: record → transcribe (cloud/local) → correct (LLM) → paste into focused app
class DictationController: ObservableObject {
    @Published var isRecording = false
    @Published var status = ""
    @Published var stage: Stage = .idle
    @Published var useCloudSTT = true
    @Published var useCorrection = true
    @Published var language = "th"

    let recorder = AudioRecorder()
    private let whisper = WhisperService()
    private let cloud = CloudTranscriptionService()
    private let correction = TextCorrectionService()
    private var processing = false
    private var cancellables = Set<AnyCancellable>()

    init() {
        recorder.$recordedFileURL
            .compactMap { $0 }
            .receive(on: DispatchQueue.main)
            .sink { [weak self] url in self?.handleAudio(url) }
            .store(in: &cancellables)
    }

    func toggle() { recorder.isRecording ? stop() : start() }

    func start() {
        guard !processing, !recorder.isRecording else { return }
        recorder.startRecording()
        isRecording = recorder.isRecording
        if isRecording {
            status = "Listening…"
            stage = .recording
        } else {
            status = "❌ Microphone unavailable"
            stage = .error("Microphone unavailable")
        }
    }

    func stop() {
        guard recorder.isRecording else { return }
        recorder.stopRecording()
        isRecording = false
        status = "⏳ Processing…"
        stage = .transcribing
    }

    private func handleAudio(_ url: URL) {
        processing = true
        let lang = language

        let finishOnMain: (String, String) -> Void = { [weak self] text, raw in
            DispatchQueue.main.async {
                guard let self = self else { return }
                let snippet = String(text.prefix(28))
                self.status = "✅ " + snippet
                self.stage = .done(snippet)
                // เคลียร์ processing หลัง paste (รวม restore clipboard) เสร็จจริงเท่านั้น
                // ไม่งั้น dictation รอบถัดไปจะเริ่มระหว่าง restore แล้ว snapshot ผิดตัว (เก็บคำที่เพิ่ง dictate แทนของเดิมผู้ใช้)
                Paster.paste(text) { [weak self] in
                    DispatchQueue.main.async { self?.processing = false }
                }
                HistoryStore.append(text: text, raw: raw)
                // กลับเป็น idle หลังโชว์สักครู่
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { [weak self] in
                    guard let self = self else { return }
                    if self.stage == .done(snippet) { self.stage = .idle }
                }
            }
        }

        let afterSTT: (String?) -> Void = { [weak self] result in
            guard let self = self else { return }
            // ลบคำบรรยายเสียง/เหตุการณ์ที่ STT เติมมา เช่น (เสียงลม) (wind) [background noise]
            let text = (result.map { self.stripSoundAnnotations($0) }) ?? ""
            // บันทึกข้อความดิบจาก STT (ก่อน LLM แก้) เพื่อแยกให้ออกว่าคำเพี้ยนมาจาก Whisper หรือ LLM
            DebugLog.log("raw STT: \(result ?? "<nil>")")
            guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                DispatchQueue.main.async {
                    self.status = "⚠️ No audio detected"
                    self.stage = .error("No audio detected")
                    self.processing = false
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in
                        if self?.stage == .error("No audio detected") { self?.stage = .idle }
                    }
                }
                return
            }
            if self.useCorrection {
                DispatchQueue.main.async {
                    self.status = "✨ AI correction…"
                    self.stage = .correcting
                }
                self.correction.correct(text: text, language: lang) { corrected in
                    DebugLog.log("corrected: \(corrected ?? "<nil, kept raw>")")
                    finishOnMain(corrected ?? text, text)
                }
            } else {
                DebugLog.log("corrected: <disabled, kept raw>")
                finishOnMain(text, text)
            }
        }

        DispatchQueue.main.async {
            self.status = self.useCloudSTT ? "☁️ Transcribing…" : "📝 Transcribing…"
            self.stage = .transcribing
        }

        if useCloudSTT {
            cloud.transcribe(fileURL: url, language: lang) { result in
                try? FileManager.default.removeItem(at: url)
                afterSTT(result)
            }
        } else {
            whisper.language = lang
            whisper.transcribe(fileURL: url) { result in afterSTT(result) }
        }
    }

    /// ลบคำบรรยายเสียง/เหตุการณ์ที่ STT ใส่มา เช่น (เสียงลม) (wind noise) [applause] *laughs*
    /// แบบที่ ElevenLabs Scribe และ Whisper มักแทรกเข้ามา
    private func stripSoundAnnotations(_ text: String) -> String {
        var result = text
        let patterns = [
            "\\([^\\)]*\\)",   // ( ... )   ASCII
            "（[^）]*）",         // （ ... ） fullwidth
            "\\[[^\\]]*\\]",   // [ ... ]
            "【[^】]*】",         // 【 ... 】
            "\\*[^*]*\\*",      // * ... *
            "‹[^›]*›",           // ‹ ... ›
            "«[^»]*»",          // « ... »
        ]
        for p in patterns {
            result = result.replacingOccurrences(of: p, with: " ", options: .regularExpression)
        }
        // กรณีคำบรรยายไม่มีวงเล็บปิด (เช่น "(เสียงลม" ค้าง) ลบคำที่ขึ้นต้นด้วย "เสียง" ที่ค้าง
        // ยุบช่องว่างซ้อน และตัดปีกกะไร
        result = result
            .replacingOccurrences(of: "\\s{2,}", with: " ", options: .regularExpression)
            .replacingOccurrences(of: "\\s+([,.!?])", with: "$1", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return result
    }
}

/// Copy text to clipboard and simulate ⌘V into the focused app (requires Accessibility permission)
enum Paster {
    private static var didPrompt = false

    /// - Parameter completion: เรียกเมื่อ paste (รวม restore clipboard ถ้ามี) เสร็จสมบูรณ์แล้วเท่านั้น
    ///   ผู้เรียกที่ gate ด้วย flag แบบ `processing` ต้องรอ completion นี้ก่อนปล่อยให้เริ่มรอบใหม่ได้
    ///   ไม่งั้น snapshot ของรอบถัดไปจะเก็บคำที่เพิ่ง dictate แทนที่จะเป็นของเดิมผู้ใช้
    static func paste(_ text: String, completion: @escaping () -> Void) {
        let pb = NSPasteboard.general

        // ไม่มีสิทธิ์ Accessibility → เก็บใน clipboard เงียบๆ ผู้ใช้กด ⌘V เอง
        // (ห้ามเด้ง dialog ตรงนี้ จะวนระหว่าง transcribe ไม่หยุด)
        // กรณีนี้ห้าม restore — ไม่งั้นผู้ใช้จะไม่เหลืออะไรให้กด ⌘V เอง
        guard AXIsProcessTrusted() else {
            pb.clearContents()
            pb.setString(text, forType: .string)
            completion()
            return
        }

        // มีสิทธิ์ auto-paste → เก็บ snapshot ของ clipboard เดิมไว้ก่อน แล้วค่อย restore กลับทีหลัง
        // ต้อง copy data ของแต่ละ type ออกมาเป็น NSPasteboardItem ใหม่ เพราะ item เดิมเขียนซ้ำไม่ได้
        let saved: [NSPasteboardItem] = (pb.pasteboardItems ?? []).compactMap { item in
            let copy = NSPasteboardItem()
            var hasData = false
            for type in item.types {
                if let data = item.data(forType: type) {
                    copy.setData(data, forType: type)
                    hasData = true
                }
            }
            return hasData ? copy : nil
        }

        pb.clearContents()
        pb.setString(text, forType: .string)

        // Small delay to ensure clipboard is set before simulating keystroke
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
            let src = CGEventSource(stateID: .combinedSessionState)
            let v = CGKeyCode(kVK_ANSI_V)
            let down = CGEvent(keyboardEventSource: src, virtualKey: v, keyDown: true)
            down?.flags = .maskCommand
            let up = CGEvent(keyboardEventSource: src, virtualKey: v, keyDown: false)
            up?.flags = .maskCommand
            down?.post(tap: .cghidEventTap)
            up?.post(tap: .cghidEventTap)

            // รอให้แอปปลายทางอ่าน clipboard เสร็จก่อน ค่อย restore ของเดิมกลับ (มิเรอร์ RestoreDelay 300ms ฝั่ง Windows)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                pb.clearContents()
                if !saved.isEmpty {
                    pb.writeObjects(saved)
                }
                completion()
            }
        }
    }

    /// ถามสิทธิ์ Accessibility แค่ครั้งเดียวต่อ session (เรียกตอนเปิดแอป)
    static func promptAccessibilityOnce() {
        guard !didPrompt, !AXIsProcessTrusted() else { return }
        didPrompt = true
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }
}
