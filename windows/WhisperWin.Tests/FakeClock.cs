using System;
using WhisperWin.Core;

namespace WhisperWin.Tests
{
    /// <summary>Controllable clock for deterministic hotkey state machine tests.</summary>
    public sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; private set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
        }
    }
}
