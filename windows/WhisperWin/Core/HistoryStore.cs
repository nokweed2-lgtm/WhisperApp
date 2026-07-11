using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WhisperWin.Core
{
    /// <summary>
    /// Read-modify-write store for the local dictation history — mirrors
    /// Sources/HistoryView.swift's HistoryStore, but at %APPDATA%\WhisperWin\history.json instead
    /// of ~/.whisperapp/history.json. Unlike DictionaryStore this file is per-machine and never
    /// touched by the Mac app, so there is no cross-platform write race to guard against — the
    /// same "unreadable file is left alone" rule below just protects against a locked/corrupt file
    /// silently losing history (e.g. OneDrive momentarily locking %APPDATA% during a backup).
    /// </summary>
    public static class HistoryStore
    {
        private const int MaxEntries = 500;

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            // History can contain Thai text — keep it human-readable instead of \uXXXX-escaped,
            // same choice as DictionaryStore for shared/dictionary.json.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Default production path: %APPDATA%\WhisperWin\history.json.</summary>
        public static string DefaultFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "WhisperWin", "history.json");
        }

        /// <summary>
        /// Outcome of attempting to read the on-disk file — distinguishes "nothing there yet"
        /// (first run, safe to start from an empty list) from "something is there but we couldn't
        /// read it" (locked file, a crashed write, corruption). The "unreadable" case must NOT be
        /// treated as empty in Append/Clear, or a read-modify-write save would silently wipe out
        /// the user's real history.
        /// </summary>
        private enum ReadOutcome { Ok, Missing, Unreadable }

        private static ReadOutcome TryRead(string path, out List<HistoryEntry> entries)
        {
            entries = new List<HistoryEntry>();
            if (!File.Exists(path))
            {
                return ReadOutcome.Missing;
            }

            try
            {
                var json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
                if (parsed == null)
                {
                    return ReadOutcome.Unreadable;
                }
                entries = parsed;
                return ReadOutcome.Ok;
            }
            catch (JsonException)
            {
                return ReadOutcome.Unreadable;
            }
            catch (IOException)
            {
                return ReadOutcome.Unreadable;
            }
        }

        /// <summary>
        /// Reads all entries, oldest first (append order) — mirrors HistoryView.swift's `load()`.
        /// Falls back to an empty list on any failure, including "unreadable", so a locked/corrupt
        /// file never crashes the History window; it just temporarily shows nothing until the file
        /// is readable again. This is a read-only path, so falling back here can't destroy anything
        /// on disk.
        ///
        /// Also re-enforces the <see cref="MaxEntries"/> cap (keeping the newest) regardless of how
        /// the file got here — a hand-edited or otherwise externally-written history.json with more
        /// than <see cref="MaxEntries"/> entries must not make the History window render an
        /// unbounded list. This makes the cap an invariant of the file's contents, not just a
        /// side effect of going through <see cref="Append"/>.
        /// </summary>
        public static List<HistoryEntry> Load(string path)
        {
            if (TryRead(path, out var entries) != ReadOutcome.Ok)
            {
                return new List<HistoryEntry>();
            }

            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(0, entries.Count - MaxEntries);
            }

            return entries;
        }

        private static void Write(string path, List<HistoryEntry> entries)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(entries, WriteOptions);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Appends one entry, capping the file at <see cref="MaxEntries"/> by dropping the oldest.
        /// Skips silently (no-op) if the existing file is present but unreadable — same rule as
        /// DictionaryStore.SaveEntries — so a locked/corrupt history.json never gets clobbered by
        /// a fresh single-entry file. Blank/whitespace-only text is ignored, since it never reaches
        /// TextInjector either (see DictationController's empty-transcript check).
        /// </summary>
        public static void Append(string path, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            List<HistoryEntry> entries;
            switch (TryRead(path, out var existing))
            {
                case ReadOutcome.Unreadable:
                    return;
                case ReadOutcome.Missing:
                    entries = new List<HistoryEntry>();
                    break;
                default:
                    entries = existing;
                    break;
            }

            entries.Add(new HistoryEntry { Id = Guid.NewGuid(), Date = DateTime.Now, Text = text });
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(0, entries.Count - MaxEntries);
            }

            Write(path, entries);
        }

        /// <summary>Wipes history — mirrors HistoryView.swift's `clear()` (unconditional overwrite).</summary>
        public static void Clear(string path) => Write(path, new List<HistoryEntry>());
    }
}
