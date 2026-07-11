using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WhisperWin.Core
{
    /// <summary>
    /// Read-modify-write store for shared/dictionary.json — mirrors Sources/DictionaryStore.swift
    /// so both apps apply the same safety rules when the Settings UI edits the file (previously
    /// this file was read-only on Windows; Phase 2 adds the editor that writes it back).
    /// </summary>
    public static class DictionaryStore
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            // shared/dictionary.json is meant to be human-readable, including Thai text — the
            // default encoder escapes non-ASCII as \uXXXX, which the Mac side (withoutEscapingSlashes,
            // prettyPrinted) does not do. UnsafeRelaxedJsonEscaping keeps output identical in spirit.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// Outcome of attempting to read the on-disk file — distinguishes "nothing there yet"
        /// (first run, safe to start from a blank file) from "something is there but we couldn't
        /// read it" (OneDrive sync lock, a crashed write, corruption). The "unreadable" case must
        /// NOT be treated as empty, or a read-modify-write save would silently overwrite the
        /// user's real data.
        /// </summary>
        private enum ReadOutcome { Ok, Missing, Unreadable }

        private static ReadOutcome TryRead(string path, out DictionaryFile file)
        {
            file = new DictionaryFile();
            if (!File.Exists(path))
            {
                return ReadOutcome.Missing;
            }

            try
            {
                var json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<DictionaryFile>(json);
                if (parsed == null)
                {
                    return ReadOutcome.Unreadable;
                }
                file = parsed;
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
        /// Reads the file for display in the Settings UI. Falls back to an empty DictionaryFile on
        /// any failure — including "unreadable" — so a locked/corrupt file never crashes the
        /// window; it just temporarily shows nothing until the file is readable again. This is a
        /// read-only path, so falling back here can't destroy anything on disk.
        /// </summary>
        public static DictionaryFile Load(string path)
        {
            return TryRead(path, out var file) == ReadOutcome.Ok ? file : new DictionaryFile();
        }

        private static void Write(string path, DictionaryFile file)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(file, WriteOptions);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Read-modify-write: replaces <c>entries</c> while preserving whatever <c>pairs</c> is on
        /// disk right now, so saving the word list never clobbers the replacement pairs (and vice
        /// versa in <see cref="SavePairs"/>). This also shrinks the race window against the Mac app
        /// writing the same file through OneDrive — we always start from the latest bytes on disk.
        ///
        /// Safety: if the file exists but can't be read/parsed right now (sync lock, partial write,
        /// corruption), this is a no-op — writing back would silently delete the user's real
        /// entries/pairs. Only "file doesn't exist yet" is treated as a blank slate.
        /// </summary>
        public static void SaveEntries(string path, List<string> entries)
        {
            switch (TryRead(path, out var file))
            {
                case ReadOutcome.Unreadable:
                    return;
                case ReadOutcome.Missing:
                    Write(path, new DictionaryFile { Entries = entries, Pairs = new List<DictionaryPair>() });
                    return;
                case ReadOutcome.Ok:
                    file.Entries = entries;
                    Write(path, file);
                    return;
            }
        }

        public static void SavePairs(string path, List<DictionaryPair> pairs)
        {
            switch (TryRead(path, out var file))
            {
                case ReadOutcome.Unreadable:
                    return;
                case ReadOutcome.Missing:
                    Write(path, new DictionaryFile { Entries = new List<string>(), Pairs = pairs });
                    return;
                case ReadOutcome.Ok:
                    file.Pairs = pairs;
                    Write(path, file);
                    return;
            }
        }
    }
}
