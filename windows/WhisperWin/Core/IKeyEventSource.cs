using System;

namespace WhisperWin.Core
{
    /// <summary>Direction of a raw key event coming from the keyboard hook.</summary>
    public enum KeyEventKind
    {
        KeyDown,
        KeyUp,
    }

    /// <summary>A single raw key event, decoupled from Win32's WH_KEYBOARD_LL payload.</summary>
    public readonly struct KeyEventArgs
    {
        public KeyEventArgs(int virtualKeyCode, KeyEventKind kind)
        {
            VirtualKeyCode = virtualKeyCode;
            Kind = kind;
        }

        public int VirtualKeyCode { get; }
        public KeyEventKind Kind { get; }
    }

    /// <summary>
    /// Abstraction over the source of raw key events (a real WH_KEYBOARD_LL hook in production,
    /// or a fake feed in tests) so <see cref="HotkeyStateMachine"/> can be unit tested without Win32.
    /// </summary>
    public interface IKeyEventSource
    {
        event EventHandler<KeyEventArgs>? KeyEvent;
    }
}
