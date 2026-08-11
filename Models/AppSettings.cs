namespace iRacing_Spotter_Generator.Models
{
    /// <summary>
    /// Persisted local application settings.
    /// </summary>
    public class AppSettings
    {
        public string GoogleApiKey { get; set; } = string.Empty;

        /// <summary>
        /// The Google Cloud TTS voice name used by default for every message
        /// that doesn't have an explicit voice selected (e.g. "de-DE-Neural2-B").
        /// </summary>
        public string DefaultGoogleVoiceName { get; set; } = string.Empty;

        /// <summary>
        /// Sample rate (Hz) used when recording own takes. iRacing itself
        /// records/plays at 5512 Hz / 8-bit, giving the classic radio sound.
        /// </summary>
        public int RecordingSampleRate { get; set; } = 5512;

        /// <summary>
        /// Bits per sample used when recording own takes.
        /// </summary>
        public int RecordingBitsPerSample { get; set; } = 8;

        /// <summary>
        /// Whether a short radio-style noise/click burst is automatically added
        /// at the start and end of every generated sample (like a squelch tail).
        /// </summary>
        public bool SquelchEnabled { get; set; } = true;

        /// <summary>
        /// Duration (in milliseconds) of the noise/click burst added at the
        /// start and end of every generated sample.
        /// </summary>
        public int SquelchDurationMs { get; set; } = 150;

        /// <summary>
        /// Volume (0.0 - 1.0) of the noise/click burst.
        /// </summary>
        public double SquelchVolume { get; set; } = 0.5;

        /// <summary>
        /// UI language code ("de" or "en"). Defaults to German.
        /// </summary>
        public string Language { get; set; } = "de";
    }
}
