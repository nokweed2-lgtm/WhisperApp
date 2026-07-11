import SwiftUI

struct SettingsView: View {
    // Hotkey
    @State private var hotkeyConfig = HotkeyManager.shared.currentConfig
    @State private var isRecordingHotkey = false

    // Groq — single key used for both STT and AI correction
    @State private var groqKey = ""
    @State private var groqMsg = ""

    // Custom dictionary
    @State private var dictEntries: [String] = []
    @State private var newDictWord = ""

    // Word replacement pairs (wrong → right)
    @State private var dictPairs: [Pair] = []
    @State private var newWrong = ""
    @State private var newRight = ""

    // Learned examples (Learn-from-Edits) — taught from History, injected as few-shot
    @State private var learnedExamples: [Example] = []

    private var sttProvider: STTProvider { STTRegistry.provider(id: "groq") }
    private var llmProvider: LLMProvider { LLMRegistry.provider(id: "groq") }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Whisper Settings")
                    .font(.title3).bold()

                // ── Hotkey ──
                VStack(alignment: .leading, spacing: 8) {
                    Label("Global Hotkey", systemImage: "keyboard")
                        .font(.subheadline).bold()

                    HStack(spacing: 12) {
                        Text("Shortcut:").font(.caption)
                        HotkeyRecorderView(hotkey: $hotkeyConfig, isRecording: $isRecordingHotkey)
                            .frame(width: 180, height: 30)
                        Button(isRecordingHotkey ? "Listening…" : "Change") {
                            isRecordingHotkey.toggle()
                        }
                        .disabled(isRecordingHotkey)
                        Button("Reset") {
                            hotkeyConfig = .default
                            HotkeyManager.shared.updateConfig(hotkeyConfig)
                        }
                    }

                    Toggle("Hold to talk (press & hold to record, release to stop)", isOn: $hotkeyConfig.isHoldMode)
                        .font(.caption)
                        .onChange(of: hotkeyConfig.isHoldMode) { _ in
                            HotkeyManager.shared.updateConfig(hotkeyConfig)
                        }

                    Text("Toggle mode: double-tap to start, single tap to stop (modifier keys like Fn) · Hold mode: press and hold to record")
                        .font(.caption2).foregroundColor(.secondary)
                }
                .onChange(of: hotkeyConfig.keyCode) { _ in HotkeyManager.shared.updateConfig(hotkeyConfig) }
                .onChange(of: hotkeyConfig.modifiers) { _ in HotkeyManager.shared.updateConfig(hotkeyConfig) }

                Divider()

                // ── Groq (STT + AI correction) ──
                VStack(alignment: .leading, spacing: 8) {
                    Label("Groq API Key", systemImage: "key.fill")
                        .font(.subheadline).bold()

                    Text("Used for both transcription (\(sttProvider.defaultModel)) and AI correction (\(llmProvider.defaultModel))")
                        .font(.caption).foregroundColor(.secondary)

                    SecureField("gsk_…", text: $groqKey)
                        .textFieldStyle(.roundedBorder)

                    HStack {
                        Button("Save") { saveKey() }.buttonStyle(.borderedProminent)
                        Button("Test") { testKey() }
                        if !groqMsg.isEmpty { Text(groqMsg).font(.caption) }
                    }

                    Text("Get a free key at console.groq.com · Or set GROQ_API_KEY in ~/.zshrc to skip entering a key")
                        .font(.caption2).foregroundColor(.secondary)
                }

                Divider()

                // ── Custom Dictionary ──
                VStack(alignment: .leading, spacing: 8) {
                    Label("Custom Dictionary", systemImage: "character.book.closed")
                        .font(.subheadline).bold()

                    Text("Names and terms that must always be spelled exactly as written · shared with the Windows app")
                        .font(.caption).foregroundColor(.secondary)

                    HStack {
                        TextField("Add a word or name…", text: $newDictWord)
                            .textFieldStyle(.roundedBorder)
                            .onSubmit { addDictWord() }
                        Button("Add") { addDictWord() }
                            .disabled(newDictWord.trimmingCharacters(in: .whitespaces).isEmpty)
                    }

                    ForEach(dictEntries, id: \.self) { word in
                        HStack {
                            Text(word).font(.callout)
                            Spacer()
                            Button { removeDictWord(word) } label: {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundColor(.secondary)
                            }
                            .buttonStyle(.plain)
                            .help("Remove \"\(word)\"")
                        }
                        .padding(.vertical, 3).padding(.horizontal, 8)
                        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 6))
                    }
                }

                Divider()

                // ── Word replacements (wrong → right pairs) ──
                VStack(alignment: .leading, spacing: 8) {
                    Label("Word replacements", systemImage: "arrow.left.arrow.right")
                        .font(.subheadline).bold()

                    Text("คู่คำที่ฟังผิดซ้ำๆ จนเดาไม่ได้จากบริบท (เช่น \"ติมสแกน\" → \"Theme Scan\") · shared with the Windows app")
                        .font(.caption).foregroundColor(.secondary)

                    HStack {
                        TextField("คำที่ฟังผิด", text: $newWrong)
                            .textFieldStyle(.roundedBorder)
                        Image(systemName: "arrow.right")
                            .foregroundColor(.secondary)
                        TextField("คำที่ถูก", text: $newRight)
                            .textFieldStyle(.roundedBorder)
                            .onSubmit { addDictPair() }
                        Button("Add") { addDictPair() }
                            .disabled(newWrong.trimmingCharacters(in: .whitespaces).isEmpty
                                      || newRight.trimmingCharacters(in: .whitespaces).isEmpty)
                    }

                    ForEach(dictPairs, id: \.self) { pair in
                        HStack {
                            Text("\(pair.toReplace) → \(pair.replaceWith)").font(.callout)
                            Spacer()
                            Button { removeDictPair(pair) } label: {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundColor(.secondary)
                            }
                            .buttonStyle(.plain)
                            .help("Remove \"\(pair.toReplace) → \(pair.replaceWith)\"")
                        }
                        .padding(.vertical, 3).padding(.horizontal, 8)
                        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 6))
                    }
                }

                Divider()

                // ── Learned examples (Learn-from-Edits) ──
                VStack(alignment: .leading, spacing: 8) {
                    Label("บทเรียนที่สอน", systemImage: "graduationcap")
                        .font(.subheadline).bold()

                    Text("ตัวอย่างที่สอนจากการแก้ใน History (สอนเป็นตัวอย่าง) — ใช้ 5 รายการล่าสุดตอนแก้คำ")
                        .font(.caption).foregroundColor(.secondary)

                    if learnedExamples.isEmpty {
                        Text("ยังไม่มีบทเรียน — ไปแก้คำใน History แล้วกด \"สอนเป็นตัวอย่าง\"")
                            .font(.caption2).foregroundColor(.secondary)
                    }

                    ForEach(learnedExamples, id: \.created) { example in
                        HStack {
                            Text("\(example.raw) → \(example.corrected)").font(.callout)
                            Spacer()
                            Button { removeLearnedExample(example) } label: {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundColor(.secondary)
                            }
                            .buttonStyle(.plain)
                            .help("Remove \"\(example.raw) → \(example.corrected)\"")
                        }
                        .padding(.vertical, 3).padding(.horizontal, 8)
                        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 6))
                    }
                }

                Spacer(minLength: 0)
            }
            .padding(20)
        }
        .frame(width: 460, height: 560)
        .onAppear {
            loadKey()
            dictEntries = DictionaryStore.load()
            dictPairs = DictionaryStore.loadPairs()
            learnedExamples = LearnedExamplesStore.load()
        }
    }

    // MARK: Custom dictionary
    private func addDictWord() {
        let w = newDictWord.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !w.isEmpty, !dictEntries.contains(w) else { newDictWord = ""; return }
        dictEntries.append(w)
        DictionaryStore.saveEntries(dictEntries)
        newDictWord = ""
    }

    private func removeDictWord(_ w: String) {
        dictEntries.removeAll { $0 == w }
        DictionaryStore.saveEntries(dictEntries)
    }

    // MARK: Word replacement pairs
    private func addDictPair() {
        let wrong = newWrong.trimmingCharacters(in: .whitespacesAndNewlines)
        let right = newRight.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !wrong.isEmpty, !right.isEmpty else { return }
        dictPairs.append(Pair(toReplace: wrong, replaceWith: right))
        DictionaryStore.savePairs(dictPairs)
        newWrong = ""
        newRight = ""
    }

    private func removeDictPair(_ pair: Pair) {
        dictPairs.removeAll { $0 == pair }
        DictionaryStore.savePairs(dictPairs)
    }

    // MARK: Learned examples
    private func removeLearnedExample(_ example: Example) {
        learnedExamples.removeAll { $0 == example }
        LearnedExamplesStore.remove(example)
    }

    // MARK: Groq key (shared by STT + LLM)
    private func loadKey() {
        groqKey = STTSettings.savedKeyFile(for: sttProvider)
        if groqKey.isEmpty {
            groqKey = (try? String(contentsOfFile: KeyStore.dir + "/llm_groq.key", encoding: .utf8))?
                .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        }
    }

    private func applyKey() {
        STTSettings.providerID = "groq"
        LLMSettings.providerID = "groq"
        let t = groqKey.trimmingCharacters(in: .whitespacesAndNewlines)
        if !t.isEmpty {
            STTSettings.saveKey(t, for: sttProvider)
            LLMSettings.saveKey(t, for: llmProvider)
        }
    }

    private func saveKey() {
        applyKey()
        groqMsg = "✅ Saved"
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) { groqMsg = "" }
    }

    private func testKey() {
        applyKey()
        guard STTSettings.key(for: sttProvider) != nil else { groqMsg = "⚠️ Enter API key first"; return }

        groqMsg = "⏳ Testing…"
        guard let url = URL(string: "https://api.groq.com/openai/v1/models") else { return }
        var req = URLRequest(url: url)
        req.setValue("Bearer \(STTSettings.key(for: sttProvider)!)", forHTTPHeaderField: "Authorization")
        URLSession.shared.dataTask(with: req) { _, resp, _ in
            let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
            DispatchQueue.main.async {
                groqMsg = code == 200 ? "✅ Key is valid" : "❌ Invalid key (code \(code))"
            }
        }.resume()
    }
}
