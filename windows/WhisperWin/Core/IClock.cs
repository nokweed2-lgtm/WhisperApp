using System;

namespace WhisperWin.Core
{
    /// <summary>
    /// Abstraction over wall-clock time so the hotkey state machine can be unit tested
    /// with a fake clock instead of real Win32/system timers.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>Production clock backed by the system time.</summary>
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
