using System;
using System.IO;
using NAudio;
using NAudio.Wave;

namespace WhisperWin.Core
{
    public sealed class AudioLevelEventArgs : EventArgs
    {
        public AudioLevelEventArgs(float peak)
        {
            Peak = peak;
        }

        /// <summary>Peak amplitude for the last buffer, normalized to [0, 1].</summary>
        public float Peak { get; }
    }

    public sealed class RecordingCompletedEventArgs : EventArgs
    {
        public RecordingCompletedEventArgs(byte[] wavBytes, TimeSpan duration)
        {
            WavBytes = wavBytes;
            Duration = duration;
        }

        public byte[] WavBytes { get; }
        public TimeSpan Duration { get; }
    }

    /// <summary>
    /// Records the default microphone as 16kHz / 16-bit / mono PCM into an in-memory WAV buffer
    /// using NAudio's WaveInEvent. Raises level events (for the floating pill's waveform) while
    /// recording and a completed event with the final WAV bytes on stop.
    ///
    /// Recordings shorter than <see cref="MinimumDuration"/> are discarded (RecordingDiscarded
    /// fires instead of RecordingCompleted) per the spec ("< 300ms → discard silently").
    /// </summary>
    public sealed class AudioRecorder : IDisposable
    {
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(300);
        private const int SampleRate = 16000;
        private const int Bits = 16;
        private const int Channels = 1;

        private WaveInEvent? _waveIn;
        private MemoryStream? _buffer;
        private WaveFileWriter? _writer;
        private DateTime _startedAt;
        private bool _disposed;

        public event EventHandler<AudioLevelEventArgs>? Level;
        public event EventHandler<RecordingCompletedEventArgs>? RecordingCompleted;
        public event EventHandler? RecordingDiscarded;
        public event EventHandler<Exception>? DeviceError;

        public bool IsRecording { get; private set; }

        public void Start()
        {
            if (IsRecording)
            {
                return;
            }

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(SampleRate, Bits, Channels),
                    BufferMilliseconds = 50,
                };
                _buffer = new MemoryStream();
                _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _waveIn.StartRecording();
                _startedAt = DateTime.UtcNow;
                IsRecording = true;
            }
            catch (Exception ex) when (ex is MmException || ex is InvalidOperationException)
            {
                CleanUp();
                DeviceError?.Invoke(this, ex);
            }
        }

        public void Stop()
        {
            if (!IsRecording || _waveIn == null)
            {
                return;
            }

            // StopRecording() is async and completes on OnRecordingStopped, which finalizes
            // the WAV writer and raises RecordingCompleted/RecordingDiscarded.
            _waveIn.StopRecording();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);

            var peak = ComputePeak(e.Buffer, e.BytesRecorded);
            Level?.Invoke(this, new AudioLevelEventArgs(peak));
        }

        private static float ComputePeak(byte[] buffer, int bytesRecorded)
        {
            short max = 0;
            for (var i = 0; i + 1 < bytesRecorded; i += 2)
            {
                var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                var abs = Math.Abs((int)sample);
                if (abs > max)
                {
                    max = (short)abs;
                }
            }
            return max / (float)short.MaxValue;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            IsRecording = false;
            var duration = DateTime.UtcNow - _startedAt;

            _writer?.Flush();
            var bytes = _buffer?.ToArray() ?? Array.Empty<byte>();

            CleanUp();

            if (e.Exception != null)
            {
                DeviceError?.Invoke(this, e.Exception);
                return;
            }

            if (duration < MinimumDuration)
            {
                RecordingDiscarded?.Invoke(this, EventArgs.Empty);
                return;
            }

            RecordingCompleted?.Invoke(this, new RecordingCompletedEventArgs(bytes, duration));
        }

        private void CleanUp()
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }

            _writer?.Dispose();
            _writer = null;
            _buffer = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            CleanUp();
        }
    }
}
