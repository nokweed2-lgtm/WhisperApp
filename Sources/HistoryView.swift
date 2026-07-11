import SwiftUI
import AppKit

// MARK: - Storage

struct HistoryEntry: Codable, Identifiable, Equatable {
    let id: UUID
    let date: Date
    let text: String
    /// STT text *before* LLM correction (the effective input to the correction step). Nullable
    /// so history.json entries written before this field existed still decode fine — nil means
    /// "unknown," and callers fall back to `text` as the "before" for teaching purposes.
    var raw: String? = nil
}

/// Local dictation history — ~/.whisperapp/history.json, capped at 500 entries.
enum HistoryStore {
    private static let path = KeyStore.dir + "/history.json"
    private static let maxEntries = 500

    static func load() -> [HistoryEntry] {
        guard let data = FileManager.default.contents(atPath: path) else { return [] }
        let dec = JSONDecoder()
        dec.dateDecodingStrategy = .iso8601
        return (try? dec.decode([HistoryEntry].self, from: data)) ?? []
    }

    static func append(text: String, raw: String? = nil) {
        var entries = load()
        entries.append(HistoryEntry(id: UUID(), date: Date(), text: text, raw: raw))
        if entries.count > maxEntries { entries.removeFirst(entries.count - maxEntries) }
        save(entries)
    }

    /// Plain edit ("แก้เฉยๆ") — updates only the stored text, teaches nothing.
    static func updateText(id: UUID, newText: String) {
        var entries = load()
        guard let idx = entries.firstIndex(where: { $0.id == id }) else { return }
        entries[idx] = HistoryEntry(id: entries[idx].id, date: entries[idx].date, text: newText, raw: entries[idx].raw)
        save(entries)
    }

    static func clear() { save([]) }

    private static func save(_ entries: [HistoryEntry]) {
        try? FileManager.default.createDirectory(atPath: KeyStore.dir, withIntermediateDirectories: true)
        let enc = JSONEncoder()
        enc.dateEncodingStrategy = .iso8601
        enc.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        guard let data = try? enc.encode(entries) else { return }
        try? data.write(to: URL(fileURLWithPath: path))
    }
}

// MARK: - View

struct HistoryView: View {
    @State private var entries: [HistoryEntry] = []
    @State private var filter = ""
    @State private var copiedID: UUID?

    // Learn-from-Edits: inline editing state for the row currently expanded
    @State private var expandedID: UUID?
    @State private var editText = ""

    // "บันทึกเป็นกฎ" — tiny sheet asking for the exact word pair (no auto-diffing the sentence)
    @State private var showRuleSheet = false
    @State private var ruleWrong = ""
    @State private var ruleRight = ""

    private var shown: [HistoryEntry] {
        let list = Array(entries.reversed())
        guard !filter.isEmpty else { return list }
        return list.filter { $0.text.localizedCaseInsensitiveContains(filter) }
    }

    var body: some View {
        VStack(spacing: 10) {
            HStack {
                TextField("Search history…", text: $filter)
                    .textFieldStyle(.roundedBorder)
                Button("Clear All") {
                    HistoryStore.clear()
                    entries = []
                }
                .disabled(entries.isEmpty)
            }

            if shown.isEmpty {
                Spacer()
                Text(entries.isEmpty ? "No history yet — dictate something!" : "No matches")
                    .foregroundColor(.secondary)
                Spacer()
            } else {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 6) {
                        ForEach(shown) { entry in row(entry) }
                    }
                }
            }
        }
        .padding(14)
        .frame(width: 480, height: 500)
        .onAppear { entries = HistoryStore.load() }
        .sheet(isPresented: $showRuleSheet) { ruleSheet }
    }

    @ViewBuilder
    private func row(_ entry: HistoryEntry) -> some View {
        if expandedID == entry.id {
            editingRow(entry)
        } else {
            collapsedRow(entry)
        }
    }

    private func collapsedRow(_ entry: HistoryEntry) -> some View {
        HStack(alignment: .top, spacing: 4) {
            Button {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(entry.text, forType: .string)
                copiedID = entry.id
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    if copiedID == entry.id { copiedID = nil }
                }
            } label: {
                VStack(alignment: .leading, spacing: 3) {
                    HStack {
                        Text(Self.fmt.string(from: entry.date))
                            .font(.caption2).foregroundColor(.secondary)
                        Spacer()
                        if copiedID == entry.id {
                            Label("Copied", systemImage: "checkmark")
                                .font(.caption2).foregroundColor(.green)
                        } else {
                            Image(systemName: "doc.on.doc")
                                .font(.caption2).foregroundColor(.secondary)
                        }
                    }
                    Text(entry.text)
                        .font(.callout)
                        .multilineTextAlignment(.leading)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("Click to copy")

            Button {
                editText = entry.text
                expandedID = entry.id
            } label: {
                Image(systemName: "pencil")
                    .font(.caption2).foregroundColor(.secondary)
            }
            .buttonStyle(.plain)
            .help("Edit")
        }
        .padding(8)
        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 8))
    }

    /// Expanded edit state — the three Learn-from-Edits actions (แก้เฉยๆ / บันทึกเป็นกฎ / สอนเป็นตัวอย่าง)
    private func editingRow(_ entry: HistoryEntry) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(Self.fmt.string(from: entry.date))
                .font(.caption2).foregroundColor(.secondary)

            TextEditor(text: $editText)
                .font(.callout)
                .frame(minHeight: 60, maxHeight: 120)
                .padding(4)
                .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 6))

            Text("สอนเป็นตัวอย่าง: ใช้กับการแก้ที่ควรเป็นแบบนี้ทุกครั้ง — อย่าสอนคำเสียงพ้องที่เพี้ยนแบบสุ่ม (เช่น แรงงาน/รายงาน)")
                .font(.caption2).foregroundColor(.secondary)

            HStack {
                Button("แก้เฉยๆ") {
                    let trimmed = editText.trimmingCharacters(in: .whitespacesAndNewlines)
                    guard !trimmed.isEmpty else { return }
                    HistoryStore.updateText(id: entry.id, newText: editText)
                    entries = HistoryStore.load()
                    expandedID = nil
                }
                Button("บันทึกเป็นกฎ") {
                    ruleWrong = ""
                    ruleRight = ""
                    showRuleSheet = true
                }
                Button("สอนเป็นตัวอย่าง") {
                    let trimmed = editText.trimmingCharacters(in: .whitespacesAndNewlines)
                    let rawValue = entry.raw ?? entry.text
                    // ข้าม no-op: ถ้าข้อความที่แก้เหมือน raw เป๊ะ การสอนจะเปลืองสล็อต 5 ตัวล่าสุด
                    // ไปกับบทเรียนที่ไม่มีความหมาย (สอนให้โมเดลพ่นสิ่งที่มันพ่นออกมาเองซ้ำ)
                    guard !trimmed.isEmpty,
                          trimmed != rawValue.trimmingCharacters(in: .whitespacesAndNewlines) else { return }
                    LearnedExamplesStore.add(raw: rawValue, corrected: editText)
                    HistoryStore.updateText(id: entry.id, newText: editText)
                    entries = HistoryStore.load()
                    // ปิด editor ทันทีหลังสอนสำเร็จ กันแตะซ้ำโดยไม่ตั้งใจแล้วสอนซ้ำสอง
                    expandedID = nil
                }
                Spacer()
                Button("Cancel") { expandedID = nil }
            }
            .font(.caption)
        }
        .padding(8)
        .background(Color.accentColor.opacity(0.08), in: RoundedRectangle(cornerRadius: 8))
    }

    /// Tiny two-field sheet for "บันทึกเป็นกฎ" — user types the exact word pair; we never
    /// auto-diff the sentence (fragile for Thai).
    private var ruleSheet: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("บันทึกเป็นกฎ").font(.headline)
            Text("คำที่ฟังผิด → คำที่ถูก จะถูกแทนที่แบบนี้ทุกครั้งที่พบ")
                .font(.caption).foregroundColor(.secondary)

            HStack {
                TextField("คำที่ฟังผิด", text: $ruleWrong)
                    .textFieldStyle(.roundedBorder)
                Image(systemName: "arrow.right").foregroundColor(.secondary)
                TextField("คำที่ถูก", text: $ruleRight)
                    .textFieldStyle(.roundedBorder)
            }

            HStack {
                Spacer()
                Button("Cancel") { showRuleSheet = false }
                Button("Save") {
                    let wrong = ruleWrong.trimmingCharacters(in: .whitespacesAndNewlines)
                    let right = ruleRight.trimmingCharacters(in: .whitespacesAndNewlines)
                    guard !wrong.isEmpty, !right.isEmpty else { return }
                    var pairs = DictionaryStore.loadPairs()
                    pairs.append(Pair(toReplace: wrong, replaceWith: right))
                    DictionaryStore.savePairs(pairs)
                    showRuleSheet = false
                    expandedID = nil
                }
                .buttonStyle(.borderedProminent)
                .disabled(ruleWrong.trimmingCharacters(in: .whitespaces).isEmpty
                          || ruleRight.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
        .frame(width: 360)
    }

    private static let fmt: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "d MMM · HH:mm"
        return f
    }()
}
