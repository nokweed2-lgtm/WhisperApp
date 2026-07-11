import Foundation

/// A learned correction example — a "before → after" pair the user opted to teach the app from
/// a History edit. Opt-in only (never auto-learned). Shared with the Windows sibling app via
/// shared/learned-examples.json ({ "raw", "corrected", "created" }).
struct Example: Codable, Equatable {
    var raw: String
    var corrected: String
    var created: String
}

/// Learned examples ("Learn-from-Edits") — few-shot corrections the user explicitly taught by
/// editing a History entry and tapping "สอนเป็นตัวอย่าง". Stored in <repo>/shared/learned-examples.json
/// so the Windows sibling app (WhisperWin) can eventually read the same list. Falls back to
/// ~/.whisperapp/learned-examples.json when the app runs outside the repo (e.g. installed in
/// /Applications). Mirrors DictionaryStore.swift's structure and safety guarantees.
enum LearnedExamplesStore {
    /// Store cap — keep at most this many examples on disk (drop oldest) to bound file size.
    private static let maxEntries = 200

    private struct FileFormat: Codable { var examples: [Example] }

    /// Walk up from the app bundle looking for the repo's shared/learned-examples.json
    private static func sharedPath() -> String? {
        var dir = URL(fileURLWithPath: Bundle.main.bundlePath).deletingLastPathComponent()
        for _ in 0..<5 {
            let candidate = dir.appendingPathComponent("shared/learned-examples.json")
            if FileManager.default.fileExists(atPath: candidate.path) { return candidate.path }
            dir.deleteLastPathComponent()
        }
        return nil
    }

    private static var fallbackPath: String { KeyStore.dir + "/learned-examples.json" }

    static var activePath: String { sharedPath() ?? fallbackPath }

    /// Outcome of attempting to read the on-disk file — distinguishes "nothing there yet"
    /// (normal first run, safe to seed) from "something is there but we couldn't read it"
    /// (OneDrive sync lock, a crashed write, corruption — must NOT be treated as empty, or a
    /// read-modify-write save would silently overwrite the user's real data with the seed).
    private enum ReadResult {
        case ok(FileFormat)
        case missing
        case unreadable
    }

    private static func attemptRead() -> ReadResult {
        guard FileManager.default.fileExists(atPath: activePath) else { return .missing }
        guard let data = FileManager.default.contents(atPath: activePath) else { return .unreadable }
        guard let file = try? JSONDecoder().decode(FileFormat.self, from: data) else { return .unreadable }
        return .ok(file)
    }

    /// Reads the file for display/use (correction prompt assembly, populating Settings UI).
    /// Falls back to an empty list on any failure — including "unreadable" — so a locked/corrupt
    /// file never blocks dictation; it just temporarily loses the learned examples until it's
    /// readable again. This is a read-only path, so falling back here can't destroy anything on disk.
    private static func readFile() -> FileFormat {
        if case .ok(let file) = attemptRead() { return file }
        return FileFormat(examples: [])
    }

    private static func writeFile(_ file: FileFormat) {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        guard let data = try? enc.encode(file) else { return }
        if sharedPath() == nil {
            try? FileManager.default.createDirectory(atPath: KeyStore.dir, withIntermediateDirectories: true)
        }
        try? data.write(to: URL(fileURLWithPath: activePath))
    }

    private static let isoFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        // Fractional seconds so two teaches in the same second still get distinct `created`
        // values — SettingsView uses `created` as a ForEach identity, and a collision there
        // triggers a SwiftUI duplicate-ID warning. Still a valid ISO8601 string for Windows to parse.
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    /// All learned examples, oldest first.
    static func load() -> [Example] { readFile().examples }

    /// Most recent `n` examples (newest last, matching `load()`'s ordering) — used for injection
    /// into the correction prompt. No divergence scoring in v1 — recency only.
    static func recent(_ n: Int = 5) -> [Example] {
        let all = load()
        guard all.count > n else { return all }
        return Array(all.suffix(n))
    }

    /// Appends a new example with the current time as ISO8601 `created`, enforcing the 200 cap
    /// (drops the oldest entries first).
    ///
    /// Safety: if the file exists but can't be read/decoded right now (sync lock, partial write,
    /// corruption), this is a no-op — writing a seed back would silently delete the user's real
    /// examples. Only "file doesn't exist yet" (first run) is treated as a blank slate.
    static func add(raw: String, corrected: String) {
        let example = Example(raw: raw, corrected: corrected, created: isoFormatter.string(from: Date()))
        switch attemptRead() {
        case .unreadable:
            return
        case .missing:
            writeFile(FileFormat(examples: [example]))
        case .ok(var file):
            file.examples.append(example)
            if file.examples.count > maxEntries {
                file.examples.removeFirst(file.examples.count - maxEntries)
            }
            writeFile(file)
        }
    }

    /// Removes a learned example (Settings management UI). No-op if the file is unreadable, for
    /// the same reason as `add`.
    static func remove(_ example: Example) {
        switch attemptRead() {
        case .unreadable, .missing:
            return
        case .ok(var file):
            file.examples.removeAll { $0 == example }
            writeFile(file)
        }
    }
}
