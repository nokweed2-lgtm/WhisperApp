using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using WhisperWin.Core;
using WhisperWin.UI;

namespace WhisperWin
{
    public partial class App : Application
    {
        // Single-instance guard: two running instances would each install a WH_KEYBOARD_LL hook
        // and fight over the mic/hotkey — a named mutex prevents that (see design spec pitfalls).
        private const string MutexName = "Global\\WhisperWin-SingleInstance";
        private Mutex? _singleInstanceMutex;

        private TaskbarIcon? _trayIcon;
        private FloatingPill? _pill;
        private HotkeyManager? _hotkeyManager;
        private AudioRecorder? _recorder;
        private DictationController? _controller;
        private ConfigStore? _configStore;
        private CredentialStore? _credentialStore;
        private HttpClient? _httpClient;
        private string? _sharedDirectory;
        private readonly string _historyPath = HistoryStore.DefaultFilePath();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Whisper is already running (check the system tray).", "Whisper",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var exeDirectory = AppContext.BaseDirectory;
            _sharedDirectory = SharedPaths.ResolveSharedDirectory(exeDirectory);

            _httpClient = new HttpClient();
            _configStore = new ConfigStore(ConfigStore.DefaultFilePath());
            _credentialStore = new CredentialStore();

            _recorder = new AudioRecorder();
            var transcription = new TranscriptionService(_httpClient);
            var correction = new CorrectionService(_httpClient);
            var injector = new TextInjector(Dispatcher);

            _controller = new DictationController(
                _recorder,
                transcription,
                correction,
                injector,
                getConfig: () => _configStore.Load(),
                getApiKey: () => _credentialStore.ReadApiKey(),
                getSystemPrompt: BuildSystemPrompt);

            _pill = new FloatingPill();
            _controller.StageChanged += OnStageChanged;
            _controller.RawTranscribed += (s, raw) => DebugLog($"raw STT: {raw}");
            _controller.ErrorOccurred += (s, msg) => DebugLog($"error: {msg}");
            _recorder.RecordingCompleted += (s, ev) =>
                DebugLog($"audio captured: {ev.WavBytes.Length} bytes, {ev.Duration.TotalMilliseconds:F0}ms");
            _recorder.RecordingDiscarded += (s, ev) => DebugLog("audio discarded (< 300ms)");
            _recorder.DeviceError += (s, ex) => DebugLog($"device error: {ex.Message}");
            // InvokeAsync, never Invoke: these events originate on hook/timer/audio threads and
            // must not block waiting for the UI thread (see the deadlock note in HotkeyStateMachine).
            _recorder.Level += (s, ev) => Dispatcher.InvokeAsync(() => _pill.UpdateLevel(ev.Peak));

            SetupTrayIcon();
            SetupHotkey();
        }

        private string BuildSystemPrompt()
        {
            var config = _configStore!.Load();

            if (_sharedDirectory == null)
            {
                // Shared files not found — fall back to a minimal prompt rather than crashing.
                return PromptBuilder.BuildSystemPrompt(
                    "Clean up this transcript. Return ONLY the corrected text.\n{{LANG_HINT}}",
                    Array.Empty<string>(),
                    Array.Empty<DictionaryPair>(),
                    config.Language);
            }

            var template = File.ReadAllText(SharedPaths.PromptFilePath(_sharedDirectory));
            // DictionaryStore.Load already falls back to an empty DictionaryFile on a missing,
            // locked, or corrupt file (e.g. a torn write from OneDrive syncing the Mac and
            // Windows apps at the same time) — reuse it here instead of deserializing raw JSON
            // so a bad dictionary.json degrades to "no dictionary this time" instead of crashing
            // the whole dictation pipeline.
            var dictionaryPath = SharedPaths.DictionaryFilePath(_sharedDirectory);
            var dictionary = DictionaryStore.Load(dictionaryPath);
            if (dictionary.Entries.Count == 0 && dictionary.Pairs.Count == 0 && File.Exists(dictionaryPath))
            {
                DebugLog($"dictionary unreadable or empty at {dictionaryPath}, continuing without it");
            }

            return PromptBuilder.BuildSystemPrompt(template, dictionary.Entries, dictionary.Pairs, config.Language);
        }

        // Lightweight diagnostic trail (%LOCALAPPDATA%\WhisperWin\debug.log): the pipeline has
        // deliberate silent paths (empty transcript → Idle) and Windows can suppress error
        // balloons, so without this a "nothing happened" report is undebuggable.
        internal static void DebugLog(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhisperWin");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "debug.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never take the app down.
            }
        }

        private void OnStageChanged(object? sender, DictationStageChangedEventArgs e)
        {
            DebugLog($"stage={e.Stage} message={e.Message}");
            Dispatcher.InvokeAsync(() =>
            {
                if (e.Stage == DictationStage.Done)
                {
                    // Done fires once per successful pipeline run with the final (post-correction,
                    // or raw-on-fallback) pasted text as the message — the same hook point
                    // HistoryView.swift's append() uses on the Mac side. Appending here rather than
                    // inside DictationController keeps Core decoupled from the history feature.
                    // Kept above the _pill guard below: history recording must not depend on the
                    // floating-pill UI existing.
                    if (!string.IsNullOrWhiteSpace(e.Message))
                    {
                        HistoryStore.Append(_historyPath, e.Message);
                    }
                }

                if (_pill == null)
                {
                    return;
                }

                if (e.Stage == DictationStage.Recording)
                {
                    _pill.PositionBottomCenter();
                    _pill.Show();
                }

                _pill.SetStage(e.Stage);

                if (e.Stage == DictationStage.Idle || e.Stage == DictationStage.Error)
                {
                    if (e.Stage == DictationStage.Error && e.Message != null)
                    {
                        _trayIcon?.ShowBalloonTip("Whisper", e.Message, BalloonIcon.Warning);
                        if (e.Message.Contains("API key"))
                        {
                            OpenSettings();
                        }
                    }
                    _pill.Hide();
                }
            });
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Whisper — hold Right Ctrl to dictate",
            };

            var iconResource = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (iconResource != null)
            {
                using var stream = iconResource.Stream;
                _trayIcon.Icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
            }

            var contextMenu = new System.Windows.Controls.ContextMenu();
            var historyItem = new System.Windows.Controls.MenuItem { Header = "History" };
            historyItem.Click += (s, e) => OpenHistory();
            var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
            settingsItem.Click += (s, e) => OpenSettings();
            var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
            quitItem.Click += (s, e) => Shutdown();

            contextMenu.Items.Add(historyItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            contextMenu.Items.Add(quitItem);
            _trayIcon.ContextMenu = contextMenu;
        }

        private void OpenSettings()
        {
            var dictionaryPath = _sharedDirectory != null ? SharedPaths.DictionaryFilePath(_sharedDirectory) : null;
            var window = new SettingsWindow(_configStore!, _credentialStore!, _httpClient!, dictionaryPath);
            window.ShowDialog();
        }

        private void OpenHistory()
        {
            var window = new HistoryWindow(_historyPath);
            window.ShowDialog();
        }

        private void SetupHotkey()
        {
            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.Started += (s, e) => _controller!.StartRecording();
            _hotkeyManager.Stopped += (s, e) => _controller!.StopRecording();
            _hotkeyManager.Toggled += (s, isOn) =>
            {
                if (isOn)
                {
                    _controller!.StartRecording();
                }
                else
                {
                    _controller!.StopRecording();
                }
            };
            _hotkeyManager.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyManager?.Dispose();
            _recorder?.Dispose();
            _httpClient?.Dispose();
            _trayIcon?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
