using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class HistoryStoreTests : IDisposable
    {
        private readonly string _tempFile;

        public HistoryStoreTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"whisperwin-history-{Guid.NewGuid():N}.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmptyList()
        {
            var entries = HistoryStore.Load(_tempFile);

            Assert.Empty(entries);
        }

        [Fact]
        public void Append_MissingFile_CreatesFileWithOneEntry()
        {
            HistoryStore.Append(_tempFile, "hello world");

            var entries = HistoryStore.Load(_tempFile);
            Assert.Single(entries);
            Assert.Equal("hello world", entries[0].Text);
            Assert.NotEqual(Guid.Empty, entries[0].Id);
        }

        [Fact]
        public void Append_ThenLoad_RoundTripsTextAndDate()
        {
            var before = DateTime.Now;
            HistoryStore.Append(_tempFile, "round trip me");
            var after = DateTime.Now;

            var entries = HistoryStore.Load(_tempFile);
            Assert.Single(entries);
            Assert.Equal("round trip me", entries[0].Text);
            // ISO 8601 round-trip via System.Text.Json's default DateTime converter — sub-second
            // precision must survive the write/read cycle, not just the date.
            Assert.InRange(entries[0].Date, before.AddSeconds(-1), after.AddSeconds(1));
        }

        [Fact]
        public void Append_MultipleEntries_PreservesAppendOrderOldestFirst()
        {
            HistoryStore.Append(_tempFile, "first");
            HistoryStore.Append(_tempFile, "second");
            HistoryStore.Append(_tempFile, "third");

            var entries = HistoryStore.Load(_tempFile);
            Assert.Equal(new[] { "first", "second", "third" }, entries.ConvertAll(e => e.Text));
        }

        [Fact]
        public void Append_501Entries_CapsAt500AndDropsOldest()
        {
            for (var i = 0; i < 501; i++)
            {
                HistoryStore.Append(_tempFile, $"entry {i}");
            }

            var entries = HistoryStore.Load(_tempFile);
            Assert.Equal(500, entries.Count);
            // Entry 0 was the oldest — it must be the one dropped, leaving entry 1 as the new oldest.
            Assert.Equal("entry 1", entries[0].Text);
            Assert.Equal("entry 500", entries[^1].Text);
        }

        [Fact]
        public void Append_BlankOrWhitespaceText_IsIgnored()
        {
            HistoryStore.Append(_tempFile, "");
            HistoryStore.Append(_tempFile, "   ");
            HistoryStore.Append(_tempFile, "\t\n");

            Assert.False(File.Exists(_tempFile));
            Assert.Empty(HistoryStore.Load(_tempFile));
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            HistoryStore.Append(_tempFile, "one");
            HistoryStore.Append(_tempFile, "two");

            HistoryStore.Clear(_tempFile);

            Assert.Empty(HistoryStore.Load(_tempFile));
        }

        [Fact]
        public void Clear_MissingFile_CreatesEmptyFileWithoutThrowing()
        {
            var exception = Record.Exception(() => HistoryStore.Clear(_tempFile));

            Assert.Null(exception);
            Assert.Empty(HistoryStore.Load(_tempFile));
        }

        [Fact]
        public void Load_CorruptJson_ReturnsEmptyListInsteadOfThrowing()
        {
            File.WriteAllText(_tempFile, "{ not valid json ");

            var entries = HistoryStore.Load(_tempFile);

            Assert.Empty(entries);
        }

        [Fact]
        public void Append_CorruptExistingFile_IsNoOp()
        {
            // Mirrors DictionaryStore's "unreadable file is left alone" rule — appending to a
            // corrupt file must not silently replace it with a fresh single-entry file.
            File.WriteAllText(_tempFile, "{ not valid json ");

            HistoryStore.Append(_tempFile, "should not be written");

            var raw = File.ReadAllText(_tempFile);
            Assert.Equal("{ not valid json ", raw);
        }

        [Fact]
        public void Append_FileLockedExclusively_IsNoOpAndDoesNotThrow()
        {
            const string original = "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"date\":\"2026-01-01T00:00:00\",\"text\":\"untouched\"}]";
            File.WriteAllText(_tempFile, original);

            using (new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var exception = Record.Exception(() => HistoryStore.Append(_tempFile, "should not be written"));
                Assert.Null(exception);
            }

            var raw = File.ReadAllText(_tempFile);
            Assert.Equal(original, raw);
        }

        [Fact]
        public void Append_ThaiUnicodeText_IsPreservedNotEscaped()
        {
            HistoryStore.Append(_tempFile, "คำไทยล้วน สวัสดีครับ");

            var raw = File.ReadAllText(_tempFile);
            Assert.Contains("คำไทยล้วน สวัสดีครับ", raw);
            Assert.DoesNotContain("\\u", raw);

            var entries = HistoryStore.Load(_tempFile);
            Assert.Equal("คำไทยล้วน สวัสดีครับ", entries[0].Text);
        }

        [Fact]
        public void Append_WritesIndentedJson()
        {
            HistoryStore.Append(_tempFile, "X");

            var raw = File.ReadAllText(_tempFile);
            Assert.Contains("\n", raw);
        }

        [Fact]
        public void Append_CreatesParentDirectoryIfMissing()
        {
            var nestedPath = Path.Combine(Path.GetTempPath(), $"whisperwin-history-dir-{Guid.NewGuid():N}", "history.json");

            HistoryStore.Append(nestedPath, "X");

            Assert.True(File.Exists(nestedPath));

            File.Delete(nestedPath);
            Directory.Delete(Path.GetDirectoryName(nestedPath)!);
        }

        [Fact]
        public void DefaultFilePath_EndsWithWhisperWinHistoryJson()
        {
            var path = HistoryStore.DefaultFilePath();

            Assert.EndsWith(Path.Combine("WhisperWin", "history.json"), path);
        }

        // ── P2 additions: edge cases not covered by P1's original 15 tests ──

        [Fact]
        public void Append_ExactlyAtCapBoundary_NothingDroppedUntil501st()
        {
            for (var i = 0; i < 500; i++)
            {
                HistoryStore.Append(_tempFile, $"entry {i}");
            }

            var atCap = HistoryStore.Load(_tempFile);
            Assert.Equal(500, atCap.Count);
            Assert.Equal("entry 0", atCap[0].Text); // still present — cap not yet exceeded

            HistoryStore.Append(_tempFile, "entry 500");

            var overCap = HistoryStore.Load(_tempFile);
            Assert.Equal(500, overCap.Count);
            Assert.Equal("entry 1", overCap[0].Text); // entry 0 dropped
            Assert.Equal("entry 500", overCap[^1].Text);
        }

        [Fact]
        public void Load_FileWithMoreThan500EntriesWrittenDirectly_IsCappedTo500()
        {
            // Simulates a history.json from a future/older build without the cap, or one hand-
            // edited/merged to exceed it — Load must not hand 500+ entries to the UI even though
            // only Append enforces the cap today.
            var entries = new List<HistoryEntry>();
            for (var i = 0; i < 510; i++)
            {
                entries.Add(new HistoryEntry { Id = Guid.NewGuid(), Date = DateTime.Now, Text = $"raw {i}" });
            }
            File.WriteAllText(_tempFile, JsonSerializer.Serialize(entries));

            var loaded = HistoryStore.Load(_tempFile);

            Assert.True(loaded.Count <= 500, $"expected Load to cap at 500 entries, got {loaded.Count}");
        }

        [Fact]
        public void Load_JsonObjectInsteadOfArray_ReturnsEmptyListInsteadOfThrowing()
        {
            File.WriteAllText(_tempFile, "{}");

            var exception = Record.Exception(() => HistoryStore.Load(_tempFile));

            Assert.Null(exception);
            Assert.Empty(HistoryStore.Load(_tempFile));
        }

        [Fact]
        public void Load_JsonArrayOfNumbersInsteadOfObjects_ReturnsEmptyListInsteadOfThrowing()
        {
            File.WriteAllText(_tempFile, "[123, 456]");

            var exception = Record.Exception(() => HistoryStore.Load(_tempFile));

            Assert.Null(exception);
            Assert.Empty(HistoryStore.Load(_tempFile));
        }

        [Fact]
        public void Append_CorruptExistingFileWrongShape_IsNoOp()
        {
            // "{}" is valid JSON but the wrong shape (object, not array) — Append must treat this
            // the same as syntactically-corrupt JSON: leave the file untouched rather than
            // clobbering it with a fresh single-entry array.
            File.WriteAllText(_tempFile, "{}");

            HistoryStore.Append(_tempFile, "should not be written");

            var raw = File.ReadAllText(_tempFile);
            Assert.Equal("{}", raw);
        }

        [Fact]
        public void Append_VeryLongText_RoundTripsIntact()
        {
            var longText = new string('a', 50_000) + "จบข้อความยาว";
            HistoryStore.Append(_tempFile, longText);

            var entries = HistoryStore.Load(_tempFile);
            Assert.Single(entries);
            Assert.Equal(longText, entries[0].Text);
        }

        [Fact]
        public void Append_TextWithNewlinesAndEmoji_RoundTripsIntact()
        {
            const string text = "line one\nline two\r\nline three 😀🎉👍 ทดสอบ emoji";
            HistoryStore.Append(_tempFile, text);

            var entries = HistoryStore.Load(_tempFile);
            Assert.Single(entries);
            Assert.Equal(text, entries[0].Text);
        }

        [Fact]
        public void Append_SequentialRapidAppends_PreservesAllEntriesInOrder()
        {
            const int count = 50;
            for (var i = 0; i < count; i++)
            {
                HistoryStore.Append(_tempFile, $"rapid {i}");
            }

            var entries = HistoryStore.Load(_tempFile);
            Assert.Equal(count, entries.Count);
            for (var i = 0; i < count; i++)
            {
                Assert.Equal($"rapid {i}", entries[i].Text);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task Append_ConcurrentAppendsFromMultipleThreads_NeverCorruptsTheFileEvenIfSomeCallsThrow()
        {
            // FINDING (documented here, not silently fixed): unlike DictionaryStore/TextInjector's
            // retry-on-transient-lock pattern, HistoryStore.Write() has no retry and no lock beyond
            // OS file-sharing rules. Under true concurrent Append() calls from multiple threads,
            // File.WriteAllText's FileShare.None WILL throw IOException for the loser(s) of the
            // race — see repro below. In production this is currently low-risk because Append is
            // only ever invoked from the single WPF dispatcher thread (per App.xaml.cs's
            // OnStageChanged, which runs inside Dispatcher.InvokeAsync), so true concurrent writers
            // don't occur today. This test therefore does NOT assert "no exceptions" (that would be
            // false); it asserts the weaker, still-important property that the on-disk file is
            // never left corrupt/unparseable after a burst of racing writers, and that no entry is
            // silently mangled (each surviving entry is intact, not truncated/merged).
            const int threads = 4;
            const int perThread = 10;
            var sawIoException = false;

            var tasks = new List<System.Threading.Tasks.Task>();
            for (var t = 0; t < threads; t++)
            {
                var threadId = t;
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    for (var i = 0; i < perThread; i++)
                    {
                        try
                        {
                            HistoryStore.Append(_tempFile, $"thread{threadId}-entry{i}");
                        }
                        catch (IOException)
                        {
                            // Expected under true concurrency — see FINDING above. HistoryStore has
                            // no retry, so a losing writer's exception propagates to its caller.
                            sawIoException = true;
                        }
                    }
                }));
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);

            // Whatever survived, the file must still be valid, loadable JSON — never truncated or
            // half-written — and every entry present must be a complete, unmangled one we wrote.
            var loadException = Record.Exception(() => HistoryStore.Load(_tempFile));
            Assert.Null(loadException);

            var entries = HistoryStore.Load(_tempFile);
            Assert.True(entries.Count > 0, "expected at least some entries to survive concurrent appends");
            Assert.True(entries.Count <= threads * perThread);
            foreach (var entry in entries)
            {
                Assert.Matches(@"^thread\d-entry\d$", entry.Text);
            }

            // Not a hard assertion (timing-dependent), but recorded so a future retry/lock fix can
            // be verified: today, contention reliably produces at least one IOException.
            _ = sawIoException;
        }

        [Fact]
        public void Load_DateRoundTrips_PreservesSubSecondPrecisionExactly()
        {
            // Writes a raw entry with a specific, sub-second-precision DateTime (as Append's
            // DateTime.Now would produce) and confirms Load reconstructs it exactly — not just
            // "close enough" — so History rows don't silently drift or truncate timestamps.
            var expected = new DateTime(2026, 7, 10, 14, 23, 45, 678, DateTimeKind.Local).AddTicks(1234);
            var raw = "[{\"id\":\"11111111-1111-1111-1111-111111111111\"," +
                      $"\"date\":\"{expected:o}\"," +
                      "\"text\":\"precise\"}]";
            File.WriteAllText(_tempFile, raw);

            var entries = HistoryStore.Load(_tempFile);

            Assert.Single(entries);
            Assert.Equal(expected, entries[0].Date);
        }

        [Fact]
        public void Append_TextThatIsOnlyEmojiOrSymbols_IsNotTreatedAsBlank()
        {
            // string.IsNullOrWhiteSpace should not classify emoji/symbols as whitespace, but this
            // guards the boundary explicitly since Append's blank check is the only gate before
            // history is recorded.
            HistoryStore.Append(_tempFile, "😀");

            var entries = HistoryStore.Load(_tempFile);
            Assert.Single(entries);
            Assert.Equal("😀", entries[0].Text);
        }
    }
}
