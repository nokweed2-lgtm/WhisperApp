import Foundation

/// A user-defined "wrong word → right word" replacement pair — for STT mishears that can't be
/// guessed from context (e.g. "ติมสแกน" → "Theme Scan"). Shared with the Windows sibling app via
/// shared/dictionary.json ({ "to_replace", "replace_with" }).
struct Pair: Codable, Equatable, Hashable {
    var toReplace: String
    var replaceWith: String

    enum CodingKeys: String, CodingKey {
        case toReplace = "to_replace"
        case replaceWith = "replace_with"
    }
}

/// Custom dictionary — names/terms that must always be spelled exactly as written.
/// Stored in <repo>/shared/dictionary.json so the Windows sibling app (WhisperWin)
/// uses the same list. Falls back to ~/.whisperapp/dictionary.json when the app
/// runs outside the repo (e.g. installed in /Applications).
enum DictionaryStore {
    static let seedEntries = [
        "Tar Sawang",
        "Coffee for Worker",
        "Claude Code",
        "Wispr Flow",
        "Take Home",
        "Micro Level",
        "Content Direction",
        "tier list",
    ]

    /// `pairs` is optional so files written before this feature (just `entries`) still decode.
    private struct FileFormat: Codable { var entries: [String]; var pairs: [Pair]? }

    /// Walk up from the app bundle looking for the repo's shared/dictionary.json
    private static func sharedPath() -> String? {
        var dir = URL(fileURLWithPath: Bundle.main.bundlePath).deletingLastPathComponent()
        for _ in 0..<5 {
            let candidate = dir.appendingPathComponent("shared/dictionary.json")
            if FileManager.default.fileExists(atPath: candidate.path) { return candidate.path }
            dir.deleteLastPathComponent()
        }
        return nil
    }

    private static var fallbackPath: String { KeyStore.dir + "/dictionary.json" }

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
    /// Falls back to the seed on any failure — including "unreadable" — so a locked/corrupt file
    /// never blocks dictation; it just temporarily loses the custom entries until it's readable
    /// again. This is a read-only path, so falling back here can't destroy anything on disk.
    private static func readFile() -> FileFormat {
        if case .ok(let file) = attemptRead() { return file }
        return FileFormat(entries: seedEntries, pairs: [])
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

    static func load() -> [String] {
        let file = readFile()
        return file.entries.isEmpty ? seedEntries : file.entries
    }

    static func loadPairs() -> [Pair] {
        readFile().pairs ?? []
    }

    /// Read-modify-write: replaces `entries` while preserving whatever `pairs` are on disk, so
    /// saving the word list never clobbers the replacement pairs (and vice versa in savePairs).
    ///
    /// Safety: if the file exists but can't be read/decoded right now (sync lock, partial write,
    /// corruption), this is a no-op — writing the seed back would silently delete the user's real
    /// entries/pairs. Only "file doesn't exist yet" (first run) is treated as a blank slate.
    static func saveEntries(_ entries: [String]) {
        switch attemptRead() {
        case .unreadable:
            return
        case .missing:
            writeFile(FileFormat(entries: entries, pairs: []))
        case .ok(var file):
            file.entries = entries
            writeFile(file)
        }
    }

    static func savePairs(_ pairs: [Pair]) {
        switch attemptRead() {
        case .unreadable:
            return
        case .missing:
            writeFile(FileFormat(entries: seedEntries, pairs: pairs))
        case .ok(var file):
            file.pairs = pairs
            writeFile(file)
        }
    }
}
