using System;

namespace WhisperWin.Core
{
    /// <summary>
    /// Pure, Win32-free state machine implementing the Phase 1 hotkey semantics for the
    /// configured hotkey (default: Right Ctrl / VK_RCONTROL):
    ///
    ///   - Hold ≥ HoldThreshold  → Started fires on threshold reached; Stopped fires on key-up.
    ///   - Double-tap (2 presses within DoubleTapWindow) while idle → Toggled fires, recording
    ///     stays on until a single subsequent tap stops it (Toggled fires again).
    ///   - A short tap (< HoldThreshold) that is NOT part of a double-tap does nothing.
    ///
    /// Feed it raw key-down/key-up events (already filtered to the configured virtual key) via
    /// <see cref="OnKeyDown"/> / <see cref="OnKeyUp"/>. It never touches the clock directly —
    /// callers pass timestamps (or supply an <see cref="IClock"/>) so tests can control time.
    /// </summary>
    public sealed class HotkeyStateMachine
    {
        public static readonly TimeSpan DefaultHoldThreshold = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan DefaultDoubleTapWindow = TimeSpan.FromMilliseconds(400);

        private readonly IClock _clock;
        private readonly TimeSpan _holdThreshold;
        private readonly TimeSpan _doubleTapWindow;

        // OnKeyDown/OnKeyUp run on the hook's message-loop thread (the UI thread) while
        // CheckHoldPromotion runs on a ThreadPool timer thread — one gate keeps their
        // interleavings from tearing shared state. Events MUST fire outside the gate: handlers
        // reach the Dispatcher, and blocking on the UI thread while holding the gate deadlocks
        // against the hook callback waiting for the same gate (observed in the field).
        private readonly object _gate = new();

        private enum PendingEvent
        {
            None,
            Started,
            Stopped,
            ToggledOn,
            ToggledOff,
        }

        private bool _keyPhysicallyDown;
        private DateTime _keyDownAt;
        private bool _holdStarted;      // Started already fired for the current physical press
        private DateTime? _lastTapUpAt; // time of the last key-up that did not become a hold

        private enum Mode
        {
            Idle,
            Holding,        // Started fired via hold; waiting for key-up to Stop
            ToggledOn,      // Started fired via double-tap toggle; waiting for a single tap to stop
        }

        private Mode _mode = Mode.Idle;

        public HotkeyStateMachine(IClock clock, TimeSpan? holdThreshold = null, TimeSpan? doubleTapWindow = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _holdThreshold = holdThreshold ?? DefaultHoldThreshold;
            _doubleTapWindow = doubleTapWindow ?? DefaultDoubleTapWindow;
        }

        /// <summary>Recording (or the equivalent "active") state started — begin capturing audio.</summary>
        public event EventHandler? Started;

        /// <summary>Recording stopped because the hold key was released.</summary>
        public event EventHandler? Stopped;

        /// <summary>Recording state flipped via double-tap toggle (fires for both on and off transitions).</summary>
        public event EventHandler<bool>? Toggled;

        public bool IsActive => _mode != Mode.Idle;

        /// <summary>
        /// Call on every raw key-down for the configured hotkey virtual key. Safe to call for
        /// key-repeat events too (auto-repeat down events while held) — they are ignored.
        /// </summary>
        public void OnKeyDown()
        {
            PendingEvent pending;
            lock (_gate)
            {
                pending = OnKeyDownLocked();
            }
            Fire(pending);
        }

        private PendingEvent OnKeyDownLocked()
        {
            if (_keyPhysicallyDown)
            {
                // Auto-repeat key-down while already held; ignore.
                return CheckHoldPromotionLocked();
            }

            _keyPhysicallyDown = true;
            _keyDownAt = _clock.UtcNow;
            _holdStarted = false;

            if (_mode == Mode.ToggledOn)
            {
                // Toggle mode, currently recording: a single tap stops it immediately on key-down,
                // mirroring the Mac implementation (isActive → stop on tap, no double-tap needed).
                _mode = Mode.Idle;
                _lastTapUpAt = null;
                return PendingEvent.ToggledOff;
            }

            if (_lastTapUpAt.HasValue && (_keyDownAt - _lastTapUpAt.Value) < _doubleTapWindow)
            {
                // Second tap of a double-tap within the window → toggle on.
                _lastTapUpAt = null;
                _mode = Mode.ToggledOn;
                return PendingEvent.ToggledOn;
            }

            // First tap (or a tap outside the double-tap window) — wait and see if it becomes a hold.
            return PendingEvent.None;
        }

        /// <summary>
        /// Call periodically (e.g. from a timer) while the key is physically down so a hold can be
        /// detected without waiting for key-up. Production code drives this from a short interval
        /// timer; tests can call it directly after advancing the fake clock.
        /// </summary>
        public void CheckHoldPromotion()
        {
            PendingEvent pending;
            lock (_gate)
            {
                pending = CheckHoldPromotionLocked();
            }
            Fire(pending);
        }

        private PendingEvent CheckHoldPromotionLocked()
        {
            if (!_keyPhysicallyDown || _holdStarted || _mode != Mode.Idle)
            {
                return PendingEvent.None;
            }

            if ((_clock.UtcNow - _keyDownAt) >= _holdThreshold)
            {
                _holdStarted = true;
                _mode = Mode.Holding;
                return PendingEvent.Started;
            }

            return PendingEvent.None;
        }

        /// <summary>Call on every raw key-up for the configured hotkey virtual key.</summary>
        public void OnKeyUp()
        {
            PendingEvent pending;
            lock (_gate)
            {
                pending = OnKeyUpLocked();
            }
            Fire(pending);
        }

        private PendingEvent OnKeyUpLocked()
        {
            if (!_keyPhysicallyDown)
            {
                return PendingEvent.None;
            }

            _keyPhysicallyDown = false;
            var now = _clock.UtcNow;

            if (_mode == Mode.Holding)
            {
                _mode = Mode.Idle;
                _lastTapUpAt = null;
                return PendingEvent.Stopped;
            }

            if (_mode == Mode.ToggledOn)
            {
                // Key released while in toggle-on mode without having been re-tapped on key-down
                // (OnKeyDown already handles the "tap to stop" transition). Nothing to do here.
                return PendingEvent.None;
            }

            // Short tap that never became a hold — record it as a candidate for double-tap.
            if (!_holdStarted && (now - _keyDownAt) < _holdThreshold)
            {
                _lastTapUpAt = now;
            }
            else
            {
                _lastTapUpAt = null;
            }

            return PendingEvent.None;
        }

        private void Fire(PendingEvent pending)
        {
            switch (pending)
            {
                case PendingEvent.Started:
                    Started?.Invoke(this, EventArgs.Empty);
                    break;
                case PendingEvent.Stopped:
                    Stopped?.Invoke(this, EventArgs.Empty);
                    break;
                case PendingEvent.ToggledOn:
                    Toggled?.Invoke(this, true);
                    break;
                case PendingEvent.ToggledOff:
                    Toggled?.Invoke(this, false);
                    break;
            }
        }

        /// <summary>Resets to idle. Used by tests and on hook re-attach.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _keyPhysicallyDown = false;
                _holdStarted = false;
                _lastTapUpAt = null;
                _mode = Mode.Idle;
            }
        }
    }
}
