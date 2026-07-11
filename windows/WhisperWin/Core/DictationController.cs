using System;
using System.Threading.Tasks;

namespace WhisperWin.Core
{
    public sealed class DictationStageChangedEventArgs : EventArgs
    {
        public DictationStageChangedEventArgs(DictationStage stage, string? message)
        {
            Stage = stage;
            Message = message;
        }

        public DictationStage Stage { get; }
        public string? Message { get; }
    }

    /// <summary>
    /// Orchestrates the record → transcribe → correct → paste pipeline, mirroring
    /// Sources/DictationController.swift. Depends only on the injected services/abstractions so
    /// it can be unit tested with fakes; the only Win32-touching piece is
    /// <see cref="TextInjector"/>, which is injected and can be swapped for a no-op in tests.
    /// </summary>
    public sealed class DictationController
    {
        private readonly AudioRecorder _recorder;
        private readonly TranscriptionService _transcription;
        private readonly CorrectionService _correction;
        private readonly TextInjector _injector;
        private readonly Func<AppConfig> _getConfig;
        private readonly Func<string?> _getApiKey;
        private readonly Func<string> _getSystemPrompt;

        private bool _processing;

        public DictationController(
            AudioRecorder recorder,
            TranscriptionService transcription,
            CorrectionService correction,
            TextInjector injector,
            Func<AppConfig> getConfig,
            Func<string?> getApiKey,
            Func<string> getSystemPrompt)
        {
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
            _correction = correction ?? throw new ArgumentNullException(nameof(correction));
            _injector = injector ?? throw new ArgumentNullException(nameof(injector));
            _getConfig = getConfig ?? throw new ArgumentNullException(nameof(getConfig));
            _getApiKey = getApiKey ?? throw new ArgumentNullException(nameof(getApiKey));
            _getSystemPrompt = getSystemPrompt ?? throw new ArgumentNullException(nameof(getSystemPrompt));

            _recorder.RecordingCompleted += (s, e) => _ = HandleAudioAsync(e.WavBytes);
            _recorder.RecordingDiscarded += (s, e) => RaiseStage(DictationStage.Idle, null);
            _recorder.DeviceError += (s, ex) => RaiseStage(DictationStage.Error, "Microphone unavailable");
        }

        public event EventHandler<DictationStageChangedEventArgs>? StageChanged;
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// Fires with the raw (post-sanitize, pre-correction) STT transcript. Diagnostic only —
        /// lets the debug log attribute added/changed words to Whisper vs the LLM correction step.
        /// </summary>
        public event EventHandler<string>? RawTranscribed;

        public bool IsRecording => _recorder.IsRecording;

        public void StartRecording()
        {
            if (_processing || _recorder.IsRecording)
            {
                return;
            }

            var apiKey = _getApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                RaiseStage(DictationStage.Error, "No API key configured");
                ErrorOccurred?.Invoke(this, "No API key configured");
                return;
            }

            _recorder.Start();
            if (_recorder.IsRecording)
            {
                RaiseStage(DictationStage.Recording, "Listening...");
            }
            else
            {
                RaiseStage(DictationStage.Error, "Microphone unavailable");
            }
        }

        public void StopRecording()
        {
            if (!_recorder.IsRecording)
            {
                return;
            }
            RaiseStage(DictationStage.Transcribing, "Processing...");
            _recorder.Stop();
        }

        private async Task HandleAudioAsync(byte[] wavBytes)
        {
            _processing = true;
            try
            {
                var config = _getConfig();
                var apiKey = _getApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    RaiseStage(DictationStage.Error, "No API key configured");
                    return;
                }

                RaiseStage(DictationStage.Transcribing, "Transcribing...");

                string rawText;
                try
                {
                    rawText = await _transcription.TranscribeAsync(wavBytes, apiKey!, config.Language).ConfigureAwait(false);
                }
                catch (TranscriptionException ex)
                {
                    RaiseStage(DictationStage.Error, "Transcription failed");
                    ErrorOccurred?.Invoke(this, $"Transcription failed: {ex.Message}");
                    return;
                }

                rawText = TranscriptSanitizer.StripSoundAnnotations(rawText);
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    RaiseStage(DictationStage.Idle, null);
                    return;
                }

                RawTranscribed?.Invoke(this, rawText);

                var finalText = rawText;
                if (config.UseCorrection)
                {
                    RaiseStage(DictationStage.Correcting, "AI correction...");
                    try
                    {
                        var systemPrompt = _getSystemPrompt();
                        finalText = await _correction.CorrectAsync(rawText, systemPrompt, apiKey!).ConfigureAwait(false);
                    }
                    catch (CorrectionException)
                    {
                        // Graceful degradation: never lose the user's words — paste the raw transcript.
                        finalText = rawText;
                    }
                }

                RaiseStage(DictationStage.Done, finalText);
                await _injector.PasteAsync(finalText).ConfigureAwait(false);
                RaiseStage(DictationStage.Idle, null);
            }
            finally
            {
                _processing = false;
            }
        }

        private void RaiseStage(DictationStage stage, string? message)
        {
            StageChanged?.Invoke(this, new DictationStageChangedEventArgs(stage, message));
        }
    }
}
