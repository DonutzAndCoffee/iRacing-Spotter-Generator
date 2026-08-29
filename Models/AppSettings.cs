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
        /// Whether the flexible radio effect (bandpass filter + optional
        /// distortion) is automatically applied to every generated sample.
        /// </summary>
        public bool RadioEffectEnabled { get; set; } = false;

        /// <summary>
        /// Low cutoff frequency (Hz) of the radio effect's bandpass filter;
        /// frequencies below this are attenuated.
        /// </summary>
        public int RadioEffectLowCutHz { get; set; } = 300;

        /// <summary>
        /// High cutoff frequency (Hz) of the radio effect's bandpass filter;
        /// frequencies above this are attenuated.
        /// </summary>
        public int RadioEffectHighCutHz { get; set; } = 3000;

        /// <summary>
        /// Amount of soft-clip distortion applied by the radio effect
        /// (0.0 = none, 1.0 = strong).
        /// </summary>
        public double RadioEffectDistortion { get; set; } = 0.2;

        /// <summary>
        /// Whether synthetic push-to-talk (PTT) start/stop beep tones are
        /// automatically added at the start and end of every generated sample.
        /// This is an independent, additional option to <see cref="SquelchEnabled"/>.
        /// </summary>
        public bool PttEnabled { get; set; } = false;

        /// <summary>
        /// Duration (in milliseconds) of each PTT beep.
        /// </summary>
        public int PttDurationMs { get; set; } = 200;

        /// <summary>
        /// Volume (0.0 - 1.0) of the PTT beeps.
        /// </summary>
        public double PttVolume { get; set; } = 0.5;

        /// <summary>
        /// Frequency (Hz) of the synthesized "stop talking" Roger Beep.
        /// </summary>
        public int PttEndFrequencyHz { get; set; } = 800;

        /// <summary>
        /// Optional custom WAV file used instead of the synthesized Roger Beep.
        /// </summary>
        public string? PttEndFilePath { get; set; }

        /// <summary>
        /// Output volume/gain applied to every generated sample (1.0 = unchanged,
        /// &gt;1.0 = louder, &lt;1.0 = quieter). Useful because iRacing plays
        /// spotter clips comparatively quiet.
        /// </summary>
        public double OutputVolume { get; set; } = 1.0;

        /// <summary>
        /// UI language code ("de" or "en"). Defaults to German.
        /// </summary>
        public string Language { get; set; } = "de";

        /// <summary>
        /// Optional path to a user-recorded (or imported) WAV file used by the
        /// settings panel's "Test" buttons instead of the built-in synthetic
        /// test tone, so settings changes can be previewed against the user's
        /// own voice/microphone, analogous to a message's recorded "Take".
        /// </summary>
        public string? TestRecordingPath { get; set; }
    }
}
