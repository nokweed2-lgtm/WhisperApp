using System;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class HotkeyStateMachineTests
    {
        private static HotkeyStateMachine CreateSut(FakeClock clock) =>
            new(clock, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(400));

        [Fact]
        public void HoldPastThreshold_FiresStarted()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var startedFired = false;
            sut.Started += (s, e) => startedFired = true;

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(160));
            sut.CheckHoldPromotion();

            Assert.True(startedFired);
            Assert.True(sut.IsActive);
        }

        [Fact]
        public void HoldThenRelease_FiresStartedThenStopped()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var started = false;
            var stopped = false;
            sut.Started += (s, e) => started = true;
            sut.Stopped += (s, e) => stopped = true;

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            sut.CheckHoldPromotion();
            Assert.True(started);

            sut.OnKeyUp();

            Assert.True(stopped);
            Assert.False(sut.IsActive);
        }

        [Fact]
        public void ShortTap_BelowThreshold_DoesNotFireStarted()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var started = false;
            sut.Started += (s, e) => started = true;

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            sut.CheckHoldPromotion(); // below threshold — should not fire
            sut.OnKeyUp();

            Assert.False(started);
            Assert.False(sut.IsActive);
        }

        [Fact]
        public void DoubleTapWithinWindow_TogglesOn()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            bool? toggledValue = null;
            sut.Toggled += (s, isOn) => toggledValue = isOn;

            // First short tap.
            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            sut.OnKeyUp();

            // Second tap within the double-tap window (400ms).
            clock.Advance(TimeSpan.FromMilliseconds(100));
            sut.OnKeyDown();

            Assert.True(toggledValue);
            Assert.True(sut.IsActive);
        }

        [Fact]
        public void DoubleTapOutsideWindow_DoesNotToggle()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var toggled = false;
            sut.Toggled += (s, isOn) => toggled = true;

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            sut.OnKeyUp();

            // Second tap AFTER the double-tap window (400ms) has elapsed.
            clock.Advance(TimeSpan.FromMilliseconds(500));
            sut.OnKeyDown();

            Assert.False(toggled);
            Assert.False(sut.IsActive);
        }

        [Fact]
        public void ToggleOn_ThenSingleTap_TogglesOff()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var toggleEvents = new System.Collections.Generic.List<bool>();
            sut.Toggled += (s, isOn) => toggleEvents.Add(isOn);

            // Double-tap to turn on.
            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            sut.OnKeyUp();
            clock.Advance(TimeSpan.FromMilliseconds(100));
            sut.OnKeyDown();
            Assert.True(sut.IsActive);

            sut.OnKeyUp(); // release the second tap's key

            // A single subsequent tap should stop it (no need for another double-tap).
            clock.Advance(TimeSpan.FromMilliseconds(500));
            sut.OnKeyDown();

            Assert.Equal(new[] { true, false }, toggleEvents);
            Assert.False(sut.IsActive);
        }

        [Fact]
        public void HoldMode_KeyRepeatDownEvents_DoNotDoubleFireStarted()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var startedCount = 0;
            sut.Started += (s, e) => startedCount++;

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(160));
            sut.CheckHoldPromotion();

            // Simulate OS auto-repeat key-down events while still held.
            sut.OnKeyDown();
            sut.OnKeyDown();
            sut.CheckHoldPromotion();

            Assert.Equal(1, startedCount);
        }

        [Fact]
        public void Reset_ReturnsToIdle()
        {
            var clock = new FakeClock();
            var sut = CreateSut(clock);

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            sut.CheckHoldPromotion();
            Assert.True(sut.IsActive);

            sut.Reset();

            Assert.False(sut.IsActive);
        }

        [Fact]
        public void KeyUp_WithNoPriorKeyDown_IsNoOpAndStaysIdle()
        {
            // Simulates the app starting (or the hook re-attaching) while the hotkey is already
            // physically held down: the very first event the state machine ever sees is a key-up.
            // Now that HotkeyManager always forwards key-up to OnKeyUp (never swallows it), this
            // must remain a safe no-op instead of firing Stopped or corrupting state.
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var stopped = false;
            var toggled = false;
            sut.Stopped += (s, e) => stopped = true;
            sut.Toggled += (s, isOn) => toggled = true;

            sut.OnKeyUp();

            Assert.False(stopped);
            Assert.False(toggled);
            Assert.False(sut.IsActive);

            // State must still be sane afterwards — a normal hold should work right after.
            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            sut.CheckHoldPromotion();
            Assert.True(sut.IsActive);
        }

        [Fact]
        public void KeyUp_WhileIdleAfterAlreadyReleased_IsIgnored()
        {
            // A stray extra key-up (e.g. duplicate WM_KEYUP/WM_SYSKEYUP for the same physical
            // release) arriving while already idle must not toggle or start anything.
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var toggled = false;
            sut.Toggled += (s, isOn) => toggled = true;

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            sut.OnKeyUp();

            // Extra key-up with no matching key-down in between.
            sut.OnKeyUp();

            Assert.False(toggled);
            Assert.False(sut.IsActive);
        }

        [Fact]
        public void RapidDoubleTapDownUpDownUp_TogglesOnThenStaysOnUntilExplicitTap()
        {
            // Fast down/up/down/up with barely any gap — regression for the fix that makes
            // key-up ALWAYS reach OnKeyUp (previously it could be swallowed while active, which
            // risked this sequence leaving the state machine's internal bookkeeping inconsistent).
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var toggleEvents = new System.Collections.Generic.List<bool>();
            sut.Toggled += (s, isOn) => toggleEvents.Add(isOn);

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(20));
            sut.OnKeyUp();
            clock.Advance(TimeSpan.FromMilliseconds(20));
            sut.OnKeyDown(); // second tap of the double-tap — toggles on
            clock.Advance(TimeSpan.FromMilliseconds(20));
            sut.OnKeyUp(); // release of the toggling key-down — must NOT toggle off

            Assert.Equal(new[] { true }, toggleEvents);
            Assert.True(sut.IsActive);
        }

        [Fact]
        public void HoldThenRelease_ThenImmediateRePress_StartsFreshHoldCycle()
        {
            // "Rapid re-dictation": user holds, releases (Stopped fires), and immediately holds
            // again. The key-up from the first hold must not be mistaken for a double-tap
            // candidate that toggles the second hold on early — it should behave as a plain,
            // independent hold cycle.
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var startedCount = 0;
            var stoppedCount = 0;
            var toggled = false;
            sut.Started += (s, e) => startedCount++;
            sut.Stopped += (s, e) => stoppedCount++;
            sut.Toggled += (s, isOn) => toggled = true;

            // First hold cycle.
            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            sut.CheckHoldPromotion();
            sut.OnKeyUp();

            Assert.Equal(1, startedCount);
            Assert.Equal(1, stoppedCount);
            Assert.False(sut.IsActive);

            // Immediate re-press, well within the double-tap window, but held long enough to
            // become its own hold rather than a toggle.
            clock.Advance(TimeSpan.FromMilliseconds(10));
            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            sut.CheckHoldPromotion();

            Assert.Equal(2, startedCount);
            Assert.False(toggled);
            Assert.True(sut.IsActive);

            sut.OnKeyUp();
            Assert.Equal(2, stoppedCount);
            Assert.False(sut.IsActive);
        }

        [Fact]
        public void ToggleOn_KeyUpOfSecondTap_DoesNotStopRecording()
        {
            // Now that key-up is always forwarded to the state machine (never swallowed while
            // active), explicitly verify that releasing the key that produced the toggle-on
            // transition does not itself stop recording — only an explicit subsequent tap does.
            var clock = new FakeClock();
            var sut = CreateSut(clock);
            var stopped = false;
            sut.Stopped += (s, e) => stopped = true;

            sut.OnKeyDown();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            sut.OnKeyUp();
            clock.Advance(TimeSpan.FromMilliseconds(100));
            sut.OnKeyDown(); // toggles on
            Assert.True(sut.IsActive);

            sut.OnKeyUp(); // release of the toggling key — must be a no-op for Stopped

            Assert.False(stopped);
            Assert.True(sut.IsActive);
        }
    }
}
