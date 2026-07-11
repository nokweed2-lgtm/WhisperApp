import Foundation

/// Send raw transcription text to LLM (cloud) to fix typos, punctuation, and sentence structure.
/// Supports multiple providers (DeepSeek, OpenAI, Groq, OpenRouter, Gemini, Anthropic, Custom)
/// via LLMSettings — see LLMProvider.swift
class TextCorrectionService: ObservableObject {
    @Published var isEnabled = true
    @Published var isCorrecting = false

    private var provider: LLMProvider { LLMSettings.current }
    private var apiKey: String? { LLMSettings.key(for: provider) }

    var isAvailable: Bool { apiKey != nil }

    func correct(text: String, language: String, completion: @escaping (String?) -> Void) {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            completion(nil); return
        }
        let p = provider
        guard let key = LLMSettings.key(for: p) else {
            print("❌ No key found for \(p.name) (configure in Settings or set env \(p.envKey))")
            completion(nil); return
        }

        let langHint: String
        switch language {
        case "th": langHint = "The text is primarily Thai, possibly mixed with English terms"
        case "en": langHint = "The text is in English"
        default:   langHint = "The text may be Thai, English, or mixed — keep each part in its original language"
        }

        let dictionaryList = DictionaryStore.load().map { "- \($0)" }.joined(separator: "\n")
        let replacementsBlock = Self.renderReplacements(DictionaryStore.loadPairs())
        let learnedExamplesBlock = Self.renderLearnedExamples(LearnedExamplesStore.recent(5))
        let systemPrompt = """
        You are a transcript-cleanup engine for speech-to-text output. The speaker is Thai
        and frequently code-switches between Thai and English in the same sentence.

        Rules:
        1. Fix misheard or garbled words using sound and context. Thai speech-to-text
           frequently confuses similar consonants (ร↔ล, ด↔ต, บ↔ป, ค↔ข), gets tones wrong,
           or splits/merges syllables. When a word comes out as a non-word or an
           obviously-wrong word, REPLACE it with the natural Thai word the speaker clearly
           meant (e.g. "เรียบล่อย" → "เรียบร้อย"). This is substitution of a garbled word —
           NOT permission to add anything: never add words, filler, or content the speaker
           did not say, never summarize, never answer questions in the text, and keep the
           same number of ideas the speaker expressed.
        2. Code-switching: technical terms, product names, and English loanwords must be
           written in correctly-spelled English (e.g. "คอนเวอร์ชัน" → "conversion",
           "มาร์จิ้น" → "margin", "ดีพลอย" → "deploy"). Thai content stays in Thai —
           never translate whole Thai phrases into English or vice versa.
        3. If the transcriber wrote an English word as Thai phonetic spelling, restore the
           English spelling. If it wrote Thai speech as broken English, restore the Thai.
           This applies even when the phonetic spelling is heavily garbled — if the context
           makes it clear the speaker meant an English/technical word, recover the correct
           English spelling (e.g. "ด็สบอร์ต" → "dashboard", "เด็สต์ทอป" → "desktop",
           "เซ็กชั่น" → "section"). Treat this as guidance to apply *when the context
           supports it* — do not force a Thai word that is genuinely Thai into English just
           because it sounds vaguely similar to one.
           Do NOT snap a garbled token onto a name from the custom dictionary just because
           it is the nearest known term or fits the topic — that invents a specific word the
           speaker never said. Only replace a garbled token with a dictionary name when the
           SOUND genuinely matches (similar syllables/consonants), not merely the meaning or
           context. If a garbled English-looking token does not clearly match any dictionary
           name by sound, write your best phonetic English guess of the word instead
           (e.g. "เชียริต" → "tier list", not a dictionary name it merely sounds adjacent to).
        4. Spacing: put one space between Thai and English segments so the text is easy to
           read. Within Thai text, use spaces only where Thai normally does (between clauses).
        5. Thai sentences do not end with a full stop — remove trailing periods after Thai
           text. English-only sentences may keep normal punctuation.
        6. Do not change politeness particles (ครับ/ค่ะ/นะ/ครับผม), word endings, or speaker gender.

        Custom dictionary — always spell these names exactly as written:
        \(dictionaryList)

        \(replacementsBlock)

        \(learnedExamplesBlock)

        Examples (raw transcript → corrected):
        - "การ์ดเทคโฮมต่างชาติมีไมโครเลเวลออปชั่นเอประมาณนี้"
          → "การ์ด Take Home ต่างชาติมี Micro Level Option A ประมาณนี้"
        - "เนยใช้ประมาณ 50 กรัม คอนเทนต์ไดเรคชั่นมี 3 ข้อ"
          → "เนยใช้ประมาณ 50 g Content Direction มี 3 ข้อ"
        - "รบกวนช่วยสรุปเซคชั่นในคลอดโค้ด"
          → "รบกวนช่วยสรุป section ใน Claude Code"
        - "ทำไมด็สบอร์ตของแอปในหน้าเด็สต์ทอปถึงมี 2 อัน"
          → "ทำไม dashboard ของแอปในหน้า desktop ถึงมี 2 อัน"
        - "โอเคครับ เจอเรียบล่อย"
          → "โอเคครับ เจอเรียบร้อย"
        - "ได้อ่านไฟล์ของแต่ละคนก่อนจัดเชียริตไหม"
          → "ได้อ่านไฟล์ของแต่ละคนก่อนจัด tier list ไหม"
          (→ "tier list" because the garbled token sounds like it; NOT "Wispr Flow" — that name
          is topically adjacent but the sound does not match, so a garbled token must never be
          forced onto a dictionary name it does not sound like.)
        (Note: spoken units like กรัม/กิโล become unit symbols — 50 g, 2 kg —
        and spoken numbers become digits.)

        Return ONLY the corrected text — no explanations, no quotation marks.
        \(langHint)
        """

        let endpoint = LLMSettings.endpoint(for: p)
        let model = LLMSettings.model(for: p)

        var req = URLRequest(url: endpoint)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.timeoutInterval = 60

        let body: [String: Any]
        switch p.style {
        case .openAI:
            req.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
            body = [
                "model": model,
                "temperature": 0.2,
                "messages": [
                    ["role": "system", "content": systemPrompt],
                    ["role": "user", "content": text],
                ],
            ]
        case .anthropic:
            req.setValue(key, forHTTPHeaderField: "x-api-key")
            req.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version")
            var anthropicBody: [String: Any] = [
                "model": model,
                "max_tokens": 8192,
                "temperature": 0.2,
                "system": systemPrompt,
                "messages": [
                    ["role": "user", "content": text],
                ],
            ]
            // GLM-5 enables thinking by default → disable for fast text correction
            if model.lowercased().contains("glm") {
                anthropicBody["thinking"] = ["type": "disabled"]
            }
            body = anthropicBody
        }

        guard let httpBody = try? JSONSerialization.data(withJSONObject: body) else {
            completion(nil); return
        }
        req.httpBody = httpBody

        DispatchQueue.main.async { self.isCorrecting = true }

        let style = p.style
        URLSession.shared.dataTask(with: req) { [weak self] data, _, error in
            DispatchQueue.main.async { self?.isCorrecting = false }

            if let error = error {
                print("❌ Correction error: \(error.localizedDescription)")
                completion(nil); return
            }
            guard let data = data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                print("❌ Correction: could not parse response")
                completion(nil); return
            }

            let content = Self.extractText(from: json, style: style)
            guard let raw = content else {
                print("❌ Correction response: \(json)")
                completion(nil); return
            }

            let cleaned = raw
                .trimmingCharacters(in: .whitespacesAndNewlines)
                .trimmingCharacters(in: CharacterSet(charactersIn: "\"'"))
            completion(cleaned.isEmpty ? nil : cleaned)
        }.resume()
    }

    /// Renders user-defined "wrong → right" pairs as a prompt block. Must stay byte-identical to
    /// PromptBuilder.RenderReplacements on the Windows side (WhisperWin/Core/PromptBuilder.cs).
    /// Empty pairs → empty string, so the {{REPLACEMENTS}} slot leaves no dangling header.
    static func renderReplacements(_ pairs: [Pair]) -> String {
        guard !pairs.isEmpty else { return "" }
        let header = "Word replacements — apply these exact substitutions when the left-hand phrase appears (use context; do not replace inside unrelated words):"
        let lines = pairs.map { "- \"\($0.toReplace)\" → \"\($0.replaceWith)\"" }
        return ([header] + lines).joined(separator: "\n")
    }

    /// Renders learned examples (Learn-from-Edits) as a prompt block — corrections the user
    /// previously taught by editing a History entry. Must stay byte-identical to a future
    /// PromptBuilder.RenderLearnedExamples on the Windows side (WhisperWin/Core/PromptBuilder.cs).
    /// Empty examples → empty string, so the slot leaves no dangling header.
    static func renderLearnedExamples(_ examples: [Example]) -> String {
        guard !examples.isEmpty else { return "" }
        let header = "Learned corrections — the user previously fixed outputs like these; prefer the corrected form (use judgement, do not force it onto unrelated text):"
        let lines = examples.map { "- \"\($0.raw)\" → \"\($0.corrected)\"" }
        return ([header] + lines).joined(separator: "\n")
    }

    /// Extract text from the response based on the provider's API style
    private static func extractText(from json: [String: Any], style: LLMProvider.Style) -> String? {
        switch style {
        case .openAI:
            guard let choices = json["choices"] as? [[String: Any]],
                  let message = choices.first?["message"] as? [String: Any],
                  let content = message["content"] as? String else { return nil }
            return content
        case .anthropic:
            guard let content = json["content"] as? [[String: Any]] else { return nil }
            return content.compactMap { $0["text"] as? String }.joined()
        }
    }
}
