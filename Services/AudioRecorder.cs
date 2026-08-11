using NAudio.Wave;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Records audio from the default microphone to a WAV file using NAudio.
    /// One instance is used for a single start/stop recording cycle.
    /// </summary>
    public class AudioRecorder : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private bool _disposed;

        public bool IsRecording { get; private set; }

        public event EventHandler? RecordingStopped;

        /// <summary>
        /// Starts recording using the given sample rate / bits per sample (mono).
        /// Defaults to a high quality format (44100 Hz, 16-bit) so the raw
        /// take retains as much quality as possible; downsampling to the
        /// iRacing-style format (e.g. 5512 Hz, 8-bit) only happens later, when
        /// previewing/playing back or exporting the pack.
        /// </summary>
        public void Start(string outputFilePath, int sampleRate = 44100, int bitsPerSample = 16)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("A recording is already in progress.");
            }

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(sampleRate, bitsPerSample, 1)
            };

            _writer = new WaveFileWriter(outputFilePath, _waveIn.WaveFormat);

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            IsRecording = true;
        }

        public void Stop()
        {
            _waveIn?.StopRecording();
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            IsRecording = false;

            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            _waveIn?.Dispose();
            _waveIn = null;

            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (IsRecording)
            {
                Stop();
            }

            _writer?.Dispose();
            _waveIn?.Dispose();
        }
    }
}
