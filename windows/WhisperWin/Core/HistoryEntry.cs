using System;
using System.Text.Json.Serialization;

namespace WhisperWin.Core
{
    /// <summary>
    /// One completed dictation — mirrors Sources/HistoryView.swift's HistoryEntry (id/date/text).
    /// Not shared with the Mac app (each machine keeps its own history.json), so the JSON shape
    /// only needs to round-trip with itself; System.Text.Json's default DateTime converter already
    /// serializes to an ISO 8601 round-trip string.
    /// </summary>
    public sealed class HistoryEntry
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("date")]
        public DateTime Date { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }
}
