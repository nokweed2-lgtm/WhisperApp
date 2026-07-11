import Foundation

/// Permanent debug log at ~/.whisperapp/debug.log — parity with the Windows sibling's
/// %LOCALAPPDATA%\WhisperWin\debug.log. Intentionally always-on: the dictation pipeline has
/// silent paths (STT returns empty, LLM leaves a valid-but-wrong word untouched) that can only be
/// diagnosed by seeing the raw STT text vs the corrected text side by side after the fact.
///
/// Best-effort: any failure is swallowed so logging can never break dictation.
enum DebugLog {
    private static let queue = DispatchQueue(label: "whisperapp.debuglog")
    private static var path: String { KeyStore.dir + "/debug.log" }
    private static let maxBytes = 1_000_000  // reset when it grows past ~1 MB

    private static let stamp: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd HH:mm:ss"
        // ล็อก locale/calendar ให้เป็นเกรกอเรียนเสมอ ไม่งั้นระบบที่ตั้ง locale เป็นไทย
        // จะโชว์ปีพุทธศักราช (เช่น 2569 แทน 2026)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.calendar = Calendar(identifier: .gregorian)
        return f
    }()

    static func log(_ message: String) {
        queue.async {
            let line = "[\(stamp.string(from: Date()))] \(message)\n"
            let fm = FileManager.default
            try? fm.createDirectory(atPath: KeyStore.dir, withIntermediateDirectories: true)

            // keep the file bounded so it never grows without limit
            if let attrs = try? fm.attributesOfItem(atPath: path),
               let size = attrs[.size] as? Int, size > maxBytes {
                try? fm.removeItem(atPath: path)
            }

            guard let data = line.data(using: .utf8) else { return }
            if let handle = FileHandle(forWritingAtPath: path) {
                defer { try? handle.close() }
                _ = try? handle.seekToEnd()
                try? handle.write(contentsOf: data)
            } else {
                try? data.write(to: URL(fileURLWithPath: path))
            }
        }
    }
}
