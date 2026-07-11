using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WhisperWin.Core
{
    /// <summary>
    /// Pastes corrected text into whatever application currently has focus: saves the current
    /// clipboard contents, sets the clipboard to the corrected text, simulates Ctrl+V via
    /// SendInput, then restores the previous clipboard contents after a short delay.
    ///
    /// Clipboard access on Windows requires an STA thread. The WPF UI thread already is STA, so
    /// all clipboard work is marshaled onto the given <see cref="Dispatcher"/> (normally
    /// Application.Current.Dispatcher) rather than done from a background thread directly.
    /// </summary>
    public sealed class TextInjector
    {
        private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(300);

        private readonly Dispatcher _dispatcher;

        public TextInjector(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>
        /// Copies <paramref name="text"/> to the clipboard and simulates Ctrl+V into the focused
        /// window. Restores the clipboard's previous contents afterwards. If the focused window is
        /// elevated (running as admin) SendInput cannot reach it — the text is left on the
        /// clipboard so the user can paste manually; callers should surface a balloon in that case.
        /// </summary>
        public async Task PasteAsync(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            string? previousClipboardText = null;
            bool hadPreviousText = false;

            await _dispatcher.InvokeAsync(() =>
            {
                if (Clipboard.ContainsText())
                {
                    previousClipboardText = Clipboard.GetText();
                    hadPreviousText = true;
                }
                SetClipboardTextWithRetry(text);
            });

            SendCtrlV();

            await Task.Delay(RestoreDelay);

            await _dispatcher.InvokeAsync(() =>
            {
                if (hadPreviousText && previousClipboardText != null)
                {
                    SetClipboardTextWithRetry(previousClipboardText);
                }
            });
        }

        /// <summary>
        /// Clipboard.SetText can throw a transient COMException if another process momentarily
        /// holds the clipboard open — retry a couple of times before giving up.
        /// </summary>
        private static void SetClipboardTextWithRetry(string text)
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch (COMException) when (attempt < maxAttempts)
                {
                    System.Threading.Thread.Sleep(30);
                }
            }
        }

        private static void SendCtrlV()
        {
            var inputs = new INPUT[4];

            inputs[0] = KeyDown(VK_CONTROL);
            inputs[1] = KeyDown(VK_V);
            inputs[2] = KeyUp(VK_V);
            inputs[3] = KeyUp(VK_CONTROL);

            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            var foregroundExe = TryGetForegroundExeName();
            if (sent != inputs.Length)
            {
                App.DebugLog($"paste: SendInput failed, sent {sent}/{inputs.Length} to {foregroundExe}, win32 error {Marshal.GetLastWin32Error()}");
            }
            else
            {
                App.DebugLog($"paste: sent {sent}/{inputs.Length} to {foregroundExe}");
            }
        }

        /// <summary>
        /// Best-effort lookup of the foreground window's process name, purely for diagnosing
        /// paste-delivery reports (e.g. "paste works in Notepad but not VS Code"). Never allowed
        /// to throw out of here — a failure to identify the foreground app must not break paste.
        /// </summary>
        private static string TryGetForegroundExeName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    return "(unknown)";
                }

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                {
                    return "(unknown)";
                }

                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                return process.ProcessName + ".exe";
            }
            catch
            {
                return "(unknown)";
            }
        }

        /// <summary>
        /// KEYBDINPUT with only wVk set (no scan code) is enough for most apps, but some
        /// Chromium/Electron surfaces and other apps that read scan codes directly can ignore
        /// synthetic input that lacks one. Stamping wScan via MapVirtualKey and flagging
        /// KEYEVENTF_SCANCODE makes the event look like a "real" keypress to more listeners.
        /// </summary>
        private static INPUT KeyDown(ushort vk) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                    dwFlags = KEYEVENTF_SCANCODE,
                },
            },
        };

        private static INPUT KeyUp(ushort vk) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                    dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP,
                },
            },
        };

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint MAPVK_VK_TO_VSC = 0;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // MOUSEINPUT must be part of the union even though we never send mouse input: it is the
        // largest union member, and without it Marshal.SizeOf<INPUT>() comes out 32 instead of
        // the 40 bytes Win32 expects on x64 — SendInput then rejects the whole batch (returns 0).
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
